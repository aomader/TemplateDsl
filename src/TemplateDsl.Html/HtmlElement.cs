using TemplateDsl.Html.Elements;

namespace TemplateDsl.Html;

/// <summary>
/// Base class of everything that can appear in an HTML template. Strings, numbers and dates convert to it implicitly,
/// so they can be used wherever an element is expected.
/// </summary>
[TemplateFormat(typeof(Text), FormatsValues = true, ContentType = "text/html; charset=utf-8")]
[Escape("text", "&", "&amp;", "<", "&lt;", ">", "&gt;")]
[Escape("attribute", "&", "&amp;", "<", "&lt;", ">", "&gt;", "\"", "&quot;", "'", "&#39;")]
public abstract partial class HtmlElement : TemplateNode;
