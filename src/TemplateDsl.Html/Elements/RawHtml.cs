namespace TemplateDsl.Html.Elements;

/// <summary>
/// Text that is written exactly as it is, without escaping. Never use it with untrusted input,
/// because that allows script injection.
/// </summary>
[TextNode]
public sealed partial class RawHtml : HtmlElement;
