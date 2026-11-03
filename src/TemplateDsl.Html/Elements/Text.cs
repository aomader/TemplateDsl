namespace TemplateDsl.Html.Elements;

/// <summary>A text node. Its value is HTML-escaped when rendered.</summary>
[TextNode(Escape = "text")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1724", Justification = "The text node of HTML. The System.Drawing.Text namespace has nothing to do with it and isn't imported.")]
public sealed partial class Text : HtmlElement;
