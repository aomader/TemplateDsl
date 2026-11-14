namespace Benchmarks.Dsl;

public sealed partial class DslButton(string label, bool primary = true) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        Div.Class(primary ? "btn btn-primary" : "btn btn-secondary")[label]
    ];
}

public sealed partial class DslCard(string title, string summary, int count) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        Div.Id($"card-{title}").Class(count > 0 ? "card active" : "card")
        [
            Div.Class("card-title")[title],
            Div.Class("card-body")
            [
                summary,
                Div.Class("count")[$"{count} items"]
            ],
            Div.Class("card-actions")
            [
                new DslButton("Open"),
                new DslButton("Share", primary: false),
                count > 0 ? new DslButton("Clear", primary: false) : null
            ]
        ]
    ];
}

public sealed partial class DslPage(string userName, bool isAdmin, int unread) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        Html
        [
            Body
            [
                Div.Id($"user-{userName}").Class(isAdmin ? "page admin" : "page")
                [
                    Div.Class("header")
                    [
                        $"Hello {userName}",
                        unread > 0 ? Div.Class("unread")[$"{unread} unread"] : "No new messages"
                    ],
                    Div.Class("nav")
                    [
                        Div.Class("nav-item")["Home"],
                        Div.Class("nav-item")["Projects"],
                        Div.Class("nav-item")["Settings"],
                        isAdmin ? Div.Class("nav-item")["Admin"] : null
                    ],
                    Div.Class("cards")
                    [
                        new DslCard("Inbox", $"Messages for {userName}", unread),
                        new DslCard("Tasks", "Things to do", 3),
                        new DslCard("Files", "Recent documents", 0),
                        new DslCard("Teams", "People you work with", 7)
                    ],
                    Div.Class("footer")["Static footer text"]
                ]
            ]
        ]
    ];
}

/// <summary>The same page as DslPage, rendered through the async path. The one await completes immediately.</summary>
public sealed partial class DslAsyncPage(string userName, bool isAdmin, int unread) : IAsyncHtmlTemplate
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) =>
    [
        Html
        [
            Body
            [
                Div.Id($"user-{userName}").Class(isAdmin ? "page admin" : "page")
                [
                    Div.Class("header")
                    [
                        "Hello ",
                        await Task.FromResult(userName),
                        unread > 0 ? Div.Class("unread")[$"{unread} unread"] : "No new messages"
                    ],
                    Div.Class("nav")
                    [
                        Div.Class("nav-item")["Home"],
                        Div.Class("nav-item")["Projects"],
                        Div.Class("nav-item")["Settings"],
                        isAdmin ? Div.Class("nav-item")["Admin"] : null
                    ],
                    Div.Class("cards")
                    [
                        new DslCard("Inbox", $"Messages for {userName}", unread),
                        new DslCard("Tasks", "Things to do", 3),
                        new DslCard("Files", "Recent documents", 0),
                        new DslCard("Teams", "People you work with", 7)
                    ],
                    Div.Class("footer")["Static footer text"]
                ]
            ]
        ]
    ];
}

public sealed partial class DslLargeRow(int number) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        Tr [ Td [ number ], Td.Class("name") [ "Row name" ] ]
    ];
}

/// <summary>Has nothing to wait for, so as a sync template it renders in one go.</summary>
public sealed partial class DslLargePage(int[] rows) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        Table [ Each(rows, row => new DslLargeRow(row)) ]
    ];
}

/// <summary>The same page as an async template, which is what streams.</summary>
public sealed partial class DslLargeAsyncPage(int[] rows) : IAsyncHtmlTemplate
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) =>
    [
        Table [ Each(rows, row => new DslLargeRow(row)) ]
    ];
}
