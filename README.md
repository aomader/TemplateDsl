<h1>
<img src="https://raw.githubusercontent.com/aomader/TemplateDsl/main/assets/icon.png" alt="" width="24" height="24">
TemplateDsl
</h1>

[![NuGet version](https://img.shields.io/nuget/v/TemplateDsl?label=NuGet)](https://www.nuget.org/packages/TemplateDsl/)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](https://opensource.org/licenses/MIT)


TemplateDsl lets you write **HTML templates** as **plain C# classes**. A **source generator** compiles them into **render methods** that write **UTF-8 straight to the response**, so typos, wrong values and missing context **fail the build instead of a request**.

- **Type-safe.** Typos, wrong attribute values and a missing context are compile errors, not runtime surprises.
- **Fast.** Templates write UTF-8 straight to the output, with almost no allocations.
- **Streaming.** Async templates send their output while they render.
- **Composable.** Templates are classes you nest, pass around and reuse like any other code.
- **Explicit, with little boilerplate.** Data comes through constructors, and logic is plain C#.
- **Safe by default.** Everything is escaped unless you say so.

## Sneak peek

```csharp
// A simple component
public sealed partial class OrderRow(Order order) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() =>
    [
        H.Tr.Class(order.IsLate ? "late" : null) [
            H.Td [ order.Id ],
            H.Td [ order.Product ],
            H.Td [ H.Time.DateTime(order.Due) [ order.Due ] ]
        ]
    ];
}

// An async page that streams a list of orders, with a context for the current user
public sealed partial class OrdersPage(IAsyncEnumerable<Order> orders) : AsyncHtmlTemplate<PageContext>
{
    public override async Task<IEnumerable<HtmlElement>> BuildAsync(PageContext context, CancellationToken ct)
    {
        var title = $"Orders of {context.UserName}";
        
        return [
            H.Raw("<!DOCTYPE html>"),
            H.Html [
                H.Head [ H.Title [ title ] ],
                H.Body [
                    H.H1 [ title ],
                    H.Table [ await H.EachAsync(orders, order => new OrderRow(order)) ]
                ]
            ]
        ];
    }
}

// A context that is passed to the page and its nested templates
public sealed record PageContext(string UserName);

services.AddScoped<PageContext>(sp =>
    new PageContext(sp.GetRequiredService<IUserService>().GetCurrentUserName()));

// Return the page from a minimal API endpoint, with the context injected from the services
app.MapGet("/orders", (OrderStore store, [FromServices] PageContext context) =>
    new OrdersPage(store.StreamAll()).ToResult(context));
```

## Getting started

> [!WARNING]
> The packages are in **preview**. The API may change until 1.0, and a minor version can break the API.

1. [Install](#installation) the [NuGet package(s)]() into your .NET 10+ project:

```shell
# The core package for HTML-based templates.
dotnet add package TemplateDsl.Html --prerelease

# Optional: convenience methods for serving templates from ASP.NET Core endpoints and controllers.
dotnet add package TemplateDsl.AspNetCore --prerelease
```

2. Write a template as a `sealed partial` class that derives from `HtmlTemplate` or `AsyncHtmlTemplate`, and implement its `Build` or `BuildAsync` method. Use the static class `H` from the `TemplateDsl.Html` namespace to write elements, attributes and children.

```csharp
using TemplateDsl.Html;

public sealed partial class TodoList(IEnumerable<Todo> todos) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() =>
    [
        H.Ul [
            H.Each(todos, todo => H.Li [ todo.Text ])
        ]
    ];
}
```

3. Render it to a string or stream.

```csharp
// Render to a string.
var html = new TodoList(todos).RenderToString();

// Render to a stream asynchronously.
await new TodoList(todos).RenderToAsync(httpContext.Response.BodyWriter, httpContext.RequestAborted);
```

4. Or return it directly from a minimal API endpoint or a controller action.

```csharp
app.MapGet("/", (TodoStore store) =>
    // Returns a `TemplateResult` that streams the output of the template to the response.
    new TodoList(store.All()).ToResult());
```

## Installation

The following packages are published to NuGet and may be installed with, e.g., `dotnet add package <package-name> --prerelease`.

| Package             | Description                                                                     | Version |
|---------------------|---------------------------------------------------------------------------------|---------|
| `TemplateDsl`       | The core package for templates, with the base classes and the source generator. | [![NuGet version](https://img.shields.io/nuget/v/TemplateDsl?label=NuGet)](https://www.nuget.org/packages/TemplateDsl/) |
| `TemplateDsl.Html` | Brings the HTML elements and attributes to the template DSL.                                                                | [![NuGet version](https://img.shields.io/nuget/v/TemplateDsl.Html?label=NuGet)](https://www.nuget.org/packages/TemplateDsl.Html/) |
| `TemplateDsl.AspNetCore` | Adds convenience methods for returning templates from ASP.NET Core endpoints and controllers. | [![NuGet version](https://img.shields.io/nuget/v/TemplateDsl.AspNetCore?label=NuGet)](https://www.nuget.org/packages/TemplateDsl.AspNetCore/) |

## Features

### Templates and elements

Elements live in the static class `H`, so autocomplete lists them after `H.` and they never clash with the
lowercase parameters of a template. Attributes are chained in the order they should appear, and children go in
brackets, which end the chain:

```csharp
H.Div.Id("main").Class("card") [
    H.H2 [ title ],
    H.P [ "Some text" ]
]
```

All standard HTML elements are available. Like all C# methods, their names are PascalCase, so they never clash with
keywords: `H.Object`, `.Class(...)` and `.For(...)`. Attributes of several words are cased like their DOM property, as in
`.TabIndex(...)`, `.ReadOnly()` and `.ColSpan(...)`. Elements like `H.Br` and `H.Img.Src(url)` have no children, and the
doctype is written with `H.Raw("<!DOCTYPE html>")`.

Nodes and children brackets can only be used inside `Build`, so that everything is checked and compiled. Templates
must be `sealed partial` classes, or abstract classes without a `Build` method. What the generator can't compile is a
build error with its own ID, each explained in [docs/diagnostics](https://github.com/aomader/TemplateDsl/blob/main/docs/diagnostics/README.md).

### Attributes and values

Boolean attributes work with or without a value, as in `.Hidden()` and `.Hidden(isHidden)`. A null value leaves an
attribute out. Attributes without their own method use `.Attr`, whose name must be a constant while the value can be
dynamic:

```csharp
H.Div.Id("main").Hidden(isHidden).Attr("data-user-id", userId).Attr("aria-label", "Profile") [
    title
]
```

Attributes take what makes sense for them. `.ColSpan(2)` and `.TabIndex(0)` take an `int`, `H.Meter.Value(0.5)` a
`double`, and `.Draggable(true)` writes `draggable="true"`. `.Class(...)`, `.Href(...)` and most others take only
strings, while `.Id(...)`, `.Title(...)`, `.DateTime(...)` and `.Value(...)` on an input also take numbers and dates.
For a value their type doesn't allow, `.Attr("colspan", "{{columns}}")` still works on the typed ones.

Inputs come in a variant per type, which takes only what HTML allows for it, with values of the right type:

```csharp
H.InputNumber.Name("qty").Min(0).Max(10).Step(0.5).Value(quantity)   // any number: int, double, decimal
H.InputDate.Name("due").Min(today).Value(order.Due)                  // DateOnly: value="2026-11-02"
H.InputEmail.Name("mail").Placeholder("you@example.com").Required()
H.InputCheckbox.Name("done").Checked(todo.Done)
H.InputCheckbox.Min(3)                                                // compile error: no min on a checkbox
```

`H.Input` itself stays as loose as HTML: it has every attribute of an input, and its type is an enum, as in
`H.Input.Type(InputType.Week)`, for when the type is only known at runtime. `InputType.DatetimeLocal` is written as
`datetime-local`.

Numbers, dates, `Guid`s and `char`s can be used directly, as in `H.P [ count ]` and `.Attr("data-id", id)`. They are
written without creating a string, escaped, and null leaves them out. For a specific format, use interpolation, as in
`$"{price:F2}"`. A `bool` is not text, so write it with `$"{flag}"` or a word of your choice.

Their culture depends on who reads them, also inside an interpolated string:

```csharp
H.Div.Style($"width:{ratio}%").Attr("data-price", price) [   // width:1.5%, data-price="1.5": for programs, always invariant
    price,                                                   // 1,5: text for people, in the current culture
    H.Img.Alt($"Costs {price}")                              // Costs 1,5: an attribute for people
]
```

- **For people:** text, and `title`, `alt`, `placeholder`, `label`, `abbr`, `aria-label`, `aria-description`,
  `aria-placeholder`, `aria-roledescription` and `aria-valuetext`. These use the current culture, so set it per
  request, with `UseRequestLocalization` for example. Otherwise, people get the culture of the server.
- **For programs:** every other attribute. These use the invariant culture, and dates are written as ISO 8601.

A string is never reformatted, so `.Value(price.ToString("C"))` and `string.Create(culture, $"...")` are the way to go for a specific culture.

### Logic in Build

`Build` is ordinary C#. It can use local variables, statements like `i++` or `list.Add(x)`, `if`, `switch`, `foreach`,
`for`, `while`, ternaries and `switch` expressions, in which each branch is output:

```csharp
H.Div [
    order.Status switch
    {
        Status.Paid => H.Span.Class("ok") [ "Paid" ],
        Status.Open when order.IsLate => new LateNotice(order),
        Status.Open => order.Total,
        _ => null
    }
]
```

Lists are written with `H.Each(items, item => ...)`, and async sequences with `await H.EachAsync(items, ...)`.

Nodes can only be part of the output. A variable or a statement can't hold one, so `var title = H.H1;` is an error, and
a template or a method returning a string is what to use instead.

### Components and layouts

Every template is a node, so templates are nested like elements. A template that derives from
`HtmlTemplateWithChildren` takes children in brackets and places them with `Children`:

```csharp
public sealed partial class Card(string title) : HtmlTemplateWithChildren
{
    public override IEnumerable<HtmlElement> Build() =>
    [
        H.Div.Class("card") [
            H.Div.Class("title") [ title ],
            H.Div.Class("body") [ Children ]
        ]
    ];
}

new Card("Hello") [ "Some text", H.Div [ "nested" ] ]
```

Templates can be generic. A value of a type parameter isn't a node, so the template writes it through what it is given
for it, such as a `Func<TItem, string>` or a template per item:

```csharp
public sealed partial class Table<TItem>(IEnumerable<TItem> items, Func<TItem, HtmlTemplate> row) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() => [H.Table [ H.Tbody [ H.Each(items, item => row(item)) ] ]];
}

new Table<Order>(orders, order => new OrderRow(order))
```

A type parameter constrained to a template, as in `where TPart : HtmlTemplate`, is nested like any template, and gets
the context if it needs one. Children in brackets need the template's own type, because each template has its own
brackets.

A page can be plain content, with its layout chosen from outside. An abstract class in your app says what layouts
need from a page:

```csharp
public abstract class Page : HtmlTemplate
{
    public abstract string Title { get; }
}

public sealed partial class HomePage(User user) : Page
{
    public override string Title => $"Hello {user.Name}";

    public override IEnumerable<HtmlElement> Build() => [H.H1 [ Title ]];
}

public sealed partial class MainLayout(Page page) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() =>
    [
        H.Html [ H.Head [ H.Title [ page.Title ] ], H.Body [ H.Main [ page ] ] ]
    ];
}

HtmlTemplate view = user.IsAdmin ? new AdminLayout(page) : new MainLayout(page);
```

### Context

Some things are needed all over a page, such as translations, asset URLs or the current user. Passing them through
every constructor is noise, so a template can ask for a context instead. It arrives as a parameter of `Build`, and
nested templates get it from their parent:

```csharp
public interface IHasStrings { Strings Strings { get; } }

public sealed record PageContext(Strings Strings, User User) : IHasStrings;

public sealed partial class SaveButton : HtmlTemplate<IHasStrings>
{
    public override IEnumerable<HtmlElement> Build(IHasStrings context) =>
    [
        H.Button.Type("submit") [ context.Strings.Save ]
    ];
}

public sealed partial class SettingsPage : HtmlTemplate<PageContext>
{
    public override IEnumerable<HtmlElement> Build(PageContext context) =>
    [
        H.H1 [ context.User.Name ],
        new Card("Settings") [ new SaveButton() ]
    ];
}

var html = new SettingsPage().RenderToString(pageContext);
```

Everything is checked at compile time:

- A template that needs a context can't be rendered without one.
- A child can ask for less than its parent has, such as one interface of the context, but not for more. Otherwise the
  build fails with [TDSL008](https://github.com/aomader/TemplateDsl/blob/main/docs/diagnostics/TDSL008.md).
- Children in brackets belong to the template that writes them, so they use its context, whatever the template they're
  placed in has. Above, `Card` needs no context at all.
- Templates without a context can be nested anywhere and cost nothing extra.

The same exists for the other base classes: `HtmlTemplateWithChildren<TContext>`, `AsyncHtmlTemplate<TContext>` and
`AsyncHtmlTemplateWithChildren<TContext>`, whose method is `BuildAsync(TContext context, CancellationToken ct)`. The
context is for what stays the same during a render. A component's own data still goes in its constructor.

### Async and streaming

A sync template renders in one go: `RenderToAsync` holds all of its output in memory, then sends it. That suits normal
pages. An async template streams: output is sent as it is rendered, a slow client slows down rendering, and a client
that goes away stops it. So a large page should be an `AsyncHtmlTemplate`, even if it has nothing to await:

```csharp
public sealed partial class Export(List<Row> rows) : AsyncHtmlTemplate
{
    public override async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) =>
    [
        H.Table [ H.Each(rows, row => new RowView(row)) ]
    ];
}
```

Async content is awaited like anything else in C#: `await H.EachAsync(items, ...)` and `await new SlowPart()`. Only an
`AsyncHtmlTemplate` can do that, so a layout that may host an async page should be one. Forgetting the `await`, or using
it in a sync template, is an ordinary compile error.

A sync template nested in an async one is written in one go too, and sent right after. A sync template with `Children`
can still wait for children that await, as in `new Card() [ await LoadTextAsync(ct) ]`.

If the client goes away or the token fires, `RenderToAsync` stops with an `OperationCanceledException`, so finishing
normally means everything was sent. ASP.NET Core handles that exception for an aborted request, so endpoints don't need
to catch it. The [web sample](https://github.com/aomader/TemplateDsl/tree/main/samples/TemplateDsl.WebSample) sends each list entry as soon as it exists.

### ASP.NET Core

With the `TemplateDsl.AspNetCore` package, an endpoint returns a template with `ToResult()`. The handler gets its
services and route values as parameters, as usual, and passes them to the constructor, so the compiler checks them:

```csharp
app.MapGet("/", (TodoStore store) => new TodoPage(store.GetAll()).ToResult());

app.MapPost("/todos", (...) => new TodoPage(...).ToResult(statusCode: StatusCodes.Status422UnprocessableEntity));

app.MapGet("/todos/{id}", Results<TemplateResult, NotFound> (int id, TodoStore store) =>
    store.Find(id) is { } todo ? new TodoView(todo).ToResult() : TypedResults.NotFound());

app.MapGet("/feed.xml", (Feed feed) => new FeedTemplate(feed).ToResult());   // application/rss+xml
```

- **Content-Type:** the format declares it, such as `text/html; charset=utf-8`, and `ToResult(contentType: ...)`
  overrides it.
- **Context:** a template that needs one is given it with `ToResult(context)`, so the compiler checks that it gets one.
  Take it as a parameter of the endpoint, as in `([FromServices] PageContext context) => ...`. The `[FromServices]`
  matters: without it, a minimal API reads a type it doesn't find among the services from the request body.
- **Headers** go out with the first output, so set them before returning, with an `HttpResponse` parameter for example.
- **Errors:** once an async template has sent output, the status is already 200, and an exception can only abort the
  response. Load what can fail before returning, and stream what can't. A sync template sends nothing until it's done.
- **Controllers:** the result is also an `IActionResult`.
- **Namespace:** `ToResult` and the result types are in `TemplateDsl.AspNetCore`, which the package imports
  automatically. A project that doesn't want that removes it with `<Using Remove="TemplateDsl.AspNetCore" />`, and adds
  the `using` where it needs it.

### Security

Every value is escaped, in text and in attributes, numbers and dates included. Only `H.Raw(...)` writes text as it is,
so never pass it untrusted input. Escaping keeps a value inside the text or attribute it's in, but some places
interpret what they hold, and there untrusted input needs more than escaping:

- **URLs**, as in `.Href(...)`, `.Src(...)`, `.Action(...)`, `.FormAction(...)` and `.Srcset(...)`: a `javascript:` URL
  runs code. Only allow the schemes you expect, such as `https:`, or relative URLs.
- **Code and styles**: event handler attributes such as `.Attr("onclick", ...)`, `.Style(...)`, and the contents of
  `<script>` and `<style>`.
- **Markup and redirects**: `H.Iframe.Srcdoc(...)` is a whole HTML document, and the content of
  `<meta http-equiv="refresh">` holds a URL.

Text in `<script>` is escaped like any other text. That keeps it from closing the element, but it also changes `<` and
`&` in code. For data in a script, `H.Raw(JsonSerializer.Serialize(data))` is safe with System.Text.Json's default
encoder, which writes `<`, `>` and `&` as `\u003C` and the like. A relaxed encoder such as `UnsafeRelaxedJsonEscaping`
isn't.

To report a vulnerability, see [SECURITY.md](https://github.com/aomader/TemplateDsl/blob/main/SECURITY.md).

## Performance

The [TemplateDsl.Benchmarks](https://github.com/aomader/TemplateDsl/tree/main/samples/TemplateDsl.Benchmarks) sample compares TemplateDsl against Razor Pages and Blazer (where it makes sense), as well as [Razor Slices](https://github.com/DamianEdwards/RazorSlices).

Measured with BenchmarkDotNet on .NET SDK 10.0.103 on an AMD Ryzen 7 3700X under Windows 11, the results are favorable to TemplateDsl in both speed and memory usage in all (artificial) test cases:

| Benchmark | TemplateDsl | Razor Slices | Blazor | Razor Pages |
|-----------|------------:|-------------:|-------:|------------:|
| Button to string | 123 ns, 144 B | 160 ns, 328 B | 1,988 ns, 3,792 B | |
| Page to string | 1.70 µs, 3.7 KB | 2.78 µs, 6.7 KB | 19.1 µs, 21.4 KB | |
| Page to stream | 1.87 µs, 0.9 KB | 5.28 µs, 3.9 KB | 19.2 µs, 18.8 KB | |
| Whole request with a layout | 79 µs, 11.6 KB | 92 µs, 16.4 KB | | 189 µs, 79.0 KB |

To run them yourself, execute `dotnet run -c Release --project samples/TemplateDsl.Benchmarks -- --filter '*'`.

## Samples

- [`samples/TemplateDsl.TodoSample`](https://github.com/aomader/TemplateDsl/tree/main/samples/TemplateDsl.TodoSample): a classic todo list with ASP.NET Core minimal
  APIs and [htmx 4](https://four.htmx.org/), built from small components. Every action swaps the app in place without a page reload, and
  editing a todo inline swaps only its row.
- [`samples/TemplateDsl.WebSample`](https://github.com/aomader/TemplateDsl/tree/main/samples/TemplateDsl.WebSample): streaming with layouts and async components.
- [`samples/TemplateDsl.FormatSamples`](https://github.com/aomader/TemplateDsl/tree/main/samples/TemplateDsl.FormatSamples): complete RSS and JSON formats.

## Other formats

HTML is one format among others. A format is a set of declarations in its own project, which needs no generator of
its own:

```csharp
using TemplateDsl;

[TemplateFormat(typeof(XmlText), ContentType = "application/xml; charset=utf-8")]
[Escape("text", "&", "&amp;", "<", "&lt;", ">", "&gt;")]
public abstract partial class XmlNode : TemplateNode;

[TextNode(Escape = "text")]
public sealed partial class XmlText : XmlNode;

[Node(Start = "<item", Body = ">", End = "</item>")]
public sealed partial class Item : XmlNode
{
    [Attr(" id=\"", "\"", Escape = "text")]
    public partial Item Id(string? value);
}

[TemplateTags(typeof(XmlNode))]
public static partial class Xml
{
    public static Item Item => new();
}

[TemplateBase]
public abstract partial class XmlTemplate : XmlNode, ITemplate<XmlNode>;
```

An attribute is a partial method that returns the node, which the generator implements, so this is written
`Xml.Item.Id("a") [ "text" ]`. Its parameter says what it takes, as for HTML: a string, a number such as `int?`, an enum,
`bool?` for the words true and false, or `bool? value = true` for an attribute without a value. Escape every attribute
with a value: a number type can be one of the app's own, which writes whatever it formats as. Attributes that several
nodes share go in a base class that takes the node as a type parameter and returns it, as `HtmlTag<TElement>` does.

Templates in the format derive from its `[TemplateBase]` class, and one with a context from
`[TemplateBase] public abstract partial class XmlTemplate<TContext> : XmlNode, ITemplate<XmlNode, TContext> where TContext : notnull;`.
A format opts in to numbers and dates with `[TemplateFormat(typeof(XmlText), FormatsValues = true)]`. Its text uses the
current culture unless it sets `TextCulture = ValueCulture.Invariant`, and its attributes are invariant unless they set
`Culture = ValueCulture.Current`. The formats that fit are text formats with a nested structure.

## Contributing

See [CONTRIBUTING.md](https://github.com/aomader/TemplateDsl/blob/main/CONTRIBUTING.md).

## License

[MIT](https://github.com/aomader/TemplateDsl/blob/main/LICENSE)
