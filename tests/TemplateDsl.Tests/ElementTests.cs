
namespace Tests;

public sealed partial class PageTemplate : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
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

