namespace Benchmarks.Dsl;

// The same templates as in DslTemplates.cs, as structs: their parents render them without allocating them, and so do
// RenderToString and RenderToAsync, which have overloads for structs.

public readonly partial record struct DslStructButton(string Label, bool Primary = true) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        Div.Class(Primary ? "btn btn-primary" : "btn btn-secondary")[Label]
    ];
}

public readonly partial record struct DslStructCard(string Title, string Summary, int Count) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        Div.Id($"card-{Title}").Class(Count > 0 ? "card active" : "card")
        [
            Div.Class("card-title")[Title],
            Div.Class("card-body")
            [
                Summary,
                Div.Class("count")[$"{Count} items"]
            ],
            Div.Class("card-actions")
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
        Html
        [
            Body
            [
                Div.Id($"user-{UserName}").Class(IsAdmin ? "page admin" : "page")
                [
                    Div.Class("header")
                    [
                        $"Hello {UserName}",
                        Unread > 0 ? Div.Class("unread")[$"{Unread} unread"] : "No new messages"
                    ],
                    Div.Class("nav")
                    [
                        Div.Class("nav-item")["Home"],
                        Div.Class("nav-item")["Projects"],
                        Div.Class("nav-item")["Settings"],
                        IsAdmin ? Div.Class("nav-item")["Admin"] : null
                    ],
                    Div.Class("cards")
                    [
                        new DslStructCard("Inbox", $"Messages for {UserName}", Unread),
                        new DslStructCard("Tasks", "Things to do", 3),
                        new DslStructCard("Files", "Recent documents", 0),
                        new DslStructCard("Teams", "People you work with", 7)
                    ],
                    Div.Class("footer")["Static footer text"]
                ]
            ]
        ]
    ];
}

public readonly partial record struct DslStructLargeRow(int Number) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        Tr [ Td [ Number ], Td.Class("name") [ "Row name" ] ]
    ];
}

/// <summary>The lambda of Each only uses its own parameter, which C# allows in a struct.</summary>
public readonly partial record struct DslStructLargePage(int[] Rows) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        Table [ Each(Rows, row => new DslStructLargeRow(row)) ]
    ];
}
