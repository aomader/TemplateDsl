namespace TodoSample.Views;

public sealed partial class TodoFooter(Todo[] todos, TodoFilter filter) : IHtmlTemplate
{
    private int Left => todos.Count(todo => !todo.Done);

    public IEnumerable<HtmlElement> Build() =>
    [
        Footer [
            Span [ Left == 1 ? "1 item left" : $"{Left} items left" ],
            FilterLink(TodoFilter.All, filter),
            FilterLink(TodoFilter.Active, filter),
            FilterLink(TodoFilter.Done, filter),
            todos.Any(todo => todo.Done)
                ? Button.Attr("hx-post", Urls.ClearDone(filter)) [ "Clear done" ]
                : null
        ]
    ];
}
