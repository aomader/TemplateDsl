using Xunit;

namespace Tests;

public sealed partial class IfReturnTemplate(bool flag, string? name = null, int count = 0) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build()
    {
        if (flag)
        {
            return [H.Div.Id("flag")];
        }
        else if (count > 1)
        {
            if (name is { } known)
            {
                return [H.Div.Id(known)];
            }

            return [H.Div.Id("many")];
        }

        return [H.Body];
    }
}

public sealed partial class YieldTemplate(bool flag, int count = 0) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build()
    {
        yield return "start ";

        if (flag)
            yield return H.Div.Id("flag");
        else
            yield return "not flag ";

        if (count == 0)
        {
            yield break;
        }

        yield return H.Body;
    }
}

public sealed partial class TernaryTemplate(bool flag, string? name = null, int count = 0) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        H.Div.Id(flag ? "a&b" : null).Class(flag ? name : $"c{count}")
        [
            flag ? "yes&" : "no&",
            flag ? H.Div.Id("one") : H.Body,
            flag ? H.Div.Id("two")["inner"] : "text&",
            flag ? H.Div.Id("three") : null,
            flag ? null : H.Div.Id("four"),
            flag
                ? count > 0 ? "nested1" : "nested2"
                : count > 0 ? H.Div.Id("nested3") : H.Body,
            H.Text(flag ? "text-yes" : name),
            $"{(flag ? "<hole>" : "hole")}"
        ]
    ];
}

public sealed partial class WholeOutputTernaryTemplate(bool flag) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
        flag ? [H.Div.Id("flag")] : [H.Body, "text"];
}

public class ConditionalTests
{
    [Theory]
    [InlineData(true, null, 0, "<div id=\"flag\"></div>")]
    [InlineData(false, null, 0, "<body></body>")]
    [InlineData(false, null, 2, "<div id=\"many\"></div>")]
    [InlineData(false, "ann", 2, "<div id=\"ann\"></div>")]
    public void If_statements_choose_the_returned_output(bool flag, string? name, int count, string expected)
    {
        Assert.Equal(expected, new IfReturnTemplate(flag, name, count).RenderToString());
    }

    [Theory]
    [InlineData(true, 0, "start <div id=\"flag\"></div>")]
    [InlineData(false, 0, "start not flag ")]
    [InlineData(true, 1, "start <div id=\"flag\"></div><body></body>")]
    public void Yield_statements_add_to_the_output(bool flag, int count, string expected)
    {
        Assert.Equal(expected, new YieldTemplate(flag, count: count).RenderToString());
    }

    [Fact]
    public void Ternaries_choose_attributes_text_and_elements_when_true()
    {
        Assert.Equal(
            "<div id=\"a&amp;b\" class=\"x\">"
            + "yes&amp;"
            + "<div id=\"one\"></div>"
            + "<div id=\"two\">inner</div>"
            + "<div id=\"three\"></div>"
            + "nested1"
            + "text-yes"
            + "&lt;hole&gt;"
            + "</div>",
            new TernaryTemplate(true, "x", 1).RenderToString());
    }

    [Fact]
    public void Ternaries_choose_attributes_text_and_elements_when_false()
    {
        Assert.Equal(
            "<div class=\"c0\">"
            + "no&amp;"
            + "<body></body>"
            + "text&amp;"
            + "<div id=\"four\"></div>"
            + "<body></body>"
            + "x"
            + "hole"
            + "</div>",
            new TernaryTemplate(false, "x", 0).RenderToString());
    }

    [Fact]
    public void Ternary_with_a_null_attribute_value_at_runtime_leaves_it_out()
    {
        Assert.StartsWith("<div id=\"a&amp;b\">", new TernaryTemplate(true).RenderToString());
    }

    [Theory]
    [InlineData(true, "<div id=\"flag\"></div>")]
    [InlineData(false, "<body></body>text")]
    public void Ternary_can_choose_the_whole_output(bool flag, string expected)
    {
        Assert.Equal(expected, new WholeOutputTernaryTemplate(flag).RenderToString());
    }

}
