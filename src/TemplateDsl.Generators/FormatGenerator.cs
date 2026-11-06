using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TemplateDsl.Generators;

/// <summary>
/// Turns the format attributes into code, so that a format is only a set of declarations: a format's
/// escape methods, plus the members that let Build type-check: the string conversion, each node's
/// brackets, the bodies and overloads of attribute methods and the <c>Each</c> methods of its factory class. Those are never executed.
/// </summary>
[Generator]
public sealed class FormatGenerator : IIncrementalGenerator
{
    private const string BufferWriter = "global::System.Buffers.IBufferWriter<byte>";
    private const string Hidden = "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]";
    private const string NotExecutable = "throw global::TemplateDsl.TemplateBuild.NotExecutable()";

    /// <summary>On every member that only exists for Build, so that code coverage only counts what renders.</summary>
    private const string Excluded = "[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]";

    /// <summary>
    /// All the types that convert to a node, with and without null. A constant like 5 needs the plain ones to be exact.
    /// Every smaller integer and char needs its own entry too, or it would convert to a number further down the list.
    /// </summary>
    private static readonly string[] FormattedTypes =
    [
        "sbyte", "byte", "short", "ushort", "int", "uint", "long", "ulong", "float", "double", "decimal", "char",
        "global::System.DateTime", "global::System.DateTimeOffset", "global::System.DateOnly", "global::System.TimeOnly",
        "global::System.TimeSpan", "global::System.Guid",
    ];

    /// <summary>Always a class: records can't derive from <c>TemplateNode</c>, and factory classes are static.</summary>
    private sealed record Declaration(string? Namespace, string Name, string TypeParameters = "", int Arity = 0);

    private sealed record FormatModel(Declaration Declaration, EquatableArray<EscapeRule> Rules, bool FormatsValues, DiagnosticInfo? Diagnostic);

    private sealed record NodeModel(Declaration Declaration, bool IsText, string Members, DiagnosticInfo? Diagnostic);

    /// <param name="Declaration">The class that declares the method.</param>
    /// <param name="Code">The body of the partial method and the overloads it gets, or null if it isn't a partial method.</param>
    /// <param name="Diagnostic">What is wrong with the declaration, if anything.</param>
    private sealed record AttributeMethodModel(Declaration Declaration, string? Code, DiagnosticInfo? Diagnostic);

    private sealed record TagsModel(Declaration Declaration, string? FormatType, DiagnosticInfo? Diagnostic);

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var formats = context.SyntaxProvider.ForAttributeWithMetadataName(
            "TemplateDsl.TemplateFormatAttribute",
            static (node, _) => node is ClassDeclarationSyntax,
            static (ctx, _) => AnalyzeFormat(ctx));

        var nodes = context.SyntaxProvider.ForAttributeWithMetadataName(
            "TemplateDsl.NodeAttribute",
            static (node, _) => node is ClassDeclarationSyntax,
            static (ctx, _) => AnalyzeNode(ctx));

        var textNodes = context.SyntaxProvider.ForAttributeWithMetadataName(
            "TemplateDsl.TextNodeAttribute",
            static (node, _) => node is ClassDeclarationSyntax,
            static (ctx, _) => AnalyzeNode(ctx));

        var tags = context.SyntaxProvider.ForAttributeWithMetadataName(
            "TemplateDsl.TemplateTagsAttribute",
            static (node, _) => node is ClassDeclarationSyntax,
            static (ctx, _) => AnalyzeTags(ctx));

        var attributeMethods = context.SyntaxProvider.ForAttributeWithMetadataName(
            "TemplateDsl.AttrAttribute",
            static (node, _) => node is MethodDeclarationSyntax,
            static (ctx, _) => AnalyzeAttributeMethod(ctx, isList: false));

        var listMethods = context.SyntaxProvider.ForAttributeWithMetadataName(
            "TemplateDsl.AttrListAttribute",
            static (node, _) => node is MethodDeclarationSyntax,
            static (ctx, _) => AnalyzeAttributeMethod(ctx, isList: true));

        // One file per class, holding all its attribute methods.
        context.RegisterSourceOutput(attributeMethods.Collect().Combine(listMethods.Collect()), static (ctx, methods) =>
        {
            foreach (var group in methods.Left.Concat(methods.Right).GroupBy(method => method.Declaration))
            {
                foreach (var method in group.Where(method => method.Diagnostic is not null))
                {
                    ctx.ReportDiagnostic(method.Diagnostic!.ToDiagnostic());
                }

                var source = StartFile(group.Key);

                foreach (var method in group)
                {
                    source.Append(method.Code);
                }

                ctx.AddSource(GetHintName(group.Key, "Attributes"), source.Append("}\n").ToString());
            }
        });

        context.RegisterSourceOutput(formats, static (ctx, format) =>
        {
            if (format.Diagnostic is not null)
            {
                ctx.ReportDiagnostic(format.Diagnostic.ToDiagnostic());
                return;
            }

            ctx.AddSource(GetHintName(format.Declaration, "Format"), EmitFormat(format));
        });

        Action<SourceProductionContext, NodeModel> emitNode = EmitNodeOutput;
        context.RegisterSourceOutput(nodes, emitNode);
        context.RegisterSourceOutput(textNodes, emitNode);

        context.RegisterSourceOutput(tags, static (ctx, tag) =>
        {
            if (tag.Diagnostic is not null)
            {
                ctx.ReportDiagnostic(tag.Diagnostic.ToDiagnostic());
                return;
            }

            ctx.AddSource(GetHintName(tag.Declaration, "Tags"), EmitTags(tag));
        });
    }

    private static void EmitNodeOutput(SourceProductionContext context, NodeModel node)
    {
        if (node.Diagnostic is not null)
        {
            context.ReportDiagnostic(node.Diagnostic.ToDiagnostic());
        }
        else if (!node.IsText)
        {
            context.AddSource(GetHintName(node.Declaration, "Node"), EmitNode(node));
        }
    }

    private static FormatModel AnalyzeFormat(GeneratorAttributeSyntaxContext context)
    {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var rules = FormatReader.ReadEscapeRules(symbol);

        return new FormatModel(
            GetDeclaration(symbol),
            new EquatableArray<EscapeRule>(rules),
            FormatReader.FormatsValues(symbol),
            CheckShape(context, symbol) ?? CheckBase(context, symbol) ?? CheckTextNode(context, symbol) ?? CheckRules(context, symbol, rules));
    }

    private static DiagnosticInfo? CheckBase(GeneratorAttributeSyntaxContext context, INamedTypeSymbol symbol) =>
        FormatReader.BaseTypes(symbol).Any(current => FormatReader.IsCoreType(current, "TemplateNode"))
            ? null
            : Invalid(context, $"'{symbol.Name}' must derive from TemplateNode");

    /// <summary>Plain strings become this node, which says how they are escaped, so without it they wouldn't be.</summary>
    private static DiagnosticInfo? CheckTextNode(GeneratorAttributeSyntaxContext context, INamedTypeSymbol symbol) =>
        FormatReader.ReadTextNode(symbol) is { } textNode && FormatReader.ReadNode(textNode) is { IsText: true }
            ? null
            : Invalid(context, $"'{symbol.Name}' must name a class marked with [TextNode] as its text node");

    private static DiagnosticInfo? CheckRules(GeneratorAttributeSyntaxContext context, INamedTypeSymbol symbol, EscapeRule[] rules)
    {
        foreach (var rule in rules)
        {
            var replacements = rule.Replacements.ToArray();
            var hasOnlyCharacters = Enumerable.Range(0, replacements.Length).All(i => i % 2 == 1 || replacements[i].Length == 1);

            if (!SyntaxFacts.IsValidIdentifier(rule.Context) || replacements.Length % 2 != 0 || !hasOnlyCharacters)
            {
                return Invalid(context, $"'{symbol.Name}' has an invalid escape rule '{rule.Context}': it needs a name usable in code and pairs of a single character and its replacement");
            }
        }

        return null;
    }

    private static NodeModel AnalyzeNode(GeneratorAttributeSyntaxContext context)
    {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var node = FormatReader.ReadNode(symbol)!;
        var format = FormatReader.FindFormat(symbol);

        var diagnostic = CheckShape(context, symbol);

        if (diagnostic is null && format is null)
        {
            diagnostic = Invalid(context, $"'{symbol.Name}' doesn't derive from a class marked with [TemplateFormat]");
        }

        // The escape contexts of attributes are checked where they are declared.
        if (diagnostic is null && node.TextEscape is { } escape && !IsKnownEscape(format!, escape))
        {
            diagnostic = Invalid(context, $"'{symbol.Name}' uses the escape context '{escape}', which its format doesn't declare");
        }

        // Ends the chain, so attributes can't follow children.
        var formatType = format?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var brackets = node.IsVoid ? "" : $"    /// <summary>The children of this node in Build.</summary>\n    {Excluded}\n    public {formatType} this[params global::TemplateDsl.TemplateChildren<{formatType}> children] => {NotExecutable};\n";

        return new NodeModel(GetDeclaration(symbol), node.IsText, brackets, diagnostic);
    }

    private static bool IsKnownEscape(ITypeSymbol format, string escape) =>
        FormatReader.ReadEscapeRules(format).Any(rule => rule.Context == escape);

    private static AttributeMethodModel AnalyzeAttributeMethod(GeneratorAttributeSyntaxContext context, bool isList)
    {
        var method = (IMethodSymbol)context.TargetSymbol;
        var syntax = (MethodDeclarationSyntax)context.TargetNode;
        var type = method.ContainingType;
        var format = FormatReader.FindFormat(type);
        var name = $"{type.Name}.{method.Name}";
        var attribute = isList ? null : FormatReader.ReadAttribute(method);
        var escape = isList ? ReadListEscape(method) : attribute?.Escape;

        var isPartial = syntax.Modifiers.Any(SyntaxKind.PartialKeyword) && syntax.Body is null && syntax.ExpressionBody is null;
        var isTopLevelPartialClass = type.ContainingType is null && syntax.Parent is ClassDeclarationSyntax parent && parent.Modifiers.Any(SyntaxKind.PartialKeyword);

        string? problem =
            !isPartial || !isTopLevelPartialClass ? $"'{name}' must be a partial method without a body, in a top-level partial class, so that the generator can implement it"
            : format is null ? $"'{type.Name}' doesn't derive from a class marked with [TemplateFormat]"
            : isList && !FormatReader.HasAttributeListShape(method) ? $"'{name}' must take a string name and a string? value, to be a list of attributes"
            : attribute is { Kind: AttributeKind.Invalid } ? $"'{name}' must take one string, bool or value type that can format itself, such as int?, to be an attribute"
            : escape is not null && !IsKnownEscape(format, escape) ? $"'{name}' uses the escape context '{escape}', which its format doesn't declare"
            : null;

        var diagnostic = problem is null ? null : DiagnosticInfo.Create(Diagnostics.InvalidFormat, syntax.Identifier.GetLocation(), problem);

        // A partial method is always implemented, even if it is invalid, so that the only error is the one above.
        string? code = null;

        if (isPartial && isTopLevelPartialClass)
        {
            var formatsValues = format is not null && FormatReader.FormatsValues(format);

            code = EmitAttributeMethod(method, problem is null ? attribute : null, isList && problem is null, formatsValues);
        }

        return new AttributeMethodModel(GetDeclaration(type), code, diagnostic);
    }

    private static string? ReadListEscape(IMethodSymbol method) =>
        method.GetAttributes()
            .Where(a => FormatReader.IsAttribute(a, "AttrListAttribute"))
            .SelectMany(a => a.NamedArguments)
            .Where(argument => argument.Key == "Escape")
            .Select(argument => argument.Value.Value as string)
            .FirstOrDefault();

    private static TagsModel AnalyzeTags(GeneratorAttributeSyntaxContext context)
    {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var format = FormatReader.ReadTagsFormat(symbol);

        return new TagsModel(
            GetDeclaration(symbol),
            format?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            CheckShape(context, symbol)
                ?? (symbol.IsStatic ? null : Invalid(context, $"'{symbol.Name}' must be a static class"))
                ?? (format is not null && FormatReader.IsFormat(format) ? null : Invalid(context, $"'{symbol.Name}' must name a class marked with [TemplateFormat]")));
    }

    private static DiagnosticInfo? CheckShape(GeneratorAttributeSyntaxContext context, INamedTypeSymbol symbol)
    {
        var declaration = (TypeDeclarationSyntax)context.TargetNode;

        return declaration.Modifiers.Any(SyntaxKind.PartialKeyword) && symbol.ContainingType is null && !symbol.IsGenericType
            ? null
            : Invalid(context, $"'{symbol.Name}' must be a top-level, non-generic, partial class");
    }

    private static DiagnosticInfo Invalid(GeneratorAttributeSyntaxContext context, string message) =>
        DiagnosticInfo.Create(Diagnostics.InvalidFormat, ((TypeDeclarationSyntax)context.TargetNode).Identifier.GetLocation(), message);

    private static Declaration GetDeclaration(INamedTypeSymbol symbol) =>
        new(
            symbol.ContainingNamespace.IsGlobalNamespace ? null : symbol.ContainingNamespace.ToDisplayString(),
            symbol.Name,
            symbol.IsGenericType ? "<" + string.Join(", ", symbol.TypeParameters.Select(p => p.Name)) + ">" : "",
            symbol.Arity);

    private static string GetHintName(Declaration declaration, string suffix) =>
        $"{(declaration.Namespace is null ? "" : declaration.Namespace + ".")}{declaration.Name}{(declaration.Arity > 0 ? "`" + declaration.Arity : "")}.{suffix}.g.cs";

    private static string EmitFormat(FormatModel format)
    {
        var name = format.Declaration.Name;
        var source = StartFile(format.Declaration);

        source.Append($$"""
                /// <summary>Lets strings be used as nodes in Build.</summary>
                {{Excluded}}
                [return: global::System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(text))]
                public static implicit operator {{name}}?(string? text) => {{NotExecutable}};


            """);

        if (format.FormatsValues)
        {
            foreach (var type in FormattedTypes)
            {
                var shown = type.Replace("global::System.", "");
                source.Append($"    /// <summary>Lets a <c>{shown}</c> be used as a node in Build, written as text.</summary>\n");
                source.Append($"    {Excluded}\n    public static implicit operator {name}({type} value) => {NotExecutable};\n\n");
                source.Append($"    /// <summary>Lets a <c>{shown}?</c> be used as a node in Build, written as text, or not at all if it is null.</summary>\n");
                source.Append($"    {Excluded}\n    public static implicit operator {name}?({type}? value) => {NotExecutable};\n\n");
            }
        }

        foreach (var rule in format.Rules)
        {
            var replacements = rule.Replacements.ToArray();
            var characters = string.Concat(Enumerable.Range(0, replacements.Length / 2).Select(i => replacements[i * 2]));

            source.Append("    private static readonly global::System.Buffers.SearchValues<char> __Special_").Append(rule.Context)
                .Append(" = global::System.Buffers.SearchValues.Create(").Append(Quote(characters)).Append(");\n\n");

            // As UTF-8 bytes, if they are all ASCII. Otherwise a formatted value is checked as characters.
            source.Append("    /// <summary>The characters of the '").Append(rule.Context).Append("' rule as UTF-8 bytes, or null if they aren't all ASCII. Used by generated code.</summary>\n");
            source.Append("    ").Append(Hidden).Append('\n');
            source.Append("    public static readonly global::System.Buffers.SearchValues<byte>? __SpecialBytes_").Append(rule.Context).Append(" = ")
                .Append(characters.All(c => c < 128)
                    ? "global::System.Buffers.SearchValues.Create(" + string.Join(", ", characters.Select(c => "(byte)" + (int)c)) + ")"
                    : "null")
                .Append(";\n\n");
            source.Append("    /// <summary>Writes <paramref name=\"value\"/> as UTF-8, escaped according to the '").Append(rule.Context).Append("' rule. Null writes nothing.</summary>\n");
            source.Append("    ").Append(Hidden).Append('\n');
            source.Append("    public static void __WriteEscaped_").Append(rule.Context).Append("(string? value, ").Append(BufferWriter).Append(" writer) =>\n");
            source.Append("        __WriteEscaped_").Append(rule.Context).Append("(global::System.MemoryExtensions.AsSpan(value), writer);\n\n");

            source.Append("    /// <summary>Writes <paramref name=\"value\"/> as UTF-8, escaped according to the '").Append(rule.Context).Append("' rule. Used by generated code.</summary>\n");
            source.Append("    /// <typeparam name=\"TValue\">A value that formats itself, such as a number or a date.</typeparam>\n");
            source.Append("    ").Append(Hidden).Append('\n');
            source.Append("    public static void __WriteEscaped_").Append(rule.Context).Append("<TValue>(TValue value, ").Append(BufferWriter).Append(" writer, string? format = null, global::System.IFormatProvider? provider = null)\n");
            source.Append("        where TValue : global::System.ISpanFormattable =>\n");
            source.Append("        global::TemplateDsl.TemplateValues.Write(value, writer, format, provider, __WriteEscaped_").Append(rule.Context).Append(");\n\n");

            source.Append("    /// <summary>Writes <paramref name=\"rest\"/> as UTF-8, escaped according to the '").Append(rule.Context).Append("' rule. Used by generated code.</summary>\n");
            source.Append("    ").Append(Hidden).Append('\n');
            source.Append("    public static void __WriteEscaped_").Append(rule.Context).Append("(global::System.ReadOnlySpan<char> rest, ").Append(BufferWriter).Append(" writer)\n    {\n");
            source.Append("        while (true)\n        {\n");
            source.Append("            var index = global::System.MemoryExtensions.IndexOfAny(rest, __Special_").Append(rule.Context).Append(");\n\n");
            source.Append("            if (index < 0)\n            {\n");
            source.Append("                global::System.Text.EncodingExtensions.GetBytes(global::System.Text.Encoding.UTF8, rest, writer);\n");
            source.Append("                return;\n            }\n\n");
            source.Append("            global::System.Text.EncodingExtensions.GetBytes(global::System.Text.Encoding.UTF8, rest.Slice(0, index), writer);\n\n");
            source.Append("            switch (rest[index])\n            {\n");

            // IndexOfAny only stops at the rule's characters, so the last one is whatever is left.
            for (var i = 0; i + 1 < replacements.Length; i += 2)
            {
                var character = SymbolDisplay.FormatLiteral(replacements[i][0], quote: true);
                source.Append(i + 2 < replacements.Length ? $"                case {character}:\n" : $"                default: // {character}\n");
                source.Append("                    global::System.Buffers.BuffersExtensions.Write(writer, ").Append(Quote(replacements[i + 1])).Append("u8);\n");
                source.Append("                    break;\n");
            }

            source.Append("            }\n\n");
            source.Append("            rest = rest.Slice(index + 1);\n        }\n    }\n\n");
        }

        return source.Append("}\n").ToString();
    }

    private static string EmitNode(NodeModel model) =>
        StartFile(model.Declaration).Append(model.Members).Append("}\n").ToString();

    /// <summary>
    /// Implements an attribute method and adds the overloads its kind takes, such as any number for a number.
    /// Kept as text, so that the model compares by value and unchanged classes aren't generated again.
    /// </summary>
    /// <param name="method">The declared partial method.</param>
    /// <param name="attribute">The attribute, or null if the method only needs a body.</param>
    /// <param name="isList">Whether it is the list of attributes without their own method.</param>
    /// <param name="formatsValues">Whether the format writes numbers and dates as text.</param>
    private static string EmitAttributeMethod(IMethodSymbol method, AttributeInfo? attribute, bool isList, bool formatsValues)
    {
        const string Formattable = "where TValue : struct, global::System.ISpanFormattable";
        const string AnyNumber = "where TValue : struct, global::System.Numerics.INumberBase<TValue>";

        // The overloads share the documentation of the declared method, so they show it in autocomplete too.
        var documentation = Documentation(method);
        var anyNumber = documentation + "    /// <typeparam name=\"TValue\">Any number type, such as int, double or decimal.</typeparam>\n";
        var formattable = documentation + "    /// <typeparam name=\"TValue\">A value that formats itself, such as a number, a date or a Guid.</typeparam>\n";
        var start = $"    {SyntaxFacts.GetText(method.DeclaredAccessibility)} {DisplayType(method.ReturnType)} {Identifier(method.Name)}";
        var parameters = string.Join(", ", method.Parameters.Select(p => DisplayType(p.Type) + " " + Identifier(p.Name)));
        var source = new StringBuilder();

        source.Append($"    {Excluded}\n    {SyntaxFacts.GetText(method.DeclaredAccessibility)} partial {DisplayType(method.ReturnType)} {Identifier(method.Name)}({parameters}) => {NotExecutable};\n\n");

        if (attribute is { Kind: AttributeKind.Typed, IsAnyNumber: true })
        {
            source.Append($"{anyNumber}    {Excluded}\n{start}<TValue>(TValue value) {AnyNumber} => {NotExecutable};\n\n");
            source.Append($"{anyNumber}    {Excluded}\n{start}<TValue>(TValue? value) {AnyNumber} => {NotExecutable};\n\n");
        }

        if (attribute is { Kind: AttributeKind.Typed, AlsoText: true })
        {
            source.Append($"{documentation}    {Excluded}\n{start}(string? value) => {NotExecutable};\n\n");
        }

        if (formatsValues && attribute is { Kind: AttributeKind.Text, IsTextOnly: false })
        {
            source.Append($"{formattable}    {Excluded}\n{start}<TValue>(TValue value) {Formattable} => {NotExecutable};\n\n");
            source.Append($"{formattable}    {Excluded}\n{start}<TValue>(TValue? value) {Formattable} => {NotExecutable};\n\n");
        }

        if (formatsValues && isList)
        {
            source.Append($"{formattable}    {Excluded}\n{start}<TValue>(string name, TValue value) {Formattable} => {NotExecutable};\n\n");
            source.Append($"{formattable}    {Excluded}\n{start}<TValue>(string name, TValue? value) {Formattable} => {NotExecutable};\n\n");
        }

        return source.ToString();
    }

    /// <summary>The documentation comment of a method in source, as lines of a comment, or nothing if it has none.</summary>
    private static string Documentation(IMethodSymbol method) =>
        string.Concat((method.GetDocumentationCommentXml() ?? "").Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("<member", StringComparison.Ordinal) && line != "</member>")
            .Select(line => "    /// " + line + "\n"));

    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static string DisplayType(ITypeSymbol type) => type.ToDisplayString(TypeFormat);

    private static string Identifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : "@" + name;

    private static string EmitTags(TagsModel tags)
    {
        var format = tags.FormatType;
        var source = StartFile(tags.Declaration);

        source.Append($$"""
                /// <summary>
                /// Repeats <paramref name="build"/> for each item, as a loop that works inside brackets:
                /// <c>Each(Items, item => ...)</c>. A null result renders nothing for that item.
                /// </summary>
                {{Excluded}}
                public static {{format}} Each<TItem>(global::System.Collections.Generic.IEnumerable<TItem> items, global::System.Func<TItem, {{format}}?> build) => {{NotExecutable}};

                /// <summary>Repeats the template <paramref name="build"/> returns for each item, also one of an interface type, such as content.</summary>
                {{Excluded}}
                public static {{format}} Each<TItem, TTemplate>(global::System.Collections.Generic.IEnumerable<TItem> items, global::System.Func<TItem, TTemplate?> build)
                    where TTemplate : global::TemplateDsl.ITemplateContent<{{format}}> => {{NotExecutable}};

                /// <summary>
                /// Repeats <paramref name="build"/> for each item as it arrives: <c>await EachAsync(Items, ...)</c>. If the
                /// next item isn't ready yet, the output so far is sent to the client before waiting for it.
                /// </summary>
                {{Excluded}}
                public static global::System.Threading.Tasks.Task<{{format}}> EachAsync<TItem>(global::System.Collections.Generic.IAsyncEnumerable<TItem> items, global::System.Func<TItem, {{format}}?> build) => {{NotExecutable}};

                /// <summary>Repeats the template <paramref name="build"/> returns for each item as it arrives, also one of an interface type.</summary>
                {{Excluded}}
                public static global::System.Threading.Tasks.Task<{{format}}> EachAsync<TItem, TTemplate>(global::System.Collections.Generic.IAsyncEnumerable<TItem> items, global::System.Func<TItem, TTemplate?> build)
                    where TTemplate : global::TemplateDsl.ITemplateContent<{{format}}> => {{NotExecutable}};

                /// <summary>Repeats <paramref name="build"/>, which can await, for each item as it arrives.</summary>
                {{Excluded}}
                public static global::System.Threading.Tasks.Task<{{format}}> EachAsync<TItem>(global::System.Collections.Generic.IAsyncEnumerable<TItem> items, global::System.Func<TItem, global::System.Threading.Tasks.Task<{{format}}?>> build) => {{NotExecutable}};

                /// <summary>Repeats <paramref name="build"/>, which can await, for each item.</summary>
                {{Excluded}}
                public static global::System.Threading.Tasks.Task<{{format}}> EachAsync<TItem>(global::System.Collections.Generic.IEnumerable<TItem> items, global::System.Func<TItem, global::System.Threading.Tasks.Task<{{format}}?>> build) => {{NotExecutable}};

            """);

        return source.Append("}\n").ToString();
    }

    private static StringBuilder StartFile(Declaration declaration)
    {
        var source = new StringBuilder("// <auto-generated/>\n#nullable enable\n\n");

        if (declaration.Namespace is not null)
        {
            source.Append("namespace ").Append(declaration.Namespace).Append(";\n\n");
        }

        return source.Append("partial class ").Append(declaration.Name).Append(declaration.TypeParameters).Append("\n{\n");
    }

    private static string Quote(string text) => SymbolDisplay.FormatLiteral(text, quote: true);
}
