namespace TodoSample;

public sealed record Todo(int Id, string Title, bool Done);

public enum TodoFilter
{
    All,
    Active,
    Done,
}

/// <summary>Keeps the todos in memory, so they last until the app restarts.</summary>
public sealed class TodoStore
{
    private readonly Lock _lock = new();
    private readonly List<Todo> _todos = [];
    private int _nextId = 1;

    public Todo[] GetAll()
    {
        lock (_lock)
        {
            return [.. _todos];
        }
    }

    public Todo? Find(int id)
    {
        lock (_lock)
        {
            return _todos.Find(todo => todo.Id == id);
        }
    }

    public void Add(string title)
    {
        lock (_lock)
        {
            _todos.Add(new Todo(_nextId++, title, Done: false));
        }
    }

    /// <summary>The todo with its new title, or null if it doesn't exist anymore.</summary>
    public Todo? Rename(int id, string title) => Update(id, todo => todo with { Title = title });

    public void Toggle(int id) => Update(id, todo => todo with { Done = !todo.Done });

    public void Delete(int id)
    {
        lock (_lock)
        {
            _todos.RemoveAll(todo => todo.Id == id);
        }
    }

    public void ClearDone()
    {
        lock (_lock)
        {
            _todos.RemoveAll(todo => todo.Done);
        }
    }

    private Todo? Update(int id, Func<Todo, Todo> change)
    {
        lock (_lock)
        {
            var index = _todos.FindIndex(todo => todo.Id == id);

            if (index < 0)
            {
                return null;
            }

            return _todos[index] = change(_todos[index]);
        }
    }
}
