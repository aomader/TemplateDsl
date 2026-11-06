namespace TodoSample.Views;

/// <summary>The full page, sent for a first visit or a reload. Everything after that swaps a part of it.</summary>
public sealed partial class Layout(IHtmlContent app) : IHtmlTemplate
{
    private const string Styles = """
        body { font: 16px/1.5 system-ui, sans-serif; max-width: 32rem; margin: 2rem auto; padding: 0 1rem; }
        form { display: flex; gap: .5rem; }
        form input { flex: 1; }
        ul { list-style: none; padding: 0; }
        li { display: flex; gap: .5rem; align-items: center; border-bottom: 1px solid #ddd; padding: .25rem 0; }
        li label { flex: 1; }
        li.editing { flex-wrap: wrap; }
        li.editing form { flex: 1; }
        li.editing .error { flex-basis: 100%; margin: 0; }
        .done { text-decoration: line-through; color: #888; }
        .error { color: #b00; }
        .current { font-weight: bold; }
        footer { display: flex; gap: .75rem; align-items: center; }
        """;

    public IEnumerable<HtmlElement> Build() =>
    [
        H.Raw("<!DOCTYPE html>"),
        H.Html.Lang("en") [
            H.Head [
                H.Meta.Charset("utf-8"),
                H.Meta.Name("viewport").Content("width=device-width, initial-scale=1"),
                H.Title [ "Todos" ],
                H.Style [ H.Raw(Styles) ],
                H.Script.Src("https://cdn.jsdelivr.net/npm/htmx.org@4.0.0/dist/htmx.min.js").Integrity("sha384-BvJpBiO8Kh31EqtJe5DRIeWrHWnCGkwytKs9NKFi86Hhw96dEqdEMzZDeK9iEGTc").CrossOrigin("anonymous")
            ],
            H.Body [ app ]
        ]
    ];
}
