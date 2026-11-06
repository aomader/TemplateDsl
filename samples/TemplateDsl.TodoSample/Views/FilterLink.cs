namespace TodoSample.Views;

/// <summary>A real link as well, so it can be opened in a new tab, and its address goes into the history.</summary>
public sealed partial class FilterLink(TodoFilter filter, TodoFilter current) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        H.A.Href(Urls.Page(filter)).Class(filter == current ? "current" : null)
            .Attr("hx-get", Urls.Page(filter)).Attr("hx-push-url", "true") [ filter.ToString() ]
    ];
}
