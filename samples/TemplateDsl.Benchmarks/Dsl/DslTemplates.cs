namespace Benchmarks.Dsl;

public sealed partial class DslButton(string label, bool primary = true) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() =>
    [
        H.Div.Class(primary ? "btn btn-primary" : "btn btn-secondary")[label]
    ];
}

public sealed partial class DslCard(string title, string summary, int count) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() =>
    [
        H.Div.Id($"card-{title}").Class(count > 0 ? "card active" : "card")
        [
            H.Div.Class("card-title")[title],
            H.Div.Class("card-body")
            [
                summary,
                H.Div.Class("count")[$"{count} items"]
            ],
            H.Div.Class("card-actions")
            [
                new DslButton("Open"),
                new DslButton("Share", primary: false),
                count > 0 ? new DslButton("Clear", primary: false) : null
            ]
        ]
    ];
}

public sealed partial class DslPage(string userName, bool isAdmin, int unread) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() =>
    [
        H.Html
        [
            H.Body
            [
                H.Div.Id($"user-{userName}").Class(isAdmin ? "page admin" : "page")
                [
                    H.Div.Class("header")
                    [
                        $"Hello {userName}",
                        unread > 0 ? H.Div.Class("unread")[$"{unread} unread"] : "No new messages"
                    ],
                    H.Div.Class("nav")
                    [
                        H.Div.Class("nav-item")["Home"],
                        H.Div.Class("nav-item")["Projects"],
                        H.Div.Class("nav-item")["Settings"],
                        isAdmin ? H.Div.Class("nav-item")["Admin"] : null
                    ],
                    H.Div.Class("cards")
                    [
                        new DslCard("Inbox", $"Messages for {userName}", unread),
                        new DslCard("Tasks", "Things to do", 3),
                        new DslCard("Files", "Recent documents", 0),
                        new DslCard("Teams", "People you work with", 7)
                    ],
                    H.Div.Class("footer")["Static footer text"]
                ]
            ]
        ]
    ];
}

/// <summary>The same page as DslPage, rendered through the async path. The one await completes immediately.</summary>
public sealed partial class DslAsyncPage(string userName, bool isAdmin, int unread) : AsyncHtmlTemplate
{
    public override async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) =>
    [
        H.Html
        [
            H.Body
            [
                H.Div.Id($"user-{userName}").Class(isAdmin ? "page admin" : "page")
                [
                    H.Div.Class("header")
                    [
                        "Hello ",
                        await Task.FromResult(userName),
                        unread > 0 ? H.Div.Class("unread")[$"{unread} unread"] : "No new messages"
                    ],
                    H.Div.Class("nav")
                    [
                        H.Div.Class("nav-item")["Home"],
                        H.Div.Class("nav-item")["Projects"],
                        H.Div.Class("nav-item")["Settings"],
                        isAdmin ? H.Div.Class("nav-item")["Admin"] : null
                    ],
                    H.Div.Class("cards")
                    [
                        new DslCard("Inbox", $"Messages for {userName}", unread),
                        new DslCard("Tasks", "Things to do", 3),
                        new DslCard("Files", "Recent documents", 0),
                        new DslCard("Teams", "People you work with", 7)
                    ],
                    H.Div.Class("footer")["Static footer text"]
                ]
            ]
        ]
    ];
}

public sealed partial class DslLargeRow(int number) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() =>
    [
        H.Tr [ H.Td [ number ], H.Td.Class("name") [ "Row name" ] ]
    ];
}

/// <summary>Has nothing to wait for, so as a sync template it renders in one go.</summary>
public sealed partial class DslLargePage(int[] rows) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() =>
    [
        H.Table [ H.Each(rows, row => new DslLargeRow(row)) ]
    ];
}

/// <summary>The same page as an async template, which is what streams.</summary>
public sealed partial class DslLargeAsyncPage(int[] rows) : AsyncHtmlTemplate
{
    public override async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) =>
    [
        H.Table [ H.Each(rows, row => new DslLargeRow(row)) ]
    ];
}
