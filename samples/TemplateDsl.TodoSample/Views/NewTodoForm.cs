namespace TodoSample.Views;

public sealed partial class NewTodoForm(TodoFilter filter, string? error) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build()
    {
        yield return H.Form.Autocomplete("off").Attr("hx-post", Urls.Todos(filter)) [
            H.InputText.Name("title").Placeholder("What needs doing?").Attr("aria-label", "New todo").Autofocus(),
            H.Button [ "Add" ]
        ];

        if (error is not null)
        {
            yield return H.P.Class("error").Role("alert") [ error ];
        }
    }
}
