using TemplateDsl;

namespace FormatSamples;

// A whole output format: nothing here but declarations.

[TemplateFormat(typeof(RssText), TextCulture = ValueCulture.Invariant, ContentType = "application/rss+xml; charset=utf-8")]
[Escape("text", "&", "&amp;", "<", "&lt;", ">", "&gt;")]
[Escape("attribute", "&", "&amp;", "<", "&lt;", ">", "&gt;", "\"", "&quot;", "'", "&apos;")]
public abstract partial class RssNode : TemplateNode;

[TextNode(Escape = "text")]
public sealed partial class RssText : RssNode;

[Node(Start = "<?xml version=\"1.0\" encoding=\"utf-8\"?><rss version=\"2.0\"", Body = ">", End = "</rss>")]
public sealed partial class RssRoot : RssNode;

[Node(Start = "<channel", Body = ">", End = "</channel>")]
public sealed partial class RssChannel : RssNode;

[Node(Start = "<item", Body = ">", End = "</item>")]
public sealed partial class RssItem : RssNode;

[Node(Start = "<title", Body = ">", End = "</title>")]
public sealed partial class RssTitle : RssNode;

[Node(Start = "<link", Body = ">", End = "</link>")]
public sealed partial class RssLink : RssNode;

[Node(Start = "<description", Body = ">", End = "</description>")]
public sealed partial class RssDescription : RssNode;

[Node(Start = "<guid", Body = ">", End = "</guid>")]
public sealed partial class RssGuid : RssNode
{
    [Attr(" isPermaLink=\"", "\"", Escape = "attribute")]
    public partial RssGuid IsPermaLink(string? value);
}

[TemplateTags(typeof(RssNode))]
public static partial class Rss
{
    public static RssRoot Root => new();

    public static RssChannel Channel => new();

    public static RssItem Item => new();

    public static RssTitle Title => new();

    public static RssLink Link => new();

    public static RssDescription Description => new();

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720", Justification = "The RSS element is called guid.")]
    public static RssGuid Guid => new();
}

/// <summary>A template that outputs RSS.</summary>
public interface IRssTemplate : ITemplate<RssNode>;
