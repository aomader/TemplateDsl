# TDSL004: Unsupported construct in Build

`Build` is compiled into code that writes the output directly, so the generator has to understand every part of
it that produces output. The message says which part it couldn't translate and why. The most common ones:

| Message | Fix |
|---------|-----|
| `'H.H1' can only be part of the output, not of a variable or a statement` | Write the node where it's output. For a part used in several places, create a template, or a method that returns a string. |
| `Each needs a collection and a lambda with one parameter and an expression body` | Write it as `H.Each(items, item => H.Li [ item.Name ])`. For more than an expression, create a template for the item: `H.Each(items, item => new ItemView(item))`. |
| `'...' is not a template node` | Only nodes, templates, text and values can be children. |
| `'...' must be a collection expression or an array initializer` | Return `[ ... ]` from `Build`, or yield the nodes with `yield return`. |
| `'H.Br' can't have children` | Void elements such as `br`, `img` and `input` take no brackets, and neither does a template that doesn't derive from a base class with `Children`, such as `HtmlTemplateWithChildren`. |
| `'...' is not an attribute of this node`, `'...' appears more than once` | Each attribute exists once per element. Use `.Attr(name, value)` for one without its own method. |
| `'...' must be a constant, because attribute names aren't escaped` | Write the name of `.Attr` as a string literal or a `const`. Its value can be dynamic. |
| `'...' is not a valid attribute name` | An attribute name is not empty and has no spaces, control characters, quotes, backticks, `=`, `<`, `>` or `/`. |
| `'...' has its own method for this kind of value, so it can't be used with Attr` | Use that method, such as `.Id(...)` instead of `.Attr("id", ...)`. A typed attribute still takes a string through `.Attr`. |
| `Children can't be placed in a node with a separator` | A node like a JSON array writes separators between its children, which it can't do for `Children` it doesn't know. |
| `Children can only be placed as a child` | `Children` can't be stored or passed on as a value, only written in brackets. |
| `the method must have a body` | Give `Build` an expression body (`=> [ ... ]`) or a block body. |

Plain C# in `Build`, such as local variables, `if`, `switch`, loops, ternaries and `switch` expressions, is supported.
