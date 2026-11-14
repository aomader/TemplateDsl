# TDSL012: A name of your own hides an element

With `using static TemplateDsl.Html.Elements;`, elements are written without a prefix. C# looks for a name in the
template, its locals and the types in scope before it looks at what `using static` imports, so a name of your own wins
over an element of the same name. C# then reports an error about what the name is, which doesn't mention the element:

```csharp
public sealed partial class Button(string text) : IHtmlTemplate { ... }

public sealed partial class Page(string title) : IHtmlTemplate
{
    public string Title => title;

    public IEnumerable<HtmlElement> Build() =>
    [
        Title [ Title ],        // CS1503, TDSL012: 'Title' is the property 'Title' here
        Button [ "Save" ]       // CS0119, TDSL012: 'Button' is the template 'Button' here
    ];
}
```

TDSL012 says which name hides the element, next to the error of C#. It is only reported where that use fails, so
using the property or the template as what it is, as in `P [ Title ]` or `Button("Save")`, is fine.

## Fix

Write the element with its class, which always means the element:

```csharp
Elements.Title [ Title ],
Elements.Button [ "Save" ]
```

Or rename what hides it, such as the property to `PageTitle` or the template to `SaveButton`, if the element is used
in more than one place.
