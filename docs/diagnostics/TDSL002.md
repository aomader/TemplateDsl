# TDSL002: Template must be a sealed class

A template with a `Build` method must be a `sealed` class. The generator implements its render method for exactly
that class, so it can't be a struct or a class that others derive from. A record can't derive from the template base
classes at all.

```csharp
public partial class Greeting : HtmlTemplate         // TDSL002
{
    public override IEnumerable<HtmlElement> Build() => [H.P [ "Hi" ]];
}
```

## Fix

Seal the class. To share members between templates, put them in an abstract class without a `Build` method, which
the templates derive from, as the `Page` class in the README's layout example does:

```csharp
public abstract class Page : HtmlTemplate
{
    public abstract string Title { get; }
}

public sealed partial class HomePage : Page { ... }
```
