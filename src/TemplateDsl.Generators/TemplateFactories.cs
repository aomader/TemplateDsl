using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace TemplateDsl.Generators;

/// <summary>
/// The factories of a project's templates: a static class <c>Templates</c> in its root namespace with a method per
/// constructor, named like the template, so that <c>using static App.Templates;</c> lets templates be written as
/// <c>Card("Hello")</c> instead of <c>new Card("Hello")</c>. The generator can't see the methods it writes, so the
/// translator recognizes a call of one by its name, with <see cref="FindTemplate"/>.
/// </summary>
internal static class TemplateFactories
{
    /// <summary>The MSBuild property that names the class: <c>Name</c>, in the root namespace, or <c>Namespace.Name</c>.</summary>
    public const string ClassProperty = "build_property.TemplateDslTemplatesClass";

    private const string RootNamespaceProperty = "build_property.RootNamespace";
    private const string DefaultName = "Templates";

    public static readonly SymbolDisplayFormat TypeFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>
    /// Whether a template gets factories: one that can be created, which is a partial class or struct that isn't
    /// abstract, and that code outside of it can see. A template that isn't partial is an error already.
    /// </summary>
    public static bool HasFactories(INamedTypeSymbol type, CancellationToken cancellationToken) =>
        type is { TypeKind: TypeKind.Class or TypeKind.Struct, IsStatic: false, IsAbstract: false, IsRefLikeType: false, ContainingType: null, IsFileLocal: false }
        && type.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal
        && type.AllInterfaces.Any(FormatReader.IsTemplateInterface)
        && type.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax(cancellationToken) is TypeDeclarationSyntax declaration && declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
        && Constructors(type).Any();

    /// <summary>
    /// The template a call of a factory of this project creates, such as <c>Card("Hello")</c> or <c>T.Card("Hello")</c>,
    /// found by its name. Null if the call is something else, or if more than one template has the name, which then
    /// gets no factory. A template with type parameters is returned without type arguments, unless the call has them.
    /// </summary>
    public static INamedTypeSymbol? FindTemplate(SemanticModel model, InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
    {
        // Only a call that C# can't resolve: one that it can is something else, such as a method of the app's own.
        var info = model.GetSymbolInfo(invocation, cancellationToken);

        if (info.Symbol is not null || info.CandidateSymbols.Any(candidate => candidate is IMethodSymbol))
        {
            return null;
        }

        var name = invocation.Expression switch
        {
            SimpleNameSyntax simple => simple.Identifier.ValueText,

            // T.Card(...) or App.Templates.Card(...): the class isn't there yet, so neither is what names it.
            MemberAccessExpressionSyntax { Name: var member, Expression: var receiver }
                when model.GetSymbolInfo(receiver, cancellationToken).Symbol is null or INamespaceSymbol => member.Identifier.ValueText,

            _ => null,
        };

        if (name is null)
        {
            return null;
        }

        var templates = model.Compilation.GetSymbolsWithName(name, SymbolFilter.Type, cancellationToken)
            .OfType<INamedTypeSymbol>()
            .Where(type => HasFactories(type, cancellationToken))
            .ToArray();

        if (templates.Length != 1)
        {
            return null;
        }

        var template = templates[0];

        // Table<Order>(...) names its type arguments, which C# can bind, as they are types of their own.
        var generic = invocation.Expression switch
        {
            GenericNameSyntax genericName => genericName,
            MemberAccessExpressionSyntax { Name: GenericNameSyntax genericName } => genericName,
            _ => null,
        };

        if (generic is not null && generic.TypeArgumentList.Arguments.Count == template.Arity)
        {
            var arguments = generic.TypeArgumentList.Arguments.Select(argument => model.GetTypeInfo(argument, cancellationToken).Type).ToArray();

            if (arguments.All(argument => argument is not null and not IErrorTypeSymbol))
            {
                return template.Construct(arguments!);
            }
        }

        return template;
    }

    /// <summary>The factories of one template, or null if it gets none.</summary>
    public static FactoryModel? Read(INamedTypeSymbol type, TypeDeclarationSyntax declaration, CancellationToken cancellationToken)
    {
        if (!HasFactories(type, cancellationToken))
        {
            return null;
        }

        var name = type.Name;
        var typeName = type.ToDisplayString(TypeFormat);
        var typeParameters = type.TypeParameters.Length == 0 ? "" : $"<{string.Join(", ", type.TypeParameters.Select(parameter => parameter.Name))}>";
        var constraints = string.Concat(type.TypeParameters.Select(Constraint));
        var cref = typeName.Replace('<', '{').Replace('>', '}');
        var methods = new StringBuilder();
        var isPublic = false;

        foreach (var constructor in Constructors(type))
        {
            var isMethodPublic = type.DeclaredAccessibility == Accessibility.Public && constructor.DeclaredAccessibility == Accessibility.Public;
            isPublic |= isMethodPublic;

            methods.Append("    /// <summary>A new <see cref=\"").Append(cref).Append("\"/>, written without <c>new</c>.</summary>\n");
            methods.Append("    ").Append(isMethodPublic ? "public" : "internal").Append(" static ").Append(typeName).Append(' ')
                .Append(name).Append(typeParameters).Append('(')
                .Append(string.Join(", ", constructor.Parameters.Select(Parameter)))
                .Append(')').Append(constraints).Append(" =>\n        new(")
                .Append(string.Join(", ", constructor.Parameters.Select(parameter => Identifier(parameter.Name))))
                .Append(");\n\n");
        }

        var location = declaration.Identifier.GetLocation();
        return new FactoryModel(name, type.ToDisplayString(), methods.ToString(), isPublic, location.SourceTree!.FilePath, location.SourceSpan, location.GetLineSpan().Span);
    }

    /// <summary>
    /// The constructors code outside of the template can call. A struct always has one without parameters, which only
    /// counts if it is the only one, and a constructor needs to set the required members of the template, as
    /// <c>new(...)</c> doesn't. Parameters that a factory can't pass on as they are, such as out parameters, rule it out.
    /// </summary>
    private static IEnumerable<IMethodSymbol> Constructors(INamedTypeSymbol type)
    {
        var hasRequired = type.GetMembers().Any(member => member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true });

        return type.InstanceConstructors.Where(constructor =>
            constructor.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal
            && !(constructor.IsImplicitlyDeclared && type.IsValueType && type.InstanceConstructors.Length > 1)
            && (!hasRequired || constructor.GetAttributes().Any(attribute => attribute.AttributeClass?.Name == "SetsRequiredMembersAttribute"))
            && constructor.Parameters.All(parameter =>
                parameter.RefKind is RefKind.None or RefKind.In
                && parameter.Type.TypeKind is not (TypeKind.Pointer or TypeKind.FunctionPointer)));
    }

    private static string Parameter(IParameterSymbol parameter)
    {
        var text = new StringBuilder();

        if (parameter.IsParams)
        {
            text.Append("params ");
        }

        if (parameter.RefKind == RefKind.In)
        {
            text.Append("in ");
        }

        text.Append(parameter.Type.ToDisplayString(TypeFormat)).Append(' ').Append(Identifier(parameter.Name));

        if (parameter.HasExplicitDefaultValue)
        {
            text.Append(" = ").Append(DefaultValue(parameter.Type, parameter.ExplicitDefaultValue));
        }

        return text.ToString();
    }

    /// <summary>A default value as C# writes it, which has to be a constant, so with the type it is for.</summary>
    private static string DefaultValue(ITypeSymbol type, object? value)
    {
        var underlying = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ? nullable.TypeArguments[0] : type;

        return value switch
        {
            null => "default",
            string text => SymbolDisplay.FormatLiteral(text, quote: true),
            char character => SymbolDisplay.FormatLiteral(character, quote: true),
            bool flag => flag ? "true" : "false",
            _ when underlying.TypeKind == TypeKind.Enum =>
                $"({underlying.ToDisplayString(TypeFormat)})({Convert.ToString(value, CultureInfo.InvariantCulture)})",
            float single when float.IsNaN(single) || float.IsInfinity(single) => NonFinite("Single", single),
            double number when double.IsNaN(number) || double.IsInfinity(number) => NonFinite("Double", number),
            _ => $"({underlying.ToDisplayString(TypeFormat)})({SymbolDisplay.FormatPrimitive(value, quoteStrings: false, useHexadecimalNumbers: false)}{Suffix(value)})",
        };
    }

    private static string NonFinite(string type, double value) =>
        $"global::System.{type}.{(double.IsNaN(value) ? "NaN" : value > 0 ? "PositiveInfinity" : "NegativeInfinity")}";

    private static string Suffix(object value) => value switch
    {
        float => "F",
        double => "D",
        decimal => "M",
        long => "L",
        ulong => "UL",
        uint => "U",
        _ => "",
    };

    internal static string Constraint(ITypeParameterSymbol parameter)
    {
        var constraints = new List<string>();

        if (parameter.HasReferenceTypeConstraint)
        {
            constraints.Add(parameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
        }

        if (parameter.HasUnmanagedTypeConstraint)
        {
            constraints.Add("unmanaged");
        }
        else if (parameter.HasValueTypeConstraint)
        {
            constraints.Add("struct");
        }

        if (parameter.HasNotNullConstraint)
        {
            constraints.Add("notnull");
        }

        constraints.AddRange(parameter.ConstraintTypes.Select(type => type.ToDisplayString(TypeFormat)));

        if (parameter.HasConstructorConstraint)
        {
            constraints.Add("new()");
        }

        return constraints.Count == 0 ? "" : $"\n        where {parameter.Name} : {string.Join(", ", constraints)}";
    }

    private static string Identifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    /// <summary>
    /// Where the class goes: <c>Templates</c> in the root namespace of the project, or what the property names. A name
    /// that is taken can't be used, as C# would report the class as a second declaration of it, in generated code.
    /// </summary>
    public static FactorySettings ReadSettings(AnalyzerConfigOptions options, Compilation compilation)
    {
        var configured = options.TryGetValue(ClassProperty, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
        var rootNamespace = options.TryGetValue(RootNamespaceProperty, out var root) && !string.IsNullOrWhiteSpace(root) ? root.Trim() : compilation.AssemblyName ?? "";
        // A name without a namespace goes in the root namespace, like the default one.
        var name = configured ?? DefaultName;
        var fullName = name.IndexOf('.') >= 0 || rootNamespace.Length == 0 ? name : $"{rootNamespace}.{name}";
        var parts = fullName.Split('.');
        var problem = parts.All(SyntaxFacts.IsValidIdentifier) ? null : "it isn't a valid name";

        if (problem is null && FindNamespace(compilation.Assembly.GlobalNamespace, parts) is not null)
        {
            problem = "a namespace has that name";
        }

        if (problem is null && compilation.Assembly.GetTypeByMetadataName(fullName) is { } type && !type.IsStatic)
        {
            problem = "a type that isn't a static class has that name";
        }

        var lastDot = fullName.LastIndexOf('.');
        return new FactorySettings(lastDot < 0 ? null : fullName.Substring(0, lastDot), fullName.Substring(lastDot + 1), fullName, problem);
    }

    private static INamespaceSymbol? FindNamespace(INamespaceSymbol global, string[] parts)
    {
        var current = global;

        foreach (var part in parts)
        {
            current = current.GetNamespaceMembers().FirstOrDefault(member => member.Name == part);

            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    /// <summary>
    /// Writes the class with the factories of every template whose name no other template of the project has, and
    /// reports the templates that share one. Null if there is nothing to write.
    /// </summary>
    public static string? Emit(FactorySettings settings, IReadOnlyList<FactoryModel> factories, Action<Diagnostic> report)
    {
        if (factories.Count == 0)
        {
            return null;
        }

        if (settings.Problem is not null)
        {
            report(Diagnostic.Create(Diagnostics.InvalidTemplatesClass, Location.None, settings.FullName, settings.Problem));
            return null;
        }

        var unique = new List<FactoryModel>();

        foreach (var group in factories.GroupBy(factory => factory.Name, StringComparer.Ordinal))
        {
            var types = group.Select(factory => factory.FullName).Distinct().OrderBy(name => name, StringComparer.Ordinal).ToArray();

            if (types.Length == 1)
            {
                // A method can't have the name of its class, so a template with it has no factory.
                if (group.Key != settings.Name)
                {
                    unique.Add(group.First());
                }

                continue;
            }

            foreach (var factory in group)
            {
                report(Diagnostic.Create(Diagnostics.SameTemplateName, factory.ToLocation(), group.Key, string.Join("', '", types)));
            }
        }

        if (unique.Count == 0)
        {
            return null;
        }

        var source = new StringBuilder("// <auto-generated/>\n#nullable enable\n#pragma warning disable\n\n");

        if (settings.Namespace is not null)
        {
            source.Append("namespace ").Append(settings.Namespace).Append(";\n\n");
        }

        source.Append("/// <summary>\n/// The templates of this project, created without <c>new</c>: with <c>using static ").Append(settings.FullName)
            .Append(";</c>, a template\n/// is written <c>Card(\"Hello\")</c> instead of <c>new Card(\"Hello\")</c>.\n/// </summary>\n");
        source.Append(unique.Any(factory => factory.IsPublic) ? "public" : "internal").Append(" static partial class ").Append(settings.Name).Append("\n{\n");

        foreach (var factory in unique.OrderBy(factory => factory.Name, StringComparer.Ordinal))
        {
            source.Append(factory.Methods);
        }

        return source.ToString().TrimEnd('\n') + "\n}\n";
    }
}

/// <summary>Where the class of the factories goes, and why it can't, if it can't.</summary>
internal sealed record FactorySettings(string? Namespace, string Name, string FullName, string? Problem);

/// <summary>The factories of one template, as code, with where it is declared to report a name it shares.</summary>
internal sealed record FactoryModel(string Name, string FullName, string Methods, bool IsPublic, string Path, TextSpan Span, LinePositionSpan LineSpan)
{
    public Location ToLocation() => Location.Create(Path, Span, LineSpan);
}
