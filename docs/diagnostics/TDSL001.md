# TDSL001: Template must be partial

The generator adds the render method to every template, which it can only do in a `partial` class.

```csharp
public sealed class Greeting : HtmlTemplate          // TDSL001
{
    public override IEnumerable<HtmlElement> Build() => [H.P [ "Hi" ]];
}
```

## Fix

Declare it `sealed partial`:

```csharp
public sealed partial class Greeting : HtmlTemplate
```
