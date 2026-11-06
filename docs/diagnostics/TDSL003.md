# TDSL003: Template must be a top-level type

A template can't be nested in another type or `file`-local. The generator writes the rest of the class into a separate
file: there, a `file`-local type isn't visible, and a nested one would need every type around it to be partial too.

```csharp
public static class Views
{
    public sealed partial class Row : IHtmlTemplate { ... }   // TDSL003: nested
}

file sealed partial class Row : IHtmlTemplate { ... }         // TDSL003: file-local
```

## Fix

Move it to the top level of a namespace, and make it `public` or `internal`. To group templates, use a namespace
instead of a containing class.
