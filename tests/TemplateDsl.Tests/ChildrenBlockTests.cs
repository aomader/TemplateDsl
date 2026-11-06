using System.Buffers;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TemplateDsl.Generators;
using Xunit;

namespace Tests;

/// <summary>A struct that takes children, so that rendering it allocates nothing either.</summary>
public readonly partial record struct BlockCard(string Title) : IHtmlTemplate, IAcceptsChildren
{
    public IEnumerable<HtmlElement> Build() => [H.Section [ H.H2 [ Title ], Children ]];
}

/// <summary>Passes its own children on, into a card.</summary>
public readonly partial record struct BlockFrame(string Title) : IHtmlTemplate, IAcceptsChildren
{
    public IEnumerable<HtmlElement> Build() => [H.Div.Class("frame") [ new BlockCard(Title) [ Children, "!" ] ]];
}

/// <summary>
/// Gives children that use its members, its primary constructor and a loop variable. A struct loops with foreach, because
/// C# doesn't let a lambda, such as the one of Each, use the members of a struct.
/// </summary>
public partial struct BlockPanel(string title, string[] items) : IHtmlTemplate
{
    public int Count { get; init; }

    public IEnumerable<HtmlElement> Build()
    {
        foreach (var item in items)
        {
            yield return new BlockCard(item) [ H.H3 [ title ], Count, " ", item ];
        }
    }
}

public sealed partial class BlockFrameWithInner(bool withInner) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [withInner ? new BlockFrame("x") [ "inner" ] : new BlockFrame("x")];
}

/// <summary>Children inside children, each using a variable of the loop around it.</summary>
public sealed partial class BlockNested(string[][] groups) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build()
    {
        foreach (var group in groups)
        {
            var size = group.Length;

            yield return new BlockCard($"{size}") [
                H.Each(group, item => new BlockFrame(item) [ item, "/", size ])
            ];
        }
    }
}

public sealed partial class BlockWithContext : IHtmlTemplate<TestContext>
{
    public IEnumerable<HtmlElement> Build(TestContext context) =>
        [new BlockCard(context.Greeting) [ context.User, new ShapeContextPart() ]];
}

public sealed partial class BlockAsync(string[] items) : IAsyncHtmlTemplate
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken cancellationToken) =>
    [
        H.Each(items, item => new BlockCard(item) [ item ]),
        new BlockFrame("late") [ await Task.FromResult("waited") ]
    ];
}

public class ChildrenBlockTests
{
    private static string Render(IHtmlTemplate template)
    {
        var writer = new ArrayBufferWriter<byte>();
        template.RenderTo(writer);
        return Encoding.UTF8.GetString(writer.WrittenSpan);
    }

    [Fact]
    public void A_struct_gives_children_that_use_its_members_and_a_loop_variable()
    {
        Assert.Equal(
            "<section><h2>a</h2><h3>T</h3>3 a</section><section><h2>b</h2><h3>T</h3>3 b</section>",
            Render(new BlockPanel("T", ["a", "b"]) { Count = 3 }));
    }

    [Fact]
    public void A_struct_passes_its_children_on()
    {
        Assert.Equal("<div class=\"frame\"><section><h2>x</h2>inner!</section></div>", Render(new BlockFrameWithInner(true)));
        Assert.Equal("<div class=\"frame\"><section><h2>x</h2>!</section></div>", Render(new BlockFrameWithInner(false)));
    }

    [Fact]
    public void Children_inside_children_get_the_variables_of_the_loops_around_them()
    {
        Assert.Equal(
            "<section><h2>2</h2>" +
            "<div class=\"frame\"><section><h2>a</h2>a/2!</section></div>" +
            "<div class=\"frame\"><section><h2>b</h2>b/2!</section></div>" +
            "</section>" +
            "<section><h2>1</h2><div class=\"frame\"><section><h2>c</h2>c/1!</section></div></section>",
            Render(new BlockNested([["a", "b"], ["c"]])));
    }

    [Fact]
    public void Children_get_the_context()
    {
        Assert.Equal(
            "<section><h2>Hi</h2>Ann<b>Ann</b></section>",
            new BlockWithContext().RenderToString(new TestContext("Hi", "Ann")));
    }

    [Fact]
    public async Task Children_in_an_async_template_can_wait()
    {
        Assert.Equal(
            "<section><h2>a</h2>a</section><section><h2>b</h2>b</section>" +
            "<div class=\"frame\"><section><h2>late</h2>waited!</section></div>",
            await new BlockAsync(["a", "b"]).RenderToStringAsync());
    }

    [Fact]
    public void Children_are_written_without_allocating()
    {
        var panel = new BlockPanel("T", ["a", "b", "c"]) { Count = 3 };
        var writer = new ArrayBufferWriter<byte>(4096);

        // Once first, so that what is allocated once, such as the compiled code, doesn't count.
        panel.RenderTo(writer);
        writer.Clear();

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < 100; i++)
        {
            panel.RenderTo(writer);
            writer.Clear();
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Theory]
    [InlineData("{ var count = 0; yield return new Card() [ count++ ]; }", "'count' is assigned in the children of a template")]
    [InlineData("{ var pair = new { A = 1 }; yield return new Card() [ pair.A ]; }", "'pair' is used in the children of a template")]
    public void What_children_can_only_get_a_copy_of_is_reported(string build, string message)
    {
        var compilation = CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText($$"""
                using System.Collections.Generic;
                using TemplateDsl;
                using TemplateDsl.Html;

                public sealed partial class Card : IHtmlTemplate, IAcceptsChildren { public IEnumerable<HtmlElement> Build() => [H.Div [ Children ]]; }
                public sealed partial class T : IHtmlTemplate { public IEnumerable<HtmlElement> Build() {{build}} }
                """)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        CSharpGeneratorDriver.Create(new TemplateGenerator()).RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("TDSL004", diagnostic.Id);
        Assert.Contains(message, diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }
}
