namespace TemplateDsl.Html.Nodes;

/// <summary>
/// The doctype, <c>&lt;!DOCTYPE html&gt;</c>, which comes first in a page so that browsers render it in standards mode.
/// It isn't an element, so it has no attributes and no children.
/// </summary>
[Node(Start = "<!DOCTYPE html>", IsVoid = true)]
public sealed partial class Doctype : HtmlElement;
