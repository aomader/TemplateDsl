using TemplateDsl;
using TemplateDsl.Html;

namespace WebSample;

/// <summary>What a layout needs from a page. This belongs to the app: the library knows nothing about it.</summary>
public abstract partial class Page : IAsyncHtmlTemplate
{
    public abstract string Title { get; }

    public virtual IHtmlContent? Head => null;
}

/// <summary>Layouts are async so that they can host async pages: await Page. Sync content needs no await.</summary>
public sealed partial class MainLayout(Page page) : IAsyncHtmlTemplate
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) =>
    [
        H.Raw("<!DOCTYPE html>"),
        H.Html.Lang("en") [
            H.Head [
                H.Meta.Charset("utf-8"),
                H.Title [ page.Title ],
                page.Head
            ],
            H.Body [
                H.Header [ H.H1 [ page.Title ] ],
                H.Main [ await page ]
            ]
        ]
    ];
}

public sealed partial class PlainLayout(Page page) : IAsyncHtmlTemplate
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) =>
    [
        H.Raw("<!DOCTYPE html>"),
        H.Html.Lang("en") [
            H.Head [ H.Title [ page.Title ] ],
            H.Body [ await page ]
        ]
    ];
}
