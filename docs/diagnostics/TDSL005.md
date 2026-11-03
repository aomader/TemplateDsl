# TDSL005: Template must implement exactly one template interface

A template is either sync or async, with or without a context. One that implements several of `ITemplate<TNode>`,
`IAsyncTemplate<TNode>` and their variants with a context, through its base classes or directly, has no single way
to render.

```csharp
public sealed partial class Page : HtmlTemplate, IAsyncTemplate<HtmlElement> { ... }   // TDSL005
```

## Fix

Derive from exactly one base class, such as `HtmlTemplate`, `AsyncHtmlTemplate` or `HtmlTemplate<TContext>`, and
don't add template interfaces yourself.
