using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TemplateDsl.Generators;
using Xunit;

namespace Tests;

public interface IHasGreeting
{
    string Greeting { get; }
}

public sealed record TestContext(string Greeting, string User) : IHasGreeting;

public sealed partial class NarrowChild : IHtmlTemplate<IHasGreeting>
{
    public IEnumerable<HtmlElement> Build(IHasGreeting c) => [H.B [ c.Greeting ]];
}

public sealed partial class PlainChild : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [H.I [ "plain" ]];
}

public sealed partial class Box : IHtmlTemplate, IAcceptsChildren
{
    public IEnumerable<HtmlElement> Build() => [H.Div [ Children ]];
}

public sealed partial class ContextBox : IHtmlTemplate<TestContext>, IAcceptsChildren
{
    public IEnumerable<HtmlElement> Build(TestContext context) => [H.Section.Id(context.User) [ Children ]];
}

public sealed partial class ContextPage(string[] items) : IHtmlTemplate<TestContext>
{
    public IEnumerable<HtmlElement> Build(TestContext ctx)
    {
        var upper = ctx.User.ToUpperInvariant();

        return
        [
            H.P.Class(ctx.User) [ upper, " & ", ctx.Greeting ],
            new NarrowChild(),
            new PlainChild(),
            H.Each(items, item => H.Span [ ctx.User, item ]),
            H.U [ ctx.User == "ann" ? new NarrowChild() : null ],
            new ContextBox() [ new NarrowChild() ],
            new Box() [ ctx.User, new NarrowChild() ]
        ];
    }
}

public sealed partial class AsyncContextChild : IAsyncHtmlTemplate<IHasGreeting>
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(IHasGreeting c, CancellationToken ct) =>
        [H.B [ await Task.FromResult(c.Greeting) ]];
}

public sealed partial class AsyncContextBox : IAsyncHtmlTemplate<TestContext>, IAcceptsChildren
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(TestContext c, CancellationToken ct) =>
        [H.Section.Id(await Task.FromResult(c.User)) [ Children ]];
}

public sealed partial class AsyncContextPage : IAsyncHtmlTemplate<TestContext>
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(TestContext c, CancellationToken cancellationToken) =>
    [
        H.P [ await Task.FromResult(c.User) ],
        await new AsyncContextChild(),
        new NarrowChild(),
        new PlainChild(),
        await new AsyncContextBox() [ await new AsyncContextChild() ]
    ];
}

/// <summary>A contract for layouts, whose implementations and layout share a context.</summary>
public abstract partial class ContextContent : IHtmlTemplate<TestContext>
{
    public abstract string Title { get; }
}

public sealed partial class ContextHome : ContextContent
{
    public override string Title => "home";

    public override IEnumerable<HtmlElement> Build(TestContext c) => [H.P [ c.User ]];
}

public sealed partial class ContextLayout(ContextContent page) : IHtmlTemplate<TestContext>
{
    public IEnumerable<HtmlElement> Build(TestContext c) => [H.H1 [ page.Title, " ", c.Greeting ], page];
}

// A custom format in the same project, where the generated members of its templates can't be seen while translating.
public interface IMiniContextTemplate : ITemplate<MiniNode, TestContext>;

public sealed partial class MiniContextPart : IMiniContextTemplate
{
    public IEnumerable<MiniNode> Build(TestContext c) => [c.User];
}

public sealed partial class MiniContextDoc : IMiniContextTemplate
{
    public IEnumerable<MiniNode> Build(TestContext c) => [Mini.List[c.Greeting, new MiniContextPart(), new MiniPart()]];
}

public class ContextTests
{
    private static readonly TestContext Context = new("hi<", "ann");

    private const string SyncExpected =
        "<p class=\"ann\">ANN &amp; hi&lt;</p><b>hi&lt;</b><i>plain</i><span>annx</span><span>anny</span><u><b>hi&lt;</b></u>"
        + "<section id=\"ann\"><b>hi&lt;</b></section><div>ann<b>hi&lt;</b></div>";

    [Fact]
    public void Nested_templates_and_children_get_the_context_without_passing_it()
    {
        Assert.Equal(SyncExpected, new ContextPage(["x", "y"]).RenderToString(Context));
    }

    [Fact]
    public async Task Streaming_a_sync_template_gives_the_same_output()
    {
        using var stream = new MemoryStream();
        await new ContextPage(["x", "y"]).RenderToAsync(stream, Context);

        Assert.Equal(SyncExpected, System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public async Task Async_templates_get_the_context()
    {
        Assert.Equal(
            "<p>ann</p><b>hi&lt;</b><b>hi&lt;</b><i>plain</i><section id=\"ann\"><b>hi&lt;</b></section>",
            await new AsyncContextPage().RenderToStringAsync(Context));
    }

    [Fact]
    public void A_child_can_ask_for_less_than_the_context()
    {
        Assert.Equal("<b>hi&lt;</b>", new NarrowChild().RenderToString(Context));
    }

    [Fact]
    public void Layouts_can_host_content_with_a_context()
    {
        Assert.Equal("<h1>home hi&lt;</h1><p>ann</p>", new ContextLayout(new ContextHome()).RenderToString(Context));
    }

    [Fact]
    public void Custom_formats_can_have_a_context()
    {
        Assert.Equal("(hi<|ann|part)", new MiniContextDoc().RenderToString(Context));
    }

    [Theory]
    [InlineData("""public sealed partial class T : IHtmlTemplate { public IEnumerable<HtmlElement> Build() => [new NeedsA()]; }""")]
    [InlineData("""public sealed partial class T : IHtmlTemplate { public IEnumerable<HtmlElement> Build() => [H.Div [ new NeedsA() ]]; }""")]
    [InlineData("""public sealed partial class T : IHtmlTemplate<B> { public IEnumerable<HtmlElement> Build(B c) => [new NeedsA()]; }""")]
    [InlineData("""public sealed partial class T : IHtmlTemplate { public IEnumerable<HtmlElement> Build() => [new BoxA() [ "x" ]]; }""")]
    public void A_context_the_parent_does_not_have_is_reported(string source)
    {
        var (diagnostics, _) = Run(source);

        Assert.Contains("TDSL008", diagnostics);
    }

    [Theory]
    [InlineData("""class C { string M() => new NeedsA().RenderToString(); }""")]
    [InlineData("""class C { string M() => new NeedsA().RenderToString(new B()); }""")]
    [InlineData("""class C { string M() => new NeedsA().RenderToString(null!, 1); }""")]
    [InlineData("""public sealed partial class T : IHtmlTemplate<A> { public IEnumerable<HtmlElement> Build() => []; }""")]
    public void Wrong_uses_do_not_compile(string source)
    {
        Assert.NotEmpty(Run(source).Errors);
    }

    [Theory]
    [InlineData("""class C { string M() => new NeedsA().RenderToString(new A()); }""")]
    [InlineData("""class C { string M() => new NeedsA().RenderToString(new SubA()); }""")]
    [InlineData("""public sealed partial class T : IHtmlTemplate<SubA> { public IEnumerable<HtmlElement> Build(SubA _) => [new NeedsA()]; }""")]
    public void Compatible_contexts_compile(string source)
    {
        var (diagnostics, errors) = Run(source);

        Assert.Empty(diagnostics);
        Assert.Empty(errors);
    }

    private static (string[] Diagnostics, string[] Errors) Run(string source)
    {
        var compilation = CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText($$"""
                using System.Collections.Generic;
                using TemplateDsl;
                using TemplateDsl.Html;

                public class A;
                public class SubA : A;
                public class B;
                public sealed partial class NeedsA : IHtmlTemplate<A> { public IEnumerable<HtmlElement> Build(A c) => [H.Div]; }
                public sealed partial class BoxA : IHtmlTemplate<A>, IAcceptsChildren { public IEnumerable<HtmlElement> Build(A c) => [Children]; }
                {{source}}
                """)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        CSharpGeneratorDriver.Create(new TemplateGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        return (
            diagnostics.Select(d => d.Id).ToArray(),
            output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id).ToArray());
    }
}
