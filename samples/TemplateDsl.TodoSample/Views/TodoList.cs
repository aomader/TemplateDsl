namespace TodoSample.Views;

public sealed partial class TodoList(IEnumerable<Todo> todos, TodoFilter filter) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() =>
    [
        H.Ul [
            H.Each(todos, todo => new TodoItem(todo, filter))
        ]
    ];
}
