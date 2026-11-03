using Xunit;

namespace Tests;

public sealed partial class AttributeTemplate : HtmlTemplate
{
    public const string ConstantClass = "from-constant";

    public override IEnumerable<HtmlElement> Build() =>
    [
        H.Div.Id("123").Class("outer")
        [
            H.Div.Class(ConstantClass),
            H.Div.Id("inner")
            [
                H.Div
            ],
            H.Div.Class("a\"<b>&c"),
            H.Div.Id(null)
        ]
    ];
}

public class AttributeTests
{
    [Fact]
    public void RenderToString_writes_attributes_children_and_escapes_values()
    {
        Assert.Equal(
            "<div id=\"123\" class=\"outer\">"
            + "<div class=\"from-constant\"></div>"
            + "<div id=\"inner\"><div></div></div>"
            + "<div class=\"a&quot;&lt;b&gt;&amp;c\"></div>"
            + "<div></div>"
            + "</div>",
            new AttributeTemplate().RenderToString());
    }

}
