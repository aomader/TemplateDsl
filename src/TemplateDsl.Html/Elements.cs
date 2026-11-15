namespace TemplateDsl.Html;

/// <summary>
/// The HTML elements: <c>Div.Id("main").Class("box") [ P [ "text" ], Br ]</c>. Attributes are chained and children
/// follow in brackets. Import it with <c>using static TemplateDsl.Html.Elements;</c> to write elements without a prefix,
/// or give it a short name with <c>using H = TemplateDsl.Html.Elements;</c> and write <c>H.Div</c>. The elements are
/// generated from elements.txt, and <c>Each</c> and <c>EachAsync</c> by the source generator.
/// </summary>
[TemplateTags(typeof(HtmlElement))]
public static partial class Elements
{
    /// <summary>The doctype, <c>&lt;!DOCTYPE html&gt;</c>, which comes first in a page: <c>[Doctype, Html [ ... ]]</c>.</summary>
    public static Nodes.Doctype Doctype => new();

    /// <summary>Writes <paramref name="value"/> as text, escaped, like a plain string does.</summary>
    public static Nodes.Text Text(string? value) => new();

    /// <summary>
    /// Writes <paramref name="value"/> as an HTML comment, <c>&lt;!--value--&gt;</c>, which it can't end early or
    /// break, as it is written to be safe without changing how it reads. Null writes nothing. Everyone who loads the
    /// page can read it, so it is no place for secrets.
    /// </summary>
    public static Nodes.Comment Comment(string? value) => new();

    /// <summary>Writes <paramref name="value"/> without escaping. Never use it with untrusted input.</summary>
    public static Nodes.RawHtml Raw(string? value) => new();
}
