namespace Benchmarks.Layouts;

public sealed record Row(string Name, int Count, bool Active);

public sealed record Dashboard(string UserName, bool IsAdmin, Row[] Rows)
{
    public string Title => $"Dashboard of {UserName}";

    public static Dashboard Sample { get; } = new(
        "Ann",
        IsAdmin: true,
        [.. Enumerable.Range(1, 25).Select(i => new Row($"Item {i}", i * 3, i % 2 == 0))]);
}
