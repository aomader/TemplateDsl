using System.Text;
using Microsoft.AspNetCore.Http;
using TemplateDsl.Html;

// What trimming or native code could break: struct templates with children, a context, content of interface types,
// an async template awaited as content, and a web result, whose content type the generator writes into the template.
var context = new PageContext("Ann");
var output = new StringBuilder();

output.Append(new Layout(new Page()).RenderToString(context));
output.Append(await new AsyncLayout(new Slow()).RenderToStringAsync());

var http = new DefaultHttpContext();
http.Response.Body = new MemoryStream();
await new Layout(new Page()).ToResult(context).ExecuteAsync(http);
output.Append(' ').Append(http.Response.ContentType).Append(' ').Append(Encoding.UTF8.GetString(((MemoryStream)http.Response.Body).ToArray()));

Console.Write(output);

public sealed record PageContext(string User);

public readonly partial record struct Card(string Title) : IHtmlTemplate, IAcceptsChildren
{
    public IEnumerable<HtmlElement> Build() => [H.Section [ H.H2 [ Title ], Children ]];
}

public sealed partial class Greeting : IHtmlTemplate<PageContext>
{
    public IEnumerable<HtmlElement> Build(PageContext context) => [H.P [ "Hi ", context.User ]];
}

public sealed partial class Page : IHtmlTemplate<PageContext>
{
    public IEnumerable<HtmlElement> Build(PageContext context) => [new Card("t") [ "body", new Greeting() ]];
}

public sealed partial class Layout(IHtmlContent<PageContext> content) : IHtmlTemplate<PageContext>
{
    public IEnumerable<HtmlElement> Build(PageContext context) => [H.Main [ content ]];
}

public sealed partial class Slow : IAsyncHtmlTemplate
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken cancellationToken) => [H.I [ await Task.FromResult("slow") ]];
}

public sealed partial class AsyncLayout(IAsyncHtmlContent content) : IAsyncHtmlTemplate
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken cancellationToken) => [H.Div [ await content ]];
}
