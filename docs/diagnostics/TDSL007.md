# TDSL007: Template nodes can only be used in Build

Nodes and children brackets are compiled away: the generator turns `Build` into code that writes them directly.
Outside of `Build` there is nothing that would do that, so a node created there could never be rendered.

```csharp
public sealed partial class Page : IHtmlTemplate
{
    private static HtmlElement Banner() => Header [ H1 [ "Title" ] ];   // TDSL007

    public IEnumerable<HtmlElement> Build() => [Banner()];
}
```

## Fix

Create a template for the part, which can be used anywhere a node can:

```csharp
public sealed partial class Banner : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [Header [ H1 [ "Title" ] ]];
}

public sealed partial class Page : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [new Banner()];
}
```
