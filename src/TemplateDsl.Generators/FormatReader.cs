using System.Text;
using Microsoft.CodeAnalysis;

namespace TemplateDsl.Generators;

internal enum AttributeKind
{
    /// <summary>A string: text.</summary>
    Text,

    /// <summary>A bool with the default true: present or not, without a value.</summary>
    Presence,

    /// <summary>Any other bool: the value is the word true or false.</summary>
    TrueFalse,

    /// <summary>A value type that formats itself, which takes only that type.</summary>
    Typed,

    /// <summary>An enum, which writes the name of the member.</summary>
    Enum,

    /// <summary>A method that can't be an attribute, because of its parameters.</summary>
    Invalid,
}

/// <param name="Name">The name of the method, or of the attribute for one from the list.</param>
/// <param name="Prefix">Written before the value.</param>
/// <param name="Suffix">Written after the value.</param>
/// <param name="Escape">The escape context of the value, or null for none.</param>
/// <param name="Kind">What values it takes.</param>
/// <param name="IsCurrentCulture">Whether values use the current culture, because people read this attribute.</param>
/// <param name="IsTextOnly">Whether a text attribute takes strings only, not numbers and dates.</param>
/// <param name="IsAnyNumber">Whether a typed attribute takes any number, not only its own type.</param>
/// <param name="AlsoText">Whether a typed attribute also takes a string.</param>
/// <param name="Format">The format of a typed attribute's values, if it has one.</param>
internal sealed record AttributeInfo(
    string Name,
    string Prefix,
    string Suffix,
    string? Escape,
    AttributeKind Kind,
    bool IsCurrentCulture = false,
    bool IsTextOnly = false,
    bool IsAnyNumber = false,
    bool AlsoText = false,
    string? Format = null);

internal sealed record AttributeListInfo(string Before, string BetweenNameAndValue, string After, string? Escape, EquatableArray<string> CurrentCultureNames);

/// <summary>How a node renders, as declared by its attributes.</summary>
internal sealed record NodeInfo(
    string Start,
    string Body,
    string End,
    bool IsText,
    string? TextEscape,
    string TextPrefix,
    string TextSuffix,
    string? Separator,
    EquatableArray<AttributeInfo> Attributes,
    AttributeListInfo? AttributeList = null,
    bool IsVoid = false);

internal sealed record EscapeRule(string Context, EquatableArray<string> Replacements);

/// <summary>Reads the format attributes, from source or from a referenced assembly alike.</summary>
internal static class FormatReader
{
    public const string Renderable = "IRenderable";
    public const string Streaming = "IStreamingRenderable";
    public const string AcceptsChildren = "IAcceptsChildren";
    public const string ContextRenderable = "ITemplateRenderable`1";

    /// <summary>Whether <paramref name="type"/> is the type with this metadata name in the core library.</summary>
    public static bool IsCoreType(ITypeSymbol type, string metadataName) => NameIn(type, "TemplateDsl") == metadataName;

    /// <summary>Whether <paramref name="type"/> is the type with this metadata name in the System namespace.</summary>
    public static bool IsSystemType(ITypeSymbol type, string metadataName) => NameIn(type, "System") == metadataName;

    /// <summary>The metadata name of <paramref name="type"/> if it is in the System namespace and not nested, such as <c>Guid</c>.</summary>
    public static string? SystemTypeName(ITypeSymbol type) => NameIn(type, "System");

    /// <summary>
    /// The metadata name of a type that isn't nested, in a namespace directly under the global one, or null. This
    /// compares names, which Roslyn keeps, instead of building the type's full name as a string.
    /// </summary>
    private static string? NameIn(ITypeSymbol type, string @namespace) =>
        type is { ContainingType: null, ContainingNamespace: { ContainingNamespace.IsGlobalNamespace: true } containing } && containing.Name == @namespace
            ? type.MetadataName
            : null;

    public static bool Implements(ITypeSymbol type, string metadataName) =>
        Interfaces(type).Any(i => IsCoreType(i, metadataName));

    /// <summary>
    /// All interfaces of <paramref name="type"/>, including itself if it is one. A type parameter has those of its
    /// constraints, which Roslyn doesn't list as its own.
    /// </summary>
    public static IEnumerable<INamedTypeSymbol> Interfaces(ITypeSymbol type) =>
        type is ITypeParameterSymbol parameter
            ? parameter.ConstraintTypes.SelectMany(Interfaces)
            : type is INamedTypeSymbol { TypeKind: TypeKind.Interface } named ? type.AllInterfaces.Prepend(named) : type.AllInterfaces;

    public static bool IsTemplateInterface(INamedTypeSymbol type) =>
        IsCoreType(type, "ITemplate`1") || IsCoreType(type, "IAsyncTemplate`1") || IsCoreType(type, "ITemplate`2") || IsCoreType(type, "IAsyncTemplate`2");

    public static IEnumerable<INamedTypeSymbol> BaseTypes(ITypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            yield return current;
        }
    }

    /// <summary>The methods of <paramref name="type"/> and of its base classes, so that nodes can share attributes.</summary>
    public static IEnumerable<IMethodSymbol> AllMethods(ITypeSymbol type) =>
        BaseTypes(type).Prepend((INamedTypeSymbol)type).SelectMany(current => current.GetMembers().OfType<IMethodSymbol>());

    public static NodeInfo? ReadNode(ITypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (IsAttribute(attribute, "NodeAttribute"))
            {
                return new NodeInfo(
                    GetNamed(attribute, "Start") ?? "",
                    GetNamed(attribute, "Body") ?? "",
                    GetNamed(attribute, "End") ?? "",
                    IsText: false,
                    TextEscape: null,
                    TextPrefix: "",
                    TextSuffix: "",
                    GetNamed(attribute, "Separator"),
                    new EquatableArray<AttributeInfo>(ReadAttributes(type)),
                    ReadAttributeList(type),
                    attribute.NamedArguments.Any(argument => argument.Key == "IsVoid" && Equals(argument.Value.Value, true)));
            }

            if (IsAttribute(attribute, "TextNodeAttribute"))
            {
                return new NodeInfo(
                    "", "", "",
                    IsText: true,
                    GetNamed(attribute, "Escape"),
                    GetNamed(attribute, "Prefix") ?? "",
                    GetNamed(attribute, "Suffix") ?? "",
                    Separator: null,
                    new EquatableArray<AttributeInfo>([]));
            }
        }

        return null;
    }

    public static EscapeRule[] ReadEscapeRules(ITypeSymbol format) =>
        format.GetAttributes()
            .Where(attribute => IsAttribute(attribute, "EscapeAttribute") && attribute.ConstructorArguments.Length == 2)
            .Select(attribute => new EscapeRule(
                (string)attribute.ConstructorArguments[0].Value!,
                new EquatableArray<string>(attribute.ConstructorArguments[1].Values.Select(value => (string)value.Value!).ToArray())))
            .ToArray();

    /// <summary>The node that plain strings become in <paramref name="format"/>.</summary>
    public static ITypeSymbol? ReadTextNode(ITypeSymbol format) =>
        format.GetAttributes()
            .FirstOrDefault(attribute => IsAttribute(attribute, "TemplateFormatAttribute") && attribute.ConstructorArguments.Length == 1)
            ?.ConstructorArguments[0].Value as ITypeSymbol;

    public static bool IsTemplateBase(ITypeSymbol type) =>
        type.GetAttributes().Any(attribute => IsAttribute(attribute, "TemplateBaseAttribute"));

    public static bool IsTags(ITypeSymbol type) =>
        type.GetAttributes().Any(attribute => IsAttribute(attribute, "TemplateTagsAttribute"));

    public static bool FormatsValues(ITypeSymbol format) =>
        format.GetAttributes()
            .Where(attribute => IsAttribute(attribute, "TemplateFormatAttribute"))
            .SelectMany(attribute => attribute.NamedArguments)
            .Any(argument => argument.Key == "FormatsValues" && Equals(argument.Value.Value, true));

    /// <summary>Whether values in the text of <paramref name="format"/> are invariant. The current culture is the default.</summary>
    public static bool HasInvariantText(ITypeSymbol format) =>
        format.GetAttributes()
            .Where(attribute => IsAttribute(attribute, "TemplateFormatAttribute"))
            .SelectMany(attribute => attribute.NamedArguments)
            .Any(argument => argument.Key == "TextCulture" && Equals(argument.Value.Value, 0));

    public static bool IsFormat(ITypeSymbol type) =>
        type.GetAttributes().Any(attribute => IsAttribute(attribute, "TemplateFormatAttribute"));

    /// <summary>The format that <paramref name="tags"/> holds the factory methods of.</summary>
    public static ITypeSymbol? ReadTagsFormat(ITypeSymbol tags) =>
        tags.GetAttributes()
            .FirstOrDefault(attribute => IsAttribute(attribute, "TemplateTagsAttribute") && attribute.ConstructorArguments.Length == 1)
            ?.ConstructorArguments[0].Value as ITypeSymbol;

    /// <summary>The closest base class marked as a format.</summary>
    public static INamedTypeSymbol? FindFormat(INamedTypeSymbol type) =>
        BaseTypes(type).FirstOrDefault(IsFormat);

    /// <summary>
    /// Applies a rule to a constant, which is why rules are data rather than code. This is a single
    /// pass over the value, like the generated runtime escaper, so both give the same result in any rule order.
    /// </summary>
    public static string Escape(IEnumerable<EscapeRule> rules, string? context, string value)
    {
        var rule = rules.FirstOrDefault(r => r.Context == context);

        if (rule is null)
        {
            return value;
        }

        var replacements = rule.Replacements.ToArray();
        var result = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            var replacement = character.ToString();

            for (var i = 0; i + 1 < replacements.Length; i += 2)
            {
                if (replacements[i] == replacement)
                {
                    replacement = replacements[i + 1];
                    break;
                }
            }

            result.Append(replacement);
        }

        return result.ToString();
    }

    private static AttributeInfo[] ReadAttributes(ITypeSymbol type) =>
        AllMethods(type).Select(ReadAttribute).OfType<AttributeInfo>().ToArray();

    /// <summary>The attribute that <paramref name="method"/> declares, if it is marked as one.</summary>
    public static AttributeInfo? ReadAttribute(IMethodSymbol method)
    {
        var attribute = method.GetAttributes().FirstOrDefault(a => IsAttribute(a, "AttrAttribute"));

        if (attribute is not { ConstructorArguments.Length: 2 })
        {
            return null;
        }

        var kind = GetKind(method);

        return new AttributeInfo(
            method.Name,
            (string)attribute.ConstructorArguments[0].Value!,
            (string)attribute.ConstructorArguments[1].Value!,
            GetNamed(attribute, "Escape"),
            kind,
            attribute.NamedArguments.Any(argument => argument.Key == "Culture" && Equals(argument.Value.Value, 1)),
            IsSet(attribute, "TextOnly"),
            IsSet(attribute, "AnyNumber"),
            IsSet(attribute, "AlsoText"),
            GetNamed(attribute, "Format"));
    }

    /// <summary>What the single parameter of an attribute method takes.</summary>
    private static AttributeKind GetKind(IMethodSymbol method)
    {
        if (method.Parameters.Length != 1 || method.IsGenericMethod)
        {
            return AttributeKind.Invalid;
        }

        var parameter = method.Parameters[0];

        if (parameter.Type.SpecialType == SpecialType.System_String)
        {
            return AttributeKind.Text;
        }

        var value = WithoutNull(parameter.Type);

        return value.SpecialType == SpecialType.System_Boolean
                ? parameter is { HasExplicitDefaultValue: true, ExplicitDefaultValue: true } ? AttributeKind.Presence : AttributeKind.TrueFalse
            : value.TypeKind == TypeKind.Enum ? AttributeKind.Enum
            : value.IsValueType && value.AllInterfaces.Any(i => IsSystemType(i, "ISpanFormattable")) ? AttributeKind.Typed
            : AttributeKind.Invalid;
    }

    private static ITypeSymbol WithoutNull(ITypeSymbol type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ? nullable.TypeArguments[0] : type;

    /// <summary>Whether <paramref name="method"/> takes a name and a value, as the method of an attribute list must.</summary>
    public static bool HasAttributeListShape(IMethodSymbol method) =>
        !method.IsGenericMethod && method.Parameters.Length == 2 && method.Parameters.All(p => p.Type.SpecialType == SpecialType.System_String);

    private static AttributeListInfo? ReadAttributeList(ITypeSymbol type)
    {
        foreach (var method in AllMethods(type))
        {
            var attribute = method.GetAttributes().FirstOrDefault(a => IsAttribute(a, "AttrListAttribute"));

            if (attribute is not null)
            {
                return new AttributeListInfo(
                    GetNamed(attribute, "Before") ?? "",
                    GetNamed(attribute, "BetweenNameAndValue") ?? "",
                    GetNamed(attribute, "After") ?? "",
                    GetNamed(attribute, "Escape"),
                    new EquatableArray<string>(attribute.NamedArguments
                        .Where(argument => argument.Key == "CurrentCultureNames")
                        .SelectMany(argument => argument.Value.Values)
                        .Select(name => (string)name.Value!)
                        .ToArray()));
            }
        }

        return null;
    }

    /// <summary>Names aren't escaped, so these characters could inject markup.</summary>
    public static bool IsValidAttributeName(string name) =>
        name.Length > 0 && name.All(c => !char.IsControl(c) && " \"'=<>/`".IndexOf(c) < 0);

    /// <summary>How an enum member is written: <c>DatetimeLocal</c> becomes <c>datetime-local</c>.</summary>
    public static string GetEnumText(string memberName)
    {
        var text = new StringBuilder();

        for (var i = 0; i < memberName.Length; i++)
        {
            if (i > 0 && char.IsUpper(memberName[i]))
            {
                text.Append('-');
            }

            text.Append(char.ToLowerInvariant(memberName[i]));
        }

        return text.ToString();
    }

    /// <summary>Whether a flag is set to true. The values of named arguments are boxed, so they are compared with Equals.</summary>
    private static bool IsSet(AttributeData attribute, string name) =>
        attribute.NamedArguments.Any(argument => argument.Key == name && Equals(argument.Value.Value, true));

    internal static bool IsAttribute(AttributeData attribute, string name) =>
        attribute.AttributeClass is { } attributeClass && IsCoreType(attributeClass, name);

    private static string? GetNamed(AttributeData attribute, string name)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == name)
            {
                return argument.Value.Value as string;
            }
        }

        return null;
    }
}
