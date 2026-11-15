namespace TodoSample.Views;

public sealed partial class NewTodoForm(TodoFilter filter, string? error) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build()
    {
        yield return Form.Autocomplete("off").Attr("hx-post", Urls.Todos(filter)) [
            InputText.Name("title").Placeholder("What needs doing?").AriaLabel("New todo").Autofocus(),
            Button [ "Add" ]
        ];

        if (error is not null)
        {
            yield return P.Class("error").Role(Role.Alert) [ error ];
        }
    }
}
