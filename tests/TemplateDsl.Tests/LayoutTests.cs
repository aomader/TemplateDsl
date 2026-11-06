using System.Buffers;
using System.Text;
using System.Text.Json;
using FormatSamples;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TemplateDsl.Generators;
using Xunit;

namespace Tests;

public abstract partial class LayoutPage : IHtmlTemplate
{
    public abstract string PageTitle { get; }

    public virtual IHtmlTemplate? Extra => null;
}

public sealed partial class HomeContent(string name) : LayoutPage
{
    public override string PageTitle => $"Hello {name}";

    public override IHtmlTemplate? Extra => new HomeExtra();

    public override IEnumerable<HtmlElement> Build() => [H.H1 [ $"Welcome {name}" ]];
}

public sealed partial class HomeExtra : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [H.Meta.Name("a").Content("b")];
}

public sealed partial class AboutContent : LayoutPage
{
    public override string PageTitle => "About";

    public override IEnumerable<HtmlElement> Build() => [H.P [ "About" ]];
}

public sealed partial class LayoutA(LayoutPage page) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        H.Html [ H.Head [ H.Title [ page.PageTitle ], page.Extra ], H.Body [ H.Main [ page ] ] ]
    ];
}

public sealed partial class LayoutB(LayoutPage page) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [H.Div.Class("b") [ page ]];
}

public sealed partial class AsyncContent(Task<string> value) : IAsyncHtmlTemplate
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) => [H.P [ await value ]];
}

public sealed partial class AsyncLayout(IAsyncHtmlTemplate page, IHtmlTemplate? sync) : IAsyncHtmlTemplate
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) =>
    [
        H.Div [ "before", sync, await page, new Panel("in panel") [ await page ], "after" ]
    ];
}


public sealed partial class JsonOne : IJsonTemplate
{
    public IEnumerable<JsonNode> Build() => [Json.Lit ("9")];
}

public class LayoutTests
{
    [Fact]
    public void Layout_renders_a_page_it_only_knows_by_interface()
    {
        Assert.Equal(
            "<html><head><title>Hello ann</title><meta name=\"a\" content=\"b\"></head><body><main><h1>Welcome ann</h1></main></body></html>",
            new LayoutA(new HomeContent("ann")).RenderToString());
    }

    [Fact]
    public void Null_template_renders_nothing()
    {
        Assert.Equal(
            "<html><head><title>About</title></head><body><main><p>About</p></main></body></html>",
            new LayoutA(new AboutContent()).RenderToString());
    }

    [Theory]
    [InlineData(true, "<html><head><title>About</title></head><body><main><p>About</p></main></body></html>")]
    [InlineData(false, "<div class=\"b\"><p>About</p></div>")]
    public void Layout_can_be_chosen_at_runtime_and_rendered_through_the_interface(bool first, string expected)
    {
        LayoutPage page = new AboutContent();
        IHtmlTemplate view = first ? new LayoutA(page) : new LayoutB(page);

        Assert.Equal(expected, view.RenderToString());

        var writer = new ArrayBufferWriter<byte>();
        view.RenderTo(writer);
        Assert.Equal(expected, Encoding.UTF8.GetString(writer.WrittenSpan));
    }

    [Fact]
    public async Task Sync_and_async_views_render_to_streams_through_the_common_interface()
    {
        IAsyncHtmlContent[] views = [new LayoutB(new AboutContent()), new AsyncContent(Task.FromResult("v"))];
        string[] expected = ["<div class=\"b\"><p>About</p></div>", "<p>v</p>"];

        for (var i = 0; i < views.Length; i++)
        {
            using var stream = new MemoryStream();
            await views[i].RenderToAsync(stream);
            Assert.Equal(expected[i], Encoding.UTF8.GetString(stream.ToArray()));
        }
    }

    [Fact]
    public async Task Async_view_renders_to_a_string_through_the_interface()
    {
        IAsyncHtmlTemplate view = new AsyncContent(Task.FromResult("v"));

        Assert.Equal("<p>v</p>", await view.RenderToStringAsync());
    }

    [Fact]
    public async Task Async_layout_hosts_sync_and_async_templates_also_inside_children()
    {
        var output = await new AsyncLayout(new AsyncContent(Task.FromResult("a")), new AboutContent()).RenderToStringAsync();

        Assert.Equal(
            "<div>before<p>About</p><p>a</p><div class=\"panel\"><div class=\"title\">in panel</div><div class=\"body\"><p>a</p></div></div>after</div>",
            output);
    }

    [Fact]
    public async Task Output_before_an_async_page_is_sent_first()
    {
        var value = new TaskCompletionSource<string>();
        var pipe = new System.IO.Pipelines.Pipe();

        var render = new AsyncLayout(new AsyncContent(value.Task), null).RenderToAsync(pipe.Writer).AsTask();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await pipe.Reader.ReadAsync(timeout.Token);
        Assert.Equal("<div>before<p>", Encoding.UTF8.GetString(result.Buffer.ToArray()));
        pipe.Reader.AdvanceTo(result.Buffer.End);

        value.SetResult("a");
        await render;
    }

    [Theory]
    [InlineData(true, true, "[1,9,9,2]")]
    [InlineData(true, false, "[1,9,2]")]
    [InlineData(false, false, "[1,2]")]
    public void Other_formats_work_and_null_leaves_no_separator(bool first, bool second, string expected)
    {
        var output = new JsonLayoutPage(first ? new JsonOne() : null, second ? new JsonOne() : null).RenderToString();

        Assert.Equal(expected, output);
        JsonDocument.Parse(output).Dispose();
    }

    private const string Header = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using TemplateDsl;
        using TemplateDsl.Html;
        using FormatSamples;

        """;

    private static (string[] Diagnostics, string[] Errors) Run(string source)
    {
        var compilation = CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText(Header + source)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        CSharpGeneratorDriver.Create(new TemplateGenerator()).RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        return (
            diagnostics.Select(d => d.Id).ToArray(),
            output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id).ToArray());
    }

    [Fact]
    public void Async_template_in_a_sync_layout_is_rejected_by_csharp()
    {
        var (diagnostics, errors) = Run("""
            public sealed partial class T(IAsyncHtmlTemplate page) : IHtmlTemplate
            {
                public IEnumerable<HtmlElement> Build() => [page];
            }
            """);

        Assert.Empty(diagnostics);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Template_of_another_format_does_not_compile()
    {
        var (_, errors) = Run("""
            public sealed partial class T(JsonNode page) : IHtmlTemplate
            {
                public IEnumerable<HtmlElement> Build() => [page];
            }
            """);

        Assert.NotEmpty(errors);
    }

}

public sealed partial class JsonLayoutPage(IJsonTemplate? first, IJsonTemplate? second) : IJsonTemplate
{
    public IEnumerable<JsonNode> Build() =>
    [
        Json.Arr [ Json.Lit ("1"), first, second, Json.Lit ("2") ]
    ];
}

public sealed partial class AwaitedInterfaceLayout(IAsyncHtmlTemplate? page) : IAsyncHtmlTemplate
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) => [H.Div [ "a", await page!, "b" ]];
}

public class AwaitedTemplateTests
{
    [Fact]
    public async Task Template_behind_an_interface_can_be_awaited_directly()
    {
        var output = await new AwaitedInterfaceLayout(new AsyncContent(Task.FromResult("v"))).RenderToStringAsync();

        Assert.Equal("<div>a<p>v</p>b</div>", output);
    }

    [Fact]
    public async Task Null_template_behind_an_interface_renders_nothing()
    {
        Assert.Equal("<div>ab</div>", await new AwaitedInterfaceLayout(null).RenderToStringAsync());
    }

    [Fact]
    public void Generated_code_has_no_awaiter()
    {
        Assert.Null(typeof(AsyncContent).GetMethod("GetAwaiter"));
    }
}
