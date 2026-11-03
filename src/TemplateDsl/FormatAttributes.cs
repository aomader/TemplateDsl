namespace TemplateDsl;

// These describe an output format as data. The source generators read them, so no format-specific
// knowledge lives in the generators. All values are constants, which lets them be merged into
// compile-time literals and read from referenced assemblies.

/// <summary>The culture that numbers, dates and the like are written in.</summary>
public enum ValueCulture
{
    /// <summary>The same for everyone, for what programs read. Dates are written as ISO 8601.</summary>
    Invariant,

    /// <summary>The culture of the current request, for what people read.</summary>
    Current,
}

/// <summary>Marks the base class of a format's nodes. <paramref name="textNode"/> is the node that plain strings become.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TemplateFormatAttribute(Type textNode) : Attribute
{
    /// <summary>The node that plain strings become, a class marked with <see cref="TextNodeAttribute"/>.</summary>
    public Type TextNode { get; } = textNode;

    /// <summary>
    /// Whether numbers, dates, <see cref="Guid"/>s and chars can be used as children and attribute values, written as
    /// text. Leave this off if text is not how the format writes them, as in JSON, where numbers have no quotes but text does.
    /// </summary>
    public bool FormatsValues { get; set; }

    /// <summary>The culture of values in the text of this format, also inside an interpolated string. Attributes have their own.</summary>
    public ValueCulture TextCulture { get; set; } = ValueCulture.Current;

    /// <summary>
    /// The HTTP Content-Type of output in this format, such as <c>text/html; charset=utf-8</c>, which web results send
    /// unless they are given another. Output is always UTF-8.
    /// </summary>
    public string? ContentType { get; set; }
}

/// <summary>
/// Marks the static partial class holding the factory methods of a format's nodes. The generator
/// adds <c>Each</c> and <c>EachAsync</c> to it. Only methods of such a class count as factories.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TemplateTagsAttribute(Type format) : Attribute
{
    /// <summary>The base class of the format's nodes, marked with <see cref="TemplateFormatAttribute"/>.</summary>
    public Type Format { get; } = format;
}

/// <summary>
/// An escaping rule of a format, as pairs of a single character and the text that replaces it, applied
/// in one pass so that replacements are never escaped again. Nodes refer to it by <paramref name="context"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class EscapeAttribute(string context, params string[] replacements) : Attribute
{
    /// <summary>The name that nodes refer to this rule by, such as <c>text</c>.</summary>
    public string Context { get; } = context;

    /// <summary>Pairs of a single character and the text that replaces it, such as <c>"&lt;", "&amp;lt;"</c>.</summary>
    public string[] Replacements { get; } = replacements;
}

/// <summary>
/// A node that renders as <see cref="Start"/>, its attributes, <see cref="Body"/>, its children and
/// <see cref="End"/>. Attributes come from the methods marked with <see cref="AttrAttribute"/>, in the
/// order they are declared. Factory methods set them through parameters with the same names.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class NodeAttribute : Attribute
{
    /// <summary>Written first, such as <c>&lt;div</c>. The attributes follow it.</summary>
    public string Start { get; set; } = "";

    /// <summary>Written after the attributes and before the children, such as <c>&gt;</c>.</summary>
    public string Body { get; set; } = "";

    /// <summary>Written last, after the children, such as <c>&lt;/div&gt;</c>.</summary>
    public string End { get; set; } = "";

    /// <summary>Whether this node has no children, so it gets no brackets, such as <c>&lt;br&gt;</c> in HTML.</summary>
    public bool IsVoid { get; set; }

    /// <summary>Written between children that actually render, such as the commas of a JSON array.</summary>
    public string? Separator { get; set; }
}

/// <summary>
/// Declares an attribute of a node as a partial method that returns the node, which the generator implements:
/// <c>[Attr(" id=\"", "\"")] public partial Item Id(string? value);</c>. It renders as <paramref name="prefix"/>,
/// the value and <paramref name="suffix"/>, or not at all if the value is null. The parameter says what values it
/// takes: a string takes text, a number such as <c>int?</c> only that, an enum one of its members, written in
/// lowercase with a dash between words, and a <c>bool?</c> writes <c>true</c> or <c>false</c>. A bool with the
/// default <c>true</c> has no value: it renders as <paramref name="prefix"/> and <paramref name="suffix"/> if it is true.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AttrAttribute(string prefix, string suffix) : Attribute
{
    /// <summary>Written before the value, such as <c> id="</c>.</summary>
    public string Prefix { get; } = prefix;

    /// <summary>Written after the value, such as the closing quote.</summary>
    public string Suffix { get; } = suffix;

    /// <summary>
    /// The <see cref="EscapeAttribute"/> context of the value, or null for no escaping. Set it for numbers and dates too:
    /// an <see cref="AnyNumber"/> attribute takes number types of the app's own, which write whatever they format as.
    /// </summary>
    public string? Escape { get; set; }

    /// <summary>The culture of values in this attribute, also inside an interpolated string. Use Current if it's meant for people to read.</summary>
    public ValueCulture Culture { get; set; }

    /// <summary>For a string, whether it takes only strings, even if its format can write numbers and dates as text.</summary>
    public bool TextOnly { get; set; }

    /// <summary>For a number, whether it takes any number, such as an int, a double or a decimal.</summary>
    public bool AnyNumber { get; set; }

    /// <summary>For a value type, whether it also takes a string, such as <c>any</c> for a step.</summary>
    public bool AlsoText { get; set; }

    /// <summary>For a value type, the format its values are written in, such as <c>yyyy-MM</c> for a month.</summary>
    public string? Format { get; set; }
}

/// <summary>
/// Declares a partial method <c>Attr(string name, string? value)</c> that returns the node, for attributes without
/// their own method, whose names aren't known in advance, such as <c>data-*</c>. In a template, names must be
/// constants, while values can be dynamic. Each renders as <see cref="Before"/>, the name, <see cref="BetweenNameAndValue"/>,
/// the value and <see cref="After"/>, or not at all if the value is null.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AttrListAttribute : Attribute
{
    /// <summary>Written before the name, such as a space.</summary>
    public string Before { get; set; } = "";

    /// <summary>Written between the name and the value, such as <c>="</c>.</summary>
    public string BetweenNameAndValue { get; set; } = "";

    /// <summary>Written after the value, such as the closing quote.</summary>
    public string After { get; set; } = "";

    /// <summary>The <see cref="EscapeAttribute"/> context of the values, or null for no escaping.</summary>
    public string? Escape { get; set; }

    /// <summary>The attributes meant for people to read, whose values use the current culture. All others are invariant.</summary>
    public string[]? CurrentCultureNames { get; set; }
}

/// <summary>A node whose output is the text passed as the first argument of its factory method.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TextNodeAttribute : Attribute
{
    /// <summary>Written before the text, such as the opening quote of a JSON string.</summary>
    public string Prefix { get; set; } = "";

    /// <summary>Written after the text.</summary>
    public string Suffix { get; set; } = "";

    /// <summary>The <see cref="EscapeAttribute"/> context of the text, or null for no escaping.</summary>
    public string? Escape { get; set; }
}

/// <summary>
/// Marks an abstract partial class that templates of a format derive from, implementing <see cref="ITemplate{TNode}"/>
/// or <see cref="IAsyncTemplate{TNode}"/>, optionally with <see cref="IAcceptsChildren"/> in a class derived from it.
/// The generator adds the abstract members. A synchronous one should also derive from the format's
/// base class, which is what makes its templates usable as children directly.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TemplateBaseAttribute : Attribute;
