![](https://raw.githubusercontent.com/aomader/TemplateDsl/main/assets/icon.png)
# TemplateDsl

TemplateDsl lets you write (HTML) templates as plain C# classes for which its source generator adds efficient render methods that write UTF-8 straight to a stream. It checks everything at compile time due to its [embedded DSL](https://en.wikipedia.org/wiki/Domain-specific_language) builders, so typos, wrong values and missing context fail the build, and nothing is parsed or looked up at runtime. Templates are composed like any other code, stream when they are async, and come with ASP.NET Core support.

```csharp
public sealed record PageContext(string UserName);   // what the whole page shares

public sealed partial class TodoItem(Todo todo) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() =>
    [
        H.Li.Class(todo.Done ? "done" : null) [
            H.InputCheckbox.Checked(todo.Done),
            todo.Title
        ]
    ];
}

public sealed partial class TodoList(IReadOnlyList<Todo> todos) : HtmlTemplate<PageContext>
{
    public override IEnumerable<HtmlElement> Build(PageContext context) =>
    [
        H.H1 [ $"{context.UserName}'s todos" ],
        H.Ul [ H.Each(todos, todo => new TodoItem(todo)) ]
    ];
}

builder.Services.AddScoped(services => new PageContext("Ann"));   // per request, such as the signed-in user

app.MapGet("/", (TodoStore store, [FromServices] PageContext context) =>
    new TodoList(store.All()).ToResult(context));
```

A template gets its own data through its constructor. What the whole page shares, such as the user, is its context,
which nested templates can ask for too, without passing it on. `TodoItem` needs none. With two todos, one of them
done, the page is:

```html
<h1>Ann's todos</h1><ul><li class="done"><input type="checkbox" checked>Write &lt;docs&gt;</li><li><input type="checkbox">Ship</li></ul>
```

## Why?

- **Mistakes fail the build, not a request.** Elements and attributes are typed methods: a misspelled one doesn't
  compile, `.ColSpan(2)` takes an `int`, and `H.InputCheckbox.Min(3)` is an error because a checkbox has no `min`.
  A template that needs a context can't be rendered without one, and output is escaped unless you ask for `H.Raw`.
- **It's just C#.** Autocomplete lists the elements after `H.` and the attributes of each. Templates are classes
  that get their data through constructors, so refactoring and find-references work as everywhere else. There is
  no separate file type, view lookup or registration.
- **Fast, with very few allocations.** The generated methods write UTF-8 without building a tree or intermediate
  strings. Rendering a page to a stream allocates under 1 KB, and async templates stream as they render.
  See [how it compares to Razor](#performance-compared-to-razor).
- **Readable and explicit.** Pages are made of small components, layouts are chosen from outside, and the logic in
  a template is plain `if`, `switch` and loops. Loading data stays in endpoints and services.

TemplateDsl renders on the server. Client-side interactivity is not a goal, but it works well with htmx, as the
[todo sample](https://github.com/aomader/TemplateDsl/tree/main/samples/TemplateDsl.TodoSample) shows.

## Getting started

```sh
dotnet add package TemplateDsl.Html --prerelease          # HTML templates; brings TemplateDsl and its generator along
dotnet add package TemplateDsl.AspNetCore --prerelease    # to return templates from endpoints and controllers
```

It needs .NET 10. All versions are previews so far, which `--prerelease` installs. Until 1.0, the API can still change
between minor versions. Each breaking change is listed in the
[changelog](https://github.com/aomader/TemplateDsl/blob/main/CHANGELOG.md), with how to migrate.

A template is a `sealed partial` class deriving from `HtmlTemplate`, or from `HtmlTemplate<TContext>` if it needs a
context, whose `Build` method returns its output. Render it to a string, write it to a response, or return it from an
endpoint:

```csharp
var html = new TodoList(todos).RenderToString(pageContext);
await new TodoList(todos).RenderToAsync(httpContext.Response.BodyWriter, pageContext, httpContext.RequestAborted);
app.MapGet("/", (TodoStore store, [FromServices] PageContext context) => new TodoList(store.All()).ToResult(context));
```

The render methods are extension methods in the `TemplateDsl` namespace, and `ToResult` is in
`TemplateDsl.AspNetCore`. The NuGet packages import both automatically. With project references instead of the
packages, add the `using`s yourself.

The packages:

- **`TemplateDsl`**: the core and the source generator, for any nested text format.
- **`TemplateDsl.Html`**: the HTML format.
- **`TemplateDsl.AspNetCore`**: templates as results of minimal API endpoints and controller actions.

The packages are released together with one version, so use the same version for all of them.

## Templates and elements

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
doctype is written with `H.Raw("<!DOCTYPE html>")`. The elements are generated from
`src/TemplateDsl.Html/elements.txt` by `dotnet run tools/GenerateHtmlElements.cs`.

Nodes and children brackets can only be used inside `Build`, so that everything is checked and compiled. Templates
must be `sealed partial` classes, or abstract classes without a `Build` method. What the generator can't compile is a
build error with its own ID, each explained in [docs/diagnostics](https://github.com/aomader/TemplateDsl/blob/main/docs/diagnostics/README.md).

## Attributes and values

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

A string is never reformatted, so `.Value(price.ToString("C"))` and `string.Create(culture, $"...")` are the way to
choose yourself.

## Logic in Build

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

## Components and layouts

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

## Context

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

## Async and streaming

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

## ASP.NET Core

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

## Security

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

## Performance compared to Razor

Razor mixes C# into markup, and TemplateDsl writes markup as C#. That makes templates ordinary classes: their
parameters are constructor parameters, a misspelled attribute or a missing context is a compile error, and refactoring,
find-references and autocomplete work as in any other code. Razor fits better for an app that already uses it, for tag
helpers and view components, or when the markup is edited by people who don't write C#.

Rendering speed is close to Razor Slices, which also compiles to plain render methods, and well ahead of Razor Pages
and Blazor's `HtmlRenderer`. All outputs are checked to be the same bytes before measuring:

| Benchmark | TemplateDsl | Razor Slices | Razor Pages | Blazor |
|-----------|------------:|-------------:|------------:|-------:|
| Button to string | 123 ns, 144 B | 160 ns, 328 B | | 1,988 ns, 3,792 B |
| Page to string | 1.70 µs, 3.7 KB | 2.78 µs, 6.7 KB | | 19.1 µs, 21.4 KB |
| Page to stream | 1.87 µs, 0.9 KB | 5.28 µs, 3.9 KB | | 19.2 µs, 18.8 KB |
| Whole request with a layout | 79 µs, 11.6 KB | 92 µs, 16.4 KB | 189 µs, 79.0 KB | |

Measured with BenchmarkDotNet on .NET 10 on an AMD Ryzen 7 3700X under Linux. The request benchmark runs in an
in-process test server and varies by about 15%, so there TemplateDsl and Razor Slices are roughly even. To run them
yourself: `dotnet run -c Release --project samples/TemplateDsl.Benchmarks -- --filter '*'`.

## Samples

- [`samples/TemplateDsl.TodoSample`](https://github.com/aomader/TemplateDsl/tree/main/samples/TemplateDsl.TodoSample): a classic todo list with ASP.NET Core minimal
  APIs and htmx 4, built from small components. Every action swaps the app in place without a page reload, and
  editing a todo inline swaps only its row.
- [`samples/TemplateDsl.WebSample`](https://github.com/aomader/TemplateDsl/tree/main/samples/TemplateDsl.WebSample): streaming with layouts and async components.
  Start it with `dotnet run --project samples/TemplateDsl.WebSample` and open the printed address.
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
