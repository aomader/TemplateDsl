namespace TodoSample.Views;

public sealed partial class TodoFooter(Todo[] todos, TodoFilter filter) : IHtmlTemplate
{
    private int Left => todos.Count(todo => !todo.Done);

    public IEnumerable<HtmlElement> Build() =>
    [
        H.Footer [
            H.Span [ Left == 1 ? "1 item left" : $"{Left} items left" ],
            new FilterLink(TodoFilter.All, filter),
            new FilterLink(TodoFilter.Active, filter),
            new FilterLink(TodoFilter.Done, filter),
            todos.Any(todo => todo.Done)
                ? H.Button.Attr("hx-post", Urls.ClearDone(filter)) [ "Clear done" ]
                : null
        ]
    ];
}
