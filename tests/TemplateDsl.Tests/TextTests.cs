using Xunit;

namespace Tests;

public sealed partial class TextTemplate : HtmlTemplate
{
    private const string Constant = "from constant";

    public override IEnumerable<HtmlElement> Build() =>
    [
        "top level",
        H.Text(" explicit top level"),
        H.Html
        [
            "in html",
            H.Body
            [
                "in body",
                H.Div["hello ", H.Div, H.Text("explicit"), "a" + "b", Constant],
                H.Div["<script>\"quoted\" & more</script>"],
                H.Text("constructed"),
                (string?)null,
                "äö€😀"
            ]
        ]
    ];
}

public class TextTests
{
    [Fact]
    public void RenderToString_writes_and_escapes_text()
    {
        Assert.Equal(
            "top level explicit top level<html>in html<body>in body"
            + "<div>hello <div></div>explicitabfrom constant</div>"
            + "<div>&lt;script&gt;\"quoted\" &amp; more&lt;/script&gt;</div>"
            + "constructed"
            + "äö€😀"
            + "</body></html>",
            new TextTemplate().RenderToString());
    }

}
