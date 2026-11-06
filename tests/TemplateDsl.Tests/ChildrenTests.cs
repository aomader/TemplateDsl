using Xunit;

namespace Tests;

public sealed partial class Panel(string title) : IHtmlTemplate, IAcceptsChildren
{
    public IEnumerable<HtmlElement> Build() =>
    [
        H.Div.Class("panel")
        [
            H.Div.Class("title")[title],
            H.Div.Class("body")[Children]
        ]
    ];
}

public sealed partial class RecordPanel(string title) : IHtmlTemplate, IAcceptsChildren
{
    public string? Css { get; init; }

    public IEnumerable<HtmlElement> Build() => [H.Div.Id(title).Class(Css)["<", this.Children, ">", (Children)]];
}

public sealed partial class TopLevelChildren : IHtmlTemplate, IAcceptsChildren
{
    public IEnumerable<HtmlElement> Build() => [Children];
}

public sealed partial class OptionalChildren(bool show) : IHtmlTemplate, IAcceptsChildren
{
    public IEnumerable<HtmlElement> Build()
    {
        if (show)
        {
            return [H.Div[Children]];
        }

        return ["hidden"];
    }
}

/// <summary>Passes its own children on to another template, inside a lambda inside a lambda.</summary>
public sealed partial class ForwardingPanel(string title) : IHtmlTemplate, IAcceptsChildren
{
    public IEnumerable<HtmlElement> Build() =>
    [
        new Panel(title)
        [
            new Panel("inner")
            [
                Children
            ]
        ]
    ];
}

public sealed partial class PanelUser(string name, bool flag) : IHtmlTemplate
{
    public Panel Prepared { get; } = new Panel("prepared");

    public IEnumerable<HtmlElement> Build() =>
    [
        new Panel("a")
        [
            "text & more ",
            $"dynamic <{name}>",
            new Chip("chip"),
            H.Div.Id(name)["div"],
            flag ? new Chip("yes") : null,
            flag ? "t" : new Chip("no")
        ],
        new Panel("chained")["one"]["two"],
        flag ? new Panel("branch")["in branch"] : H.Div,
        Prepared["added"],
        new TopLevelChildren()["bare"],
        new ForwardingPanel("f")["forwarded"]
    ];
}

public class ChildrenTests
{
    [Fact]
    public void Children_passed_in_a_template_are_placed_by_the_component()
    {
        Assert.Equal(
            "<div class=\"panel\"><div class=\"title\">a</div><div class=\"body\">"
            + "text &amp; more dynamic &lt;x&gt;"
            + "<div>chip</div><div id=\"x\">div</div><div>yes</div>t"
            + "</div></div>"
            + "<div class=\"panel\"><div class=\"title\">chained</div><div class=\"body\">onetwo</div></div>"
            + "<div class=\"panel\"><div class=\"title\">branch</div><div class=\"body\">in branch</div></div>"
            + "<div class=\"panel\"><div class=\"title\">prepared</div><div class=\"body\">added</div></div>"
            + "bare"
            + "<div class=\"panel\"><div class=\"title\">f</div><div class=\"body\">"
            + "<div class=\"panel\"><div class=\"title\">inner</div><div class=\"body\">forwarded</div></div>"
            + "</div></div>",
            new PanelUser("x", flag: true).RenderToString());
    }

    [Fact]
    public void Component_without_children_renders_an_empty_slot()
    {
        Assert.Equal(
            "<div class=\"panel\"><div class=\"title\">t</div><div class=\"body\"></div></div>",
            new Panel("t").RenderToString());
    }

}


public sealed partial class ChildrenCases(int @case) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build()
    {
        if (@case == 0)
        {
            return [(new RecordPanel("t") { Css = "c" }) [ "x" ]];
        }

        if (@case == 1)
        {
            return [new OptionalChildren(true) [ "x" ], new OptionalChildren(false) [ "x" ]];
        }

        if (@case == 2)
        {
            return [new TopLevelChildren() [ "ä" ]];
        }

        return [new Panel("t") [ "content" ]];
    }
}

public class ChildrenCaseTests
{
    private const string PanelHtml =
        "<div class=\"panel\"><div class=\"title\">t</div><div class=\"body\">content</div></div>";

    [Fact]
    public void Record_keeps_its_properties_and_children_can_be_placed_twice()
    {
        Assert.Equal("<div id=\"t\" class=\"c\">&lt;x&gt;x</div>", new ChildrenCases(0).RenderToString());
    }

    [Fact]
    public void Component_decides_whether_children_are_rendered()
    {
        Assert.Equal("<div>x</div>hidden", new ChildrenCases(1).RenderToString());
    }

    [Fact]
    public void Component_with_children_renders_to_a_buffer_writer()
    {
        var writer = new System.Buffers.ArrayBufferWriter<byte>();

        new ChildrenCases(2).RenderTo(writer);

        Assert.Equal("ä", System.Text.Encoding.UTF8.GetString(writer.WrittenSpan));
    }

    [Fact]
    public async Task Component_with_children_renders_to_a_stream()
    {
        using var stream = new MemoryStream();

        await new ChildrenCases(3).RenderToAsync(stream);

        Assert.Equal(PanelHtml, System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public void Dynamic_text_is_escaped()
    {
        Assert.DoesNotContain("<x>", new PanelUser("<x>", false).RenderToString());
        Assert.Contains("&lt;x&gt;", new PanelUser("<x>", false).RenderToString());
    }
}
