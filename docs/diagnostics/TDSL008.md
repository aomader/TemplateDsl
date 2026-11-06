# TDSL008: A nested template needs a context its parent doesn't have

A nested template gets its context from the template that writes it (see "Context" in the README). It can ask for
less than that, such as one interface of the context, but not for more, and not for one if the parent has none.

```csharp
public sealed partial class SaveButton : IHtmlTemplate<IHasStrings> { ... }

public sealed partial class Form : IHtmlTemplate                     // no context
{
    public IEnumerable<HtmlElement> Build() => [new SaveButton()];   // TDSL008
}
```

## Fix

Give the parent a context that has what the child needs, either the same type or one that implements it:

```csharp
public sealed record AppContext(Strings Strings, User User) : IHasStrings;

public sealed partial class Form : IHtmlTemplate<AppContext>
{
    public IEnumerable<HtmlElement> Build(AppContext context) => [new SaveButton()];
}
```

If only this one child needs the value, pass it to its constructor instead.
