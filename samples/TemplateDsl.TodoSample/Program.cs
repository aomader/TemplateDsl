using Microsoft.AspNetCore.Http.HttpResults;
using TodoSample;
using TodoSample.Views;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<TodoStore>();

var app = builder.Build();

// The whole app: the list, the counter and the filter change together.

app.MapGet("/", (HttpContext context, TodoStore store, string? filter) =>
    App(context, store, filter));

app.MapPost("/todos", async (HttpContext context, TodoStore store, string? filter) =>
{
    var title = await ReadTitleAsync(context);

    if (title.Length == 0)
    {
        return App(context, store, filter, "Please enter something to do.");
    }

    store.Add(title);
    return App(context, store, filter);
});

app.MapPost("/todos/clear-done", (HttpContext context, TodoStore store, string? filter) =>
{
    store.ClearDone();
    return App(context, store, filter);
});

app.MapPost("/todos/{id:int}/toggle", (HttpContext context, TodoStore store, int id, string? filter) =>
{
    store.Toggle(id);
    return App(context, store, filter);
});

app.MapDelete("/todos/{id:int}", (HttpContext context, TodoStore store, int id, string? filter) =>
{
    store.Delete(id);
    return App(context, store, filter);
});

// One row: editing a title changes nothing else, so only the row is swapped. A todo that
// was deleted in the meantime is a 404 with no content, which removes the row.

app.MapGet("/todos/{id:int}", Results<TemplateResult, NotFound> (TodoStore store, int id, string? filter) =>
    store.Find(id) is { } todo
        ? new TodoItem(todo, Urls.ParseFilter(filter)).ToResult()
        : TypedResults.NotFound());

app.MapGet("/todos/{id:int}/edit", Results<TemplateResult, NotFound> (TodoStore store, int id, string? filter) =>
    store.Find(id) is { } todo
        ? new TodoEditor(todo, Urls.ParseFilter(filter)).ToResult()
        : TypedResults.NotFound());

app.MapPut("/todos/{id:int}", async Task<Results<TemplateResult, NotFound>> (HttpContext context, TodoStore store, int id, string? filter) =>
{
    var title = await ReadTitleAsync(context);

    if (store.Find(id) is not { } todo)
    {
        return TypedResults.NotFound();
    }

    if (title.Length == 0)
    {
        return new TodoEditor(todo, Urls.ParseFilter(filter), title, "A todo needs a title.")
            .ToResult(statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    return store.Rename(id, title) is { } renamed
        ? new TodoItem(renamed, Urls.ParseFilter(filter)).ToResult()
        : TypedResults.NotFound();
});

app.Run();

// htmx asks for just the app when it swaps part of the page. Anything else, such as a
// first visit, a reload or going back in the history, gets the whole page.
static TemplateResult App(HttpContext context, TodoStore store, string? filter, string? error = null)
{
    context.Response.Headers.Vary = "HX-Request-Type";

    var todoApp = new TodoApp(store.GetAll(), Urls.ParseFilter(filter), error);
    var isPartial = context.Request.Headers["HX-Request-Type"] == "partial";

    return (isPartial ? (HtmlTemplate)todoApp : new Layout(todoApp))
        .ToResult(statusCode: error is null ? null : StatusCodes.Status422UnprocessableEntity);
}

static async Task<string> ReadTitleAsync(HttpContext context)
{
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    return form["title"].ToString().Trim();
}
