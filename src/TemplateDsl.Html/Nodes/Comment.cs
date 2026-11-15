namespace TemplateDsl.Html.Nodes;

/// <summary>
/// An HTML comment, <c>&lt;!-- text --&gt;</c>. Its text can't end the comment early or break it, which
/// <see cref="CommentText"/> sees to without changing how it reads. Everyone who loads the page can read it.
/// </summary>
[TextNode(Prefix = "<!--", Suffix = "-->", Escape = "comment")]
public sealed partial class Comment : HtmlElement;
