namespace TemplateDsl.Html;

/// <summary>
/// The HTML elements: <c>H.Div.Id("main").Class("box") [ H.P [ "text" ], H.Br ]</c>. Attributes are chained
/// and children follow in brackets. The elements are generated from elements.txt, and <c>Each</c> and
/// <c>EachAsync</c> by the source generator.
/// </summary>
[TemplateTags(typeof(HtmlElement))]
public static partial class H
{
    /// <summary>Writes <paramref name="value"/> as text, escaped, like a plain string does.</summary>
    public static Elements.Text Text(string? value) => new();

    /// <summary>Writes <paramref name="value"/> without escaping. Never use it with untrusted input.</summary>
    public static Elements.RawHtml Raw(string? value) => new();
}
