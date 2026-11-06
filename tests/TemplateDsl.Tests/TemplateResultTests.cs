using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Tests;

public sealed partial class Greeting(string name) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [H.P [ "Hi ", name ]];
}

public sealed partial class GreetingFromContext : IHtmlTemplate<TestContext>
{
    public IEnumerable<HtmlElement> Build(TestContext context) => [H.P [ context.Greeting, " ", context.User ]];
}

public sealed partial class RequestPath : IHtmlTemplate<HttpContext>
{
    public IEnumerable<HtmlElement> Build(HttpContext context) => [H.Code [ context.Request.Path.Value ]];
}

public sealed partial class FeedTitle(string title) : FormatSamples.IRssTemplate
{
    public IEnumerable<FormatSamples.RssNode> Build() => [FormatSamples.Rss.Title [ title ]];
}

public sealed partial class GatedPage(Task<string> rest) : IAsyncHtmlTemplate
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken cancellationToken) =>
    [
        H.P [ "first" ],
        H.P [ await rest ]
    ];
}

/// <summary>Renderable, but not content of any format, so it has no content type of its own.</summary>
public sealed class BareRenderable : ITemplateRenderable, ITemplateRenderable<TestContext>;

public class TemplateResultTests : IAsyncLifetime
{
    private readonly TaskCompletionSource<string> _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static readonly HttpClient Client = new();
    private WebApplication _app = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddScoped(_ => new TestContext("Hello", "from services"));

        _app = builder.Build();

        _app.MapGet("/html", () => new Greeting("<you>").ToResult());
        _app.MapGet("/status", () => new Greeting("again").ToResult(statusCode: StatusCodes.Status422UnprocessableEntity));
        _app.MapGet("/content-type", () => new Greeting("text").ToResult(contentType: "text/plain; charset=utf-8"));
        _app.MapGet("/rss", () => new FeedTitle("News").ToResult());
        _app.MapGet("/context-from-services", ([FromServices] TestContext context) => new GreetingFromContext().ToResult(context));
        _app.MapGet("/context-given", () => new GreetingFromContext().ToResult(new TestContext("Hey", "given")));
        _app.MapGet("/http-context", (HttpContext context) => new RequestPath().ToResult(context));
        _app.MapGet("/stream", () => new GatedPage(_gate.Task).ToResult());
        _app.MapGet("/header", (HttpResponse response) =>
        {
            response.Headers.Vary = "HX-Request";
            return new Greeting("header").ToResult();
        });
        _app.MapGet("/union/{id:int}", Results<TemplateResult, NotFound> (int id) =>
            id == 1 ? new Greeting("one").ToResult() : TypedResults.NotFound());

        await _app.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
    }

    private async Task<(int Status, string? ContentType, string Body)> GetAsync(string path)
    {
        using var response = await Client.GetAsync(_app.Urls.First() + path);
        return ((int)response.StatusCode, response.Content.Headers.ContentType?.ToString(), await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/html", 200, "text/html; charset=utf-8", "<p>Hi &lt;you&gt;</p>")]
    [InlineData("/status", 422, "text/html; charset=utf-8", "<p>Hi again</p>")]
    [InlineData("/content-type", 200, "text/plain; charset=utf-8", "<p>Hi text</p>")]
    [InlineData("/rss", 200, "application/rss+xml; charset=utf-8", "<title>News</title>")]
    [InlineData("/context-from-services", 200, "text/html; charset=utf-8", "<p>Hello from services</p>")]
    [InlineData("/context-given", 200, "text/html; charset=utf-8", "<p>Hey given</p>")]
    [InlineData("/http-context", 200, "text/html; charset=utf-8", "<code>/http-context</code>")]
    [InlineData("/union/1", 200, "text/html; charset=utf-8", "<p>Hi one</p>")]
    public async Task An_endpoint_renders_the_template_it_returns(string path, int status, string contentType, string body)
    {
        Assert.Equal((status, contentType, body), await GetAsync(path));
    }

    [Fact]
    public async Task A_union_can_also_be_another_result()
    {
        Assert.Equal(404, (await GetAsync("/union/2")).Status);
    }

    [Fact]
    public async Task Headers_set_before_returning_are_sent()
    {
        using var response = await Client.GetAsync(_app.Urls.First() + "/header");
        Assert.Equal("HX-Request", Assert.Single(response.Headers.Vary));
    }

    [Fact]
    public async Task An_async_template_streams_before_what_it_awaits_is_ready()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var response = await Client.GetAsync(_app.Urls.First() + "/stream", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);

        var first = new byte["<p>first</p>".Length];
        await body.ReadExactlyAsync(first, timeout.Token);
        Assert.Equal("<p>first</p>", Encoding.UTF8.GetString(first));

        _gate.SetResult("second");
        Assert.Equal("<p>second</p>", await new StreamReader(body).ReadToEndAsync(timeout.Token));
    }

    [Fact]
    public async Task A_controller_can_return_it_as_an_action_result()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        IActionResult result = new Greeting("mvc").ToResult(statusCode: 201);
        await result.ExecuteResultAsync(new ActionContext(context, new RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()));

        Assert.Equal(201, context.Response.StatusCode);
        Assert.Equal("text/html; charset=utf-8", context.Response.ContentType);
        Assert.Equal("<p>Hi mvc</p>", Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
    }

    [Fact]
    public async Task A_controller_can_return_one_with_a_context_as_an_action_result()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        IActionResult result = new GreetingFromContext().ToResult(new TestContext("Hi", "mvc"));
        await result.ExecuteResultAsync(new ActionContext(context, new RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()));

        Assert.Equal("<p>Hi mvc</p>", Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
    }

    /// <summary>The same as for the render methods: a template that needs a context can't become a result without one.</summary>
    [Theory]
    [InlineData("new NeedsText().ToResult()", true)]
    [InlineData("new NeedsText().ToResult(statusCode: 201)", true)]
    [InlineData("new NeedsText().ToResult(\"text\")", false)]
    [InlineData("new NeedsHttpContext().ToResult()", true)]
    [InlineData("new NeedsHttpContext().ToResult(new DefaultHttpContext())", false)]
    public void A_result_needs_the_context_of_its_template(string call, bool isError)
    {
        var compilation = CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText($$"""
                using Microsoft.AspNetCore.Http;
                using TemplateDsl;
                using TemplateDsl.AspNetCore;

                public sealed class NeedsText : ITemplateRenderable<string>;
                public sealed class NeedsHttpContext : ITemplateRenderable<HttpContext>;

                public static class Endpoint
                {
                    public static object Handle() => {{call}};
                }
                """)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Assert.Equal(isError, compilation.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void The_result_reports_its_status_and_content_type()
    {
        var result = new FeedTitle("x").ToResult(statusCode: 202);

        Assert.Equal(202, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Equal("application/rss+xml; charset=utf-8", ((IContentTypeHttpResult)result).ContentType);
    }

    [Fact]
    public void A_renderable_that_is_no_content_is_sent_as_plain_text()
    {
        Assert.Equal("text/plain; charset=utf-8", ((IContentTypeHttpResult)new BareRenderable().ToResult()).ContentType);
        Assert.Equal("text/plain; charset=utf-8", ((IContentTypeHttpResult)new BareRenderable().ToResult(new TestContext("Hi", "x"))).ContentType);
    }
}
