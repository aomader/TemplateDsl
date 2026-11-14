namespace TodoSample.Views;

public sealed partial class TodoList(IEnumerable<Todo> todos, TodoFilter filter) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        Ul [
            Each(todos, todo => TodoItem(todo, filter))
        ]
    ];
}
