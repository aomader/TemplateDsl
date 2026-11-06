namespace Benchmarks.Dsl;

// The same templates as in DslTemplates.cs, as structs: their parents render them without allocating them, and so do
// RenderToString and RenderToAsync, which have overloads for structs.

public readonly partial record struct DslStructButton(string Label, bool Primary = true) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        H.Div.Class(Primary ? "btn btn-primary" : "btn btn-secondary")[Label]
    ];
}

public readonly partial record struct DslStructCard(string Title, string Summary, int Count) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        H.Div.Id($"card-{Title}").Class(Count > 0 ? "card active" : "card")
        [
            H.Div.Class("card-title")[Title],
            H.Div.Class("card-body")
            [
                Summary,
                H.Div.Class("count")[$"{Count} items"]
            ],
            H.Div.Class("card-actions")
            [
                new DslStructButton("Open"),
                new DslStructButton("Share", Primary: false),
                Count > 0 ? new DslStructButton("Clear", Primary: false) : null
            ]
        ]
    ];
}

public readonly partial record struct DslStructPage(string UserName, bool IsAdmin, int Unread) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        H.Html
        [
            H.Body
            [
                H.Div.Id($"user-{UserName}").Class(IsAdmin ? "page admin" : "page")
                [
                    H.Div.Class("header")
                    [
                        $"Hello {UserName}",
                        Unread > 0 ? H.Div.Class("unread")[$"{Unread} unread"] : "No new messages"
                    ],
                    H.Div.Class("nav")
                    [
                        H.Div.Class("nav-item")["Home"],
                        H.Div.Class("nav-item")["Projects"],
                        H.Div.Class("nav-item")["Settings"],
                        IsAdmin ? H.Div.Class("nav-item")["Admin"] : null
                    ],
                    H.Div.Class("cards")
                    [
                        new DslStructCard("Inbox", $"Messages for {UserName}", Unread),
                        new DslStructCard("Tasks", "Things to do", 3),
                        new DslStructCard("Files", "Recent documents", 0),
                        new DslStructCard("Teams", "People you work with", 7)
                    ],
                    H.Div.Class("footer")["Static footer text"]
                ]
            ]
        ]
    ];
}

public readonly partial record struct DslStructLargeRow(int Number) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        H.Tr [ H.Td [ Number ], H.Td.Class("name") [ "Row name" ] ]
    ];
}

/// <summary>The lambda of Each only uses its own parameter, which C# allows in a struct.</summary>
public readonly partial record struct DslStructLargePage(int[] Rows) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        H.Table [ H.Each(Rows, row => new DslStructLargeRow(row)) ]
    ];
}
