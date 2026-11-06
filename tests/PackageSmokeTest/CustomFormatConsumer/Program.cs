using TemplateDsl;

Console.Write(new Doc("a,b").RenderToString());

[TemplateFormat(typeof(ListText))]
[Escape("text", ",", "\\,")]
public abstract partial class ListNode : TemplateNode;

[TextNode(Escape = "text")]
public sealed partial class ListText : ListNode;

[Node(Start = "[", End = "]", Separator = ",")]
public sealed partial class Items : ListNode;

[TemplateTags(typeof(ListNode))]
public static partial class Lists
{
    public static Items Items => new();
}

public interface IListTemplate : ITemplate<ListNode>;

public sealed partial class Doc(string value) : IListTemplate
{
    public IEnumerable<ListNode> Build() => [Lists.Items[value, "c"]];
}
