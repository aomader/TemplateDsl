# TDSL010: The class of the template factories can't have its name

The generator writes a static class with a factory for every template of the project, so that templates can be
written without `new` (see "Creating templates without new" in the README). By default it is `Templates` in the
project's root namespace, such as `App.Templates`. That name can't be used if something else in the project has it
already, most often a folder `Templates`, whose files are in the namespace `App.Templates`:

```
App/
  Templates/          // namespace App.Templates: TDSL010
    Card.cs
```

A type named `App.Templates` that isn't a static class has the same problem. A `static partial class Templates` of the
project's own is fine: it is the project's part of the class, and the factories are added to it.

## Fix

Give the class another name with the MSBuild property `TemplateDslTemplatesClass`. A name without a namespace goes in
the root namespace:

```xml
<PropertyGroup>
  <TemplateDslTemplatesClass>Views</TemplateDslTemplatesClass>              <!-- App.Views -->
  <TemplateDslTemplatesClass>App.Ui.Templates</TemplateDslTemplatesClass>  <!-- or a namespace of its own -->
</PropertyGroup>
```

Then import that class instead, as in `using static App.Ui.Templates;`.
