namespace TodoSample.Views;

/// <summary>
/// Everything that changes when the list does. Adding, ticking, deleting and filtering return all of it, and htmx
/// swaps it in place, so the counter and the filter always agree with the list. Everything inside targets it by
/// inheritance, unless it changes only its own row, as editing does.
/// </summary>
public sealed partial class TodoApp(Todo[] todos, TodoFilter filter, string? error = null) : IHtmlTemplate
{
    private IEnumerable<Todo> Visible => filter switch
    {
        TodoFilter.Active => todos.Where(todo => !todo.Done),
        TodoFilter.Done => todos.Where(todo => todo.Done),
        _ => todos,
    };

    public IEnumerable<HtmlElement> Build() =>
    [
        H.Div.Id("app").Attr("hx-target:inherited", "#app").Attr("hx-swap:inherited", "outerHTML") [
            H.H1 [ "Todos" ],
            new NewTodoForm(filter, error),
            new TodoList(Visible, filter),
            new TodoFooter(todos, filter)
        ]
    ];
}
