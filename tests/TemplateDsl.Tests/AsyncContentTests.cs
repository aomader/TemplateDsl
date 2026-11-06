using System.IO.Pipelines;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TemplateDsl.Generators;
using Xunit;

namespace Tests;

/// <summary>Hosts content that may be sync or async, and may need the context, less of it, or none.</summary>
public sealed partial class AsyncContentLayout(IAsyncHtmlContent<TestContext> content) : IAsyncHtmlTemplate<TestContext>
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(TestContext context, CancellationToken cancellationToken) =>
    [
        H.P [ "before" ],
        H.Main [ await content ],
        H.P [ "after" ]
    ];
}

/// <summary>The same, with the content as a type parameter.</summary>
public sealed partial class AsyncGenericContentLayout<TContent>(TContent content) : IAsyncHtmlTemplate<TestContext>
    where TContent : IAsyncHtmlContent<TestContext>
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(TestContext context, CancellationToken cancellationToken) =>
        [H.Main [ await content ]];
}

/// <summary>Content without a context, in a layout without one.</summary>
public sealed partial class AsyncPlainContentLayout(IAsyncHtmlContent? content) : IAsyncHtmlTemplate
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken cancellationToken) => [H.Main [ await content! ]];
}

public sealed partial class AsyncUserPart : IAsyncHtmlTemplate<IHasGreeting>
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(IHasGreeting context, CancellationToken cancellationToken) =>
        [H.Em [ await Task.FromResult(context.Greeting) ]];
}

public class AsyncContentTests
{
    private static readonly TestContext Context = new("Hello", "Ann");

    [Fact]
    public async Task Async_content_takes_sync_and_async_templates_with_any_context_that_fits()
    {
        Assert.Equal("<p>before</p><main><span>extra</span></main><p>after</p>", await new AsyncContentLayout(new SmallPart()).RenderToStringAsync(Context));
        Assert.Equal("<p>before</p><main><b>Ann</b></main><p>after</p>", await new AsyncContentLayout(new ShapeContextPart()).RenderToStringAsync(Context));
        Assert.Equal("<p>before</p><main><i>Hello</i></main><p>after</p>", await new AsyncContentLayout(new ShapeGreetingPart()).RenderToStringAsync(Context));
        Assert.Equal("<p>before</p><main><p>a</p></main><p>after</p>", await new AsyncContentLayout(new AsyncContent(Task.FromResult("a"))).RenderToStringAsync(Context));
        Assert.Equal("<p>before</p><main><em>Hello</em></main><p>after</p>", await new AsyncContentLayout(new AsyncUserPart()).RenderToStringAsync(Context));
    }

    [Fact]
    public async Task Async_content_through_a_type_parameter_also_takes_structs()
    {
        Assert.Equal("<main><li>a 1</li></main>", await new AsyncGenericContentLayout<ShapeRow>(new ShapeRow("a", 1)).RenderToStringAsync(Context));
        Assert.Equal("<main><em>Hello</em></main>", await new AsyncGenericContentLayout<AsyncUserPart>(new AsyncUserPart()).RenderToStringAsync(Context));
    }

    [Fact]
    public async Task Async_content_without_a_context_renders_in_a_layout_without_one()
    {
        Assert.Equal("<main><span>extra</span></main>", await new AsyncPlainContentLayout(new SmallPart()).RenderToStringAsync());
        Assert.Equal("<main><p>b</p></main>", await new AsyncPlainContentLayout(new AsyncContent(Task.FromResult("b"))).RenderToStringAsync());
        Assert.Equal("<main></main>", await new AsyncPlainContentLayout(null).RenderToStringAsync());
    }

    [Fact]
    public async Task Async_content_streams_what_comes_before_it_while_it_waits()
    {
        var value = new TaskCompletionSource<string>();
        var pipe = new Pipe();
        var render = new AsyncContentLayout(new AsyncContent(value.Task)).RenderToAsync(pipe.Writer, Context).AsTask();

        var first = await pipe.Reader.ReadAsync();
        Assert.Equal("<p>before</p><main><p>", Encoding.UTF8.GetString(first.Buffer));
        pipe.Reader.AdvanceTo(first.Buffer.End);

        value.SetResult("late");
        await render;
        await pipe.Writer.CompleteAsync();

        var rest = await pipe.Reader.ReadAsync();
        Assert.Equal("late</p></main><p>after</p>", Encoding.UTF8.GetString(rest.Buffer));
    }

    [Theory]
    [InlineData("public static class Use { public static object Make() => new Layout(new NeedsB()); }")]
    [InlineData("public sealed partial class SyncPage(IAsyncHtmlContent content) : IHtmlTemplate { public IEnumerable<HtmlElement> Build() => [H.Main [ content ]]; }")]
    public void Content_that_doesnt_fit_does_not_compile(string source)
    {
        var compilation = CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText($$"""
                using System.Collections.Generic;
                using System.Threading;
                using System.Threading.Tasks;
                using TemplateDsl;
                using TemplateDsl.Html;

                public sealed record A;
                public sealed record B;
                public sealed partial class NeedsB : IAsyncHtmlTemplate<B> { public async Task<IEnumerable<HtmlElement>> BuildAsync(B b, CancellationToken ct) => []; }
                public sealed partial class Layout(IAsyncHtmlContent<A> content) : IAsyncHtmlTemplate<A> { public async Task<IEnumerable<HtmlElement>> BuildAsync(A a, CancellationToken ct) => [await content]; }
                {{source}}
                """)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        CSharpGeneratorDriver.Create(new TemplateGenerator()).RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        Assert.NotEmpty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }
}
