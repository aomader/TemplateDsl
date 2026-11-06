namespace Benchmarks.Layouts;

public abstract partial class DslLayoutPage : IHtmlTemplate
{
    public abstract string Title { get; }

    public abstract string Description { get; }

    public virtual IHtmlContent? Head => null;
}

public sealed partial class DslMainLayout(DslLayoutPage page) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        H.Raw("<!DOCTYPE html>"),
        H.Html.Lang("en") [
            H.Head [
                H.Meta.Charset("utf-8"),
                H.Title [ page.Title ],
                H.Meta.Name("description").Content(page.Description),
                page.Head
            ],
            H.Body [
                H.Header.Class("top") [
                    H.A.Href("/") [ "Home" ],
                    H.A.Href("/about") [ "About" ]
                ],
                H.Main.Class("content") [ page ],
                H.Footer [ "Static footer" ]
            ]
        ]
    ];
}

public sealed partial class DslDashboardHead : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [H.Link.Rel("stylesheet").Href("/dashboard.css")];
}

public sealed partial class DslDashboardPage(Dashboard data) : DslLayoutPage
{
    public override string Title => data.Title;

    public override string Description => $"Everything about {data.UserName}";

    public override IHtmlContent? Head => new DslDashboardHead();

    public override IEnumerable<HtmlElement> Build()
    {
        yield return H.H1 [ $"Hello {data.UserName}" ];

        if (data.IsAdmin)
        {
            yield return H.P.Class("admin") [ "You are an administrator" ];
        }

        yield return H.Table [
            H.Tbody [
                H.Each(data.Rows, row => new DslRow(row))
            ]
        ];
    }
}

public sealed partial class DslRow(Row row) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        H.Tr.Class(row.Active ? "active" : null) [
            H.Td [ row.Name ],
            H.Td [ $"{row.Count}" ]
        ]
    ];
}
