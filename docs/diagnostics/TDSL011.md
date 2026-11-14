# TDSL011: Templates with the same name get no factory

Every template gets a factory named like it in the project's class of template factories, so that `Card("Hello")`
creates a `Card`. Two templates with the same name, in different namespaces or with a different number of type
parameters, would need the same factory name. Neither gets one, because a call couldn't tell which template it means:

```csharp
namespace App.Orders { public sealed partial class Summary(Order order) : IHtmlTemplate { ... } }   // TDSL011
namespace App.Users  { public sealed partial class Summary(User user) : IHtmlTemplate { ... } }     // TDSL011
```

This is a warning: the templates themselves work, and can still be created with `new`.

## Fix

Rename one of them, which also makes the code that uses them easier to read:

```csharp
namespace App.Orders { public sealed partial class OrderSummary(Order order) : IHtmlTemplate { ... } }
namespace App.Users  { public sealed partial class UserSummary(User user) : IHtmlTemplate { ... } }
```

Or keep the names, and create both with `new`, as in `new Orders.Summary(order)`.
