namespace TodoSample;

/// <summary>
/// The addresses of the app in one place. Each keeps the current filter, so that the app
/// that comes back still shows what the user was looking at.
/// </summary>
public static class Urls
{
    public static string Page(TodoFilter filter) => "/" + Query(filter);

    public static string Todos(TodoFilter filter) => "/todos" + Query(filter);

    public static string ClearDone(TodoFilter filter) => "/todos/clear-done" + Query(filter);

    public static string Todo(int id, TodoFilter filter) => $"/todos/{id}{Query(filter)}";

    public static string Toggle(int id, TodoFilter filter) => $"/todos/{id}/toggle{Query(filter)}";

    public static string Edit(int id, TodoFilter filter) => $"/todos/{id}/edit{Query(filter)}";

    public static TodoFilter ParseFilter(string? filter) =>
        Enum.TryParse<TodoFilter>(filter, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) ? parsed : TodoFilter.All;

    private static string Query(TodoFilter filter) => "?filter=" + filter.ToString().ToLowerInvariant();
}
