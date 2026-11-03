# TDSL007: Template nodes can only be used in Build

Nodes and children brackets are compiled away: the generator turns `Build` into code that writes them directly.
Outside of `Build` there is nothing that would do that, so a node created there could never be rendered.

```csharp
public sealed partial class Page : HtmlTemplate
{
    private static HtmlElement Header() => H.Header [ H.H1 [ "Title" ] ];   // TDSL007

    public override IEnumerable<HtmlElement> Build() => [Header()];
}
```

## Fix

Create a template for the part, which can be used anywhere a node can:

```csharp
public sealed partial class Header : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() => [H.Header [ H.H1 [ "Title" ] ]];
}

public sealed partial class Page : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() => [new Header()];
}
```
