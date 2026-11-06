namespace TodoSample.Views;

public sealed partial class TodoList(IEnumerable<Todo> todos, TodoFilter filter) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        H.Ul [
            H.Each(todos, todo => new TodoItem(todo, filter))
        ]
    ];
}
