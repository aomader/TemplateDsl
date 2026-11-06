# TDSL005: Template must implement exactly one template interface

A template is either sync or async, with or without a context. One that implements several of `ITemplate<TNode>`,
`IAsyncTemplate<TNode>` and their variants with a context, through interfaces such as `IHtmlTemplate` or directly, has
no single way to render.

```csharp
public sealed partial class Page : IHtmlTemplate, IAsyncTemplate<HtmlElement> { ... }   // TDSL005
```

## Fix

Implement exactly one of them, such as `IHtmlTemplate`, `IAsyncHtmlTemplate` or `IHtmlTemplate<TContext>`. To take
children, add `IAcceptsChildren`, which isn't a template interface.
