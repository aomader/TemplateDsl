
namespace Tests;

public sealed partial class PageTemplate : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() =>
    [
        H.Html
        [
            H.Body
            [
                H.Div,
                H.Div[H.Div]
            ]
        ]
    ];
}

