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
        Doctype,
        Html.Lang("en") [
            Head [
                Meta.Charset("utf-8"),
                Title [ page.Title ],
                Meta.Name("description").Content(page.Description),
                page.Head
            ],
            Body [
                Header.Class("top") [
                    A.Href("/") [ "Home" ],
                    A.Href("/about") [ "About" ]
                ],
                Main.Class("content") [ page ],
                Footer [ "Static footer" ]
            ]
        ]
    ];
}

public sealed partial class DslDashboardHead : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [Link.Rel("stylesheet").Href("/dashboard.css")];
}

public sealed partial class DslDashboardPage(Dashboard data) : DslLayoutPage
{
    public override string Title => data.Title;

    public override string Description => $"Everything about {data.UserName}";

    public override IHtmlContent? Head => new DslDashboardHead();

    public override IEnumerable<HtmlElement> Build()
    {
        yield return H1 [ $"Hello {data.UserName}" ];

        if (data.IsAdmin)
        {
            yield return P.Class("admin") [ "You are an administrator" ];
        }

        yield return Table [
            Tbody [
                Each(data.Rows, row => new DslRow(row))
            ]
        ];
    }
}

public sealed partial class DslRow(Row row) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        Tr.Class(row.Active ? "active" : null) [
            Td [ row.Name ],
            Td [ $"{row.Count}" ]
        ]
    ];
}
