using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using TemplateDsl.Generators;
using Xunit;

namespace Tests;

public class UsageRuleTests
{
    private const string Header = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using TemplateDsl;
        using TemplateDsl.Html;
        using TemplateDsl.Html.Elements;

        public sealed partial class Box : IHtmlTemplate, IAcceptsChildren
        {
            public IEnumerable<HtmlElement> Build() => [H.Div [ Children ]];
        }

        public sealed partial class Leaf : IHtmlTemplate
        {
            public IEnumerable<HtmlElement> Build() => [H.Div];
        }

        """;

    private static async Task<string[]> AnalyzeAsync(string source)
    {
        var compilation = CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText(Header + source)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var diagnostics = await compilation
            .WithAnalyzers([new TemplateUsageAnalyzer()])
            .GetAnalyzerDiagnosticsAsync();

        return diagnostics.Select(d => d.Id).ToArray();
    }

    [Theory]
    [InlineData("""class C { object M() => H.Div; }""")]
    [InlineData("""class C { object M() => H.Div.Id("x"); }""")]
    [InlineData("""class C { object M() => H.Div.Id("x") [ "y" ]; }""")]
    [InlineData("""class C { object M() => H.Text("x"); }""")]
    [InlineData("""class C { object M() => H.Each(new[] { 1 }, i => "x"); }""")]
    [InlineData("""class C { object M() => new Div { Id = "x" }; }""")]
    [InlineData("""class C { object M() => new Text(); }""")]
    [InlineData("""class C { Div M() => new(); }""")]
    [InlineData("""class C { object M(Box box) => box ["x"]; }""")]
    [InlineData("""class C { object M() => new Box() ["x"]; }""")]
    [InlineData("""class C { object F = H.Div; }""")]
    [InlineData("""class C { object P => H.Div; }""")]
    [InlineData("""class C { object Build() => H.Div; }""")]
    [InlineData("""public sealed partial class T : IHtmlTemplate { public IEnumerable<HtmlElement> Build() => []; object Helper() => H.Div; }""")]
    [InlineData("""public sealed partial class T : IHtmlTemplate { object F = H.Div; public IEnumerable<HtmlElement> Build() => []; }""")]
    public async Task Nodes_outside_build_are_reported(string source)
    {
        Assert.Contains("TDSL007", await AnalyzeAsync(source));
    }

    [Theory]
    [InlineData("""public sealed partial class T : IHtmlTemplate { public IEnumerable<HtmlElement> Build() => [H.Div [ "x" ], new Box() [ "y" ], H.Div.Id("z")]; }""")]
    [InlineData("""public sealed partial class T : IHtmlTemplate { IEnumerable<HtmlElement> HtmlTemplate.Build() => [H.Div]; }""")]
    [InlineData("""public sealed partial class T(string[] items) : IHtmlTemplate { public IEnumerable<HtmlElement> Build() => [H.Each(items, i => H.Div [ i ])]; }""")]
    [InlineData("""public sealed partial class T : IAsyncHtmlTemplate { public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) => [H.Div [ await Task.FromResult("x") ]]; }""")]
    [InlineData("""class C { string M() => new Leaf().RenderToString(); }""")]
    [InlineData("""class C { Box M() => new Box(); }""")]
    [InlineData("""class C { int M(int[] a) => a[0]; }""")]
    [InlineData("""class C { string M() => new string('x', 1); }""")]
    public async Task Everything_else_is_allowed(string source)
    {
        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    public async Task A_format_can_create_its_own_nodes()
    {
        Assert.Empty(await AnalyzeAsync("""
            [TemplateFormat(typeof(MyText))] public abstract partial class MyNode : TemplateNode;
            [TextNode] public sealed partial class MyText : MyNode { public static MyText Make() => new(); }
            [Node] public sealed partial class MyItem : MyNode;
            [TemplateTags(typeof(MyNode))] public static partial class MyTags { public static MyItem Item() => new(); }
            """));
    }

    /// <summary>With the generated code, which has the conversions of templates to nodes.</summary>
    private static async Task<string[]> AnalyzeGeneratedAsync(string source)
    {
        var compilation = CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText(Header + source)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        CSharpGeneratorDriver.Create(new TemplateGenerator()).RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        var diagnostics = await output.WithAnalyzers([new TemplateUsageAnalyzer()]).GetAnalyzerDiagnosticsAsync();

        return diagnostics.Select(d => d.Id).ToArray();
    }

    [Theory]
    [InlineData("""class C { HtmlElement M() => new Leaf(); }""")]
    [InlineData("""class C { void Take(HtmlElement content) { } void M() => Take(new Leaf()); }""")]
    [InlineData("""public sealed partial class T : IHtmlTemplate { HtmlElement? Part => new Leaf(); public IEnumerable<HtmlElement> Build() => []; }""")]
    public async Task A_template_converted_to_a_node_outside_build_is_reported(string source)
    {
        Assert.Contains("TDSL009", await AnalyzeGeneratedAsync(source));
    }

    [Theory]
    [InlineData("""public sealed partial class T(IHtmlContent content) : IHtmlTemplate { public IEnumerable<HtmlElement> Build() => [new Leaf(), H.Div [ content, new Leaf() ], H.Each(new[] { 1 }, i => new Leaf())]; }""")]
    [InlineData("""class C { IHtmlContent M() => new Leaf(); IHtmlTemplate N() => new Leaf(); }""")]
    public async Task Templates_in_build_and_as_content_are_allowed(string source)
    {
        Assert.DoesNotContain("TDSL009", await AnalyzeGeneratedAsync(source));
    }

    private static string[] RunGenerator(string build)
    {
        var compilation = CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText(Header + "public sealed partial class T(string model) : IHtmlTemplate { public IEnumerable<HtmlElement> Build() " + build + " }")],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return CSharpGeneratorDriver.Create(new TemplateGenerator()).RunGenerators(compilation).GetRunResult().Diagnostics.Select(d => d.Id).ToArray();
    }

    [Theory]
    [InlineData("""=> [H.Div("x")];""")]
    [InlineData("""=> [H.Div(model)];""")]
    [InlineData("""=> [H.Div("a", "b")];""")]
    [InlineData("""=> [H.Div.Id("a")( "b")];""")]
    public void Positional_attributes_are_reported(string build)
    {
        Assert.Contains("TDSL004", RunGenerator(build));
    }

    [Theory]
    [InlineData("""=> [H.Div.Id("a").Class("b")];""")]
    [InlineData("""=> [H.Div.Class("b").Id("a") [ "x" ]];""")]
    [InlineData("""=> [H.Text("x"), H.Raw("y"), H.Text(model)];""")]
    public void Named_attributes_and_text_nodes_are_fine(string build)
    {
        Assert.DoesNotContain("TDSL004", RunGenerator(build));
    }
}
