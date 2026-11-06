namespace TodoSample.Views;

/// <summary>
/// A row in edit mode. Everything in it targets the row itself, so saving or cancelling swaps back only this
/// row, and the rest of the app stays as it is. Escape cancels, like the button.
/// </summary>
public sealed partial class TodoEditor(Todo todo, TodoFilter filter, string? title = null, string? error = null) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        H.Li.Id($"todo-{todo.Id}").Class("editing").Attr("hx-target:inherited", "closest li").Attr("hx-swap:inherited", "outerHTML") [
            H.Form.Autocomplete("off").Attr("hx-put", Urls.Todo(todo.Id, filter)) [
                H.InputText.Name("title").Value(title ?? todo.Title).Attr("aria-label", "Title").Autofocus()
                    .Attr("hx-get", Urls.Todo(todo.Id, filter)).Attr("hx-trigger", "keyup[key=='Escape']"),
                H.Button [ "Save" ],
                H.Button.Type("button").Class("cancel").Attr("hx-get", Urls.Todo(todo.Id, filter)) [ "Cancel" ]
            ],
            error is not null ? H.P.Class("error").Role("alert") [ error ] : null
        ]
    ];
}
