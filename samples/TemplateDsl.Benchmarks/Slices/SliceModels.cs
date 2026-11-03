namespace Benchmarks.Slices;

public sealed record SliceButtonModel(string Label, bool Primary = true);

public sealed record SliceCardModel(string Title, string Summary, int Count);

public sealed record SlicePageModel(string UserName, bool IsAdmin, int Unread);

public sealed record SliceLayoutModel(string Title, string Description);
