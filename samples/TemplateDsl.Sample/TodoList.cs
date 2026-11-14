namespace SampleApp;

public sealed record TodoItem(string Title, bool Done);

public sealed partial class TodoList(IEnumerable<TodoItem> items) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        new Card("Todos") [
            Each(items, item => Div.Class(item.Done ? "todo done" : "todo") [ item.Title ]),
            new Button("Add")
        ]
    ];
}
