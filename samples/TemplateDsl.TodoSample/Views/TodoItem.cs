namespace TodoSample.Views;

/// <summary>
/// One todo as it is shown. Ticking and deleting change the counter too, so they return the whole app. Edit only
/// replaces this row with a <see cref="TodoEditor"/>, which replaces itself with this row again when it's done.
/// </summary>
public sealed partial class TodoItem(Todo todo, TodoFilter filter) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        H.Li.Id($"todo-{todo.Id}") [
            H.Label.Class(todo.Done ? "done" : null) [
                H.InputCheckbox.Checked(todo.Done).Attr("hx-post", Urls.Toggle(todo.Id, filter)),
                " ",
                todo.Title
            ],
            H.Button.Type("button").Class("edit")
                .Attr("hx-get", Urls.Edit(todo.Id, filter)).Attr("hx-target", "closest li").Attr("hx-swap", "outerHTML") [ "Edit" ],
            H.Button.Type("button").Attr("hx-delete", Urls.Todo(todo.Id, filter)).Attr("aria-label", $"Delete {todo.Title}") [ "×" ]
        ]
    ];
}
