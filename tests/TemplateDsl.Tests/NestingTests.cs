using Xunit;

namespace Tests;

public sealed partial class Chip(string label) : HtmlTemplate
{
    public string? Css { get; init; }

    public override IEnumerable<HtmlElement> Build() => [H.Div.Class(Css)[label]];
}

public sealed partial class ChipBar(string title, Chip? optional, bool flag) : HtmlTemplate
{
    public Chip Primary { get; } = new("primary");

    public override IEnumerable<HtmlElement> Build() =>
    [
        new Chip("top"),
        H.Div.Id(title)
        [
            "before ",
            new Chip("one"),
            new Chip("two") { Css = "x" },
            Primary,
            optional,
            flag ? new Chip("yes") : null,
            flag ? new Chip("chip") : "text",
            flag ? H.Body : new Chip("else"),
            new Chip("chained"),
            H.Div[new Chip("deep")]
        ]
    ];
}

public sealed partial class ChipBranch(bool flag) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build()
    {
        if (flag)
        {
            return [new Chip("if")];
        }

        return [H.Div[new Chip("after")]];
    }
}

public sealed partial class ChipYield : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build()
    {
        yield return new Chip("a");
        yield return "b";
    }
}

public sealed partial class TreeNode(int depth) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() =>
    [
        H.Div.Id($"d{depth}")[depth > 0 ? new TreeNode(depth - 1) : null]
    ];
}



public class NestingTests
{
    [Fact]
    public void Nested_templates_render_in_place()
    {
        Assert.Equal(
            "<div>top</div>"
            + "<div id=\"t\">before "
            + "<div>one</div>"
            + "<div class=\"x\">two</div>"
            + "<div>primary</div>"
            + "<div>opt</div>"
            + "<div>yes</div>"
            + "<div>chip</div>"
            + "<body></body>"
            + "<div>chained</div>"
            + "<div><div>deep</div></div>"
            + "</div>",
            new ChipBar("t", new Chip("opt"), flag: true).RenderToString());
    }

    [Fact]
    public void Null_templates_and_false_branches_render_nothing_or_the_other_branch()
    {
        Assert.Equal(
            "<div>top</div>"
            + "<div id=\"t\">before "
            + "<div>one</div>"
            + "<div class=\"x\">two</div>"
            + "<div>primary</div>"
            + "text"
            + "<div>else</div>"
            + "<div>chained</div>"
            + "<div><div>deep</div></div>"
            + "</div>",
            new ChipBar("t", null, flag: false).RenderToString());
    }

    [Theory]
    [InlineData(true, "<div>if</div>")]
    [InlineData(false, "<div><div>after</div></div>")]
    public void Templates_can_be_returned_from_if_statements(bool flag, string expected)
    {
        Assert.Equal(expected, new ChipBranch(flag).RenderToString());
    }

    [Fact]
    public void Templates_can_be_yielded()
    {
        Assert.Equal("<div>a</div>b", new ChipYield().RenderToString());
    }

    [Fact]
    public void Recursion_guarded_by_a_condition_terminates()
    {
        Assert.Equal(
            "<div id=\"d2\"><div id=\"d1\"><div id=\"d0\"></div></div></div>",
            new TreeNode(2).RenderToString());
    }
}
