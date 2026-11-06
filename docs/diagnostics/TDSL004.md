# TDSL004: Unsupported construct in Build

`Build` is compiled into code that writes the output directly, so the generator has to understand every part of
it that produces output. The message says which part it couldn't translate and why. The most common ones:

| Message | Fix |
|---------|-----|
| `'H.H1' can only be part of the output, not of a variable or a statement` | Write the node where it's output. For a part used in several places, create a template, or a method that returns a string. |
| `Each needs a collection and a lambda with one parameter and an expression body` | Write it as `H.Each(items, item => H.Li [ item.Name ])`. For more than an expression, create a template for the item: `H.Each(items, item => new ItemView(item))`. |
| `'...' is not a template node` | Only nodes, templates, text and values can be children. Nodes are written where they are output, in `Build`. |
| `'...' is a HtmlElement, which Build can only place if it is written there` | Content is passed as a template, such as an `IHtmlContent` or `IHtmlContent<TContext>` parameter, which brackets take like any template. |
| `'...' must be a collection expression or an array initializer` | Return `[ ... ]` from `Build`, or yield the nodes with `yield return`. |
| `'H.Br' can't have children` | Void elements such as `br`, `img` and `input` take no brackets, and neither does a template that doesn't implement `IAcceptsChildren`. |
| `'...' is not an attribute of this node`, `'...' appears more than once` | Each attribute exists once per element. Use `.Attr(name, value)` for one without its own method. |
| `'...' must be a constant, because attribute names aren't escaped` | Write the name of `.Attr` as a string literal or a `const`. Its value can be dynamic. |
| `'...' is not a valid attribute name` | An attribute name is not empty and has no spaces, control characters, quotes, backticks, `=`, `<`, `>` or `/`. |
| `'...' has its own method for this kind of value, so it can't be used with Attr` | Use that method, such as `.Id(...)` instead of `.Attr("id", ...)`. A typed attribute still takes a string through `.Attr`. |
| `Children can't be placed in a node with a separator` | A node like a JSON array writes separators between its children, which it can't do for `Children` it doesn't know. |
| `Children can only be placed as a child` | `Children` can't be stored or passed on as a value, only written in brackets. |
| `'...' is assigned in the children of a template, which only get a copy of it` | Children become a method of the template, which gets the variables of `Build` it uses as copies. Assign the variable before the brackets. |
| `'...' is used in the children of a template, which get a copy of it, but a ref, a ref struct or an anonymous type can't be copied` | Use a variable of another type in the children, such as a record instead of an anonymous type, or a string instead of a span. |
| `the method must have a body` | Give `Build` an expression body (`=> [ ... ]`) or a block body. |

Plain C# in `Build`, such as local variables, `if`, `switch`, loops, ternaries and `switch` expressions, is supported.
