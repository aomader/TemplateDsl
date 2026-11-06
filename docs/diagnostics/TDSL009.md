# TDSL009: A template is only a node in Build

In `Build`, a template converts to its format's node type, such as `HtmlElement`, so that it can be placed at the
top level, in a ternary or by `Each`. The conversion only exists for that: `Build` is compiled into render methods,
and the conversion never runs. Anywhere else, such as when a template is passed to a parameter of type `HtmlElement`,
it is reported, because the template would be lost.

```csharp
public sealed partial class Layout(HtmlElement content) : IHtmlTemplate { ... }

var page = new Layout(new HomePage());          // TDSL009
```

## Fix

Pass the template as content. `IHtmlContent<TContext>` takes any HTML template that renders with that context: one
that needs it, less of it, or none. Without a context, it's `IHtmlContent`. A parameter of either type goes in
brackets like any template, and the context is checked when it compiles:

```csharp
public sealed partial class Layout(IHtmlContent<PageContext> content) : IHtmlTemplate<PageContext>
{
    public IEnumerable<HtmlElement> Build(PageContext context) => [H.Html [ H.Body [ H.Main [ content ] ] ]];
}

var page = new Layout(new HomePage());
```

A type parameter works too, such as `Layout<TContent>(TContent content) where TContent : IHtmlContent<PageContext>`,
and renders a struct without boxing it.
