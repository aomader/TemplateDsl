# TDSL001: Template must be partial

The generator adds the render methods and the conversion to `HtmlElement` to every template, which it can only do in a
`partial` class, struct or record. Code that uses a template that isn't partial can have errors about these too, which go
away with this one.

```csharp
public sealed class Greeting : IHtmlTemplate          // TDSL001
{
    public IEnumerable<HtmlElement> Build() => [H.P [ "Hi" ]];
}
```

## Fix

Declare it `partial`:

```csharp
public sealed partial class Greeting : IHtmlTemplate
```
