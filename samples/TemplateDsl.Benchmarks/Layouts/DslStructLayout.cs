namespace Benchmarks.Layouts;

// The same layout and page as in DslLayout.cs, as structs. A struct can't derive from a contract class, so what a layout
// needs from a page is an interface, and the layout takes the page and its head as type parameters, which renders them
// without boxing them.

public interface IDslStructLayoutPage<out THead> : IHtmlTemplate
    where THead : IHtmlTemplate
{
    string Title { get; }

    string Description { get; }

    THead Head { get; }
}

public readonly partial record struct DslStructMainLayout<TPage, THead>(TPage Page) : IHtmlTemplate
    where TPage : IDslStructLayoutPage<THead>
    where THead : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build()
    {
        yield return H.Raw("<!DOCTYPE html>");
        yield return H.Html.Lang("en") [
            H.Head [
                H.Meta.Charset("utf-8"),
                H.Title [ Page.Title ],
                H.Meta.Name("description").Content(Page.Description),
                Page.Head
            ],
            H.Body [
                H.Header.Class("top") [
                    H.A.Href("/") [ "Home" ],
                    H.A.Href("/about") [ "About" ]
                ],
                H.Main.Class("content") [ Page ],
                H.Footer [ "Static footer" ]
            ]
        ];
    }
}

public readonly partial record struct DslStructDashboardHead : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [H.Link.Rel("stylesheet").Href("/dashboard.css")];
}

public readonly partial record struct DslStructDashboardPage(Dashboard Data) : IDslStructLayoutPage<DslStructDashboardHead>
{
    public string Title => Data.Title;

    public string Description => $"Everything about {Data.UserName}";

    public DslStructDashboardHead Head => default;

    public IEnumerable<HtmlElement> Build()
    {
        yield return H.H1 [ $"Hello {Data.UserName}" ];

        if (Data.IsAdmin)
        {
            yield return H.P.Class("admin") [ "You are an administrator" ];
        }

        yield return H.Table [
            H.Tbody [
                H.Each(Data.Rows, row => new DslStructRow(row))
            ]
        ];
    }
}

public readonly partial record struct DslStructRow(Row Row) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        H.Tr.Class(Row.Active ? "active" : null) [
            H.Td [ Row.Name ],
            H.Td [ $"{Row.Count}" ]
        ]
    ];
}
