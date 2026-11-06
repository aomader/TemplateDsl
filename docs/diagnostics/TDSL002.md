# TDSL002: Template can't be a ref struct

A template converts to its format's node type, such as `HtmlElement`, so that it can be passed wherever content is
expected, and the node holds on to it. A `ref struct` can't be held by anything that lives on the heap.

```csharp
public ref partial struct Greeting : IHtmlTemplate          // TDSL002
{
    public IEnumerable<HtmlElement> Build() => [H.P [ "Hi" ]];
}
```

## Fix

Make it a class, a struct or a record. A small template, such as a row, can be a `readonly record struct`, which a
parent renders without allocating it:

```csharp
public readonly partial record struct Greeting(string Name) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [H.P [ "Hi ", Name ]];
}
```
