using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TemplateDsl.Generators;
using Xunit;

namespace Tests;

public sealed partial class ElementsPage(string url, bool flag) : HtmlTemplate
{
    private static readonly string[] Items = ["x"];

    public override IEnumerable<HtmlElement> Build() =>
    [
        H.Html.Lang("en") [
            H.Head [
                H.Title [ "A & B" ],
                H.Meta.Charset("utf-8"),
                H.Meta.HttpEquiv("refresh").Content("1"),
                H.Link.Rel("stylesheet").Href(url)
            ],
            H.Body [
                H.H1.Class("big") [ "Hi" ],
                H.P [ "One", H.Br, H.Span [ "Two" ] ],
                H.A.Href(url).Target("_blank").Hidden(flag) [ "Go" ],
                H.Img.Src(url).Alt("x<y"),
                H.Object.Data(url),
                H.Label.For("f") [ "Name" ],
                H.Input.Type(InputType.Checkbox).Checked(flag).Disabled().ReadOnly(!flag).Attr("data-a", url),
                H.Ul [ H.Each(Items, x => H.Li [ x ]) ],
                H.Var [ "v" ]
            ]
        ]
    ];
}

public class HtmlElementsTests
{
    private static IEnumerable<PropertyInfo> Factories() =>
        typeof(H).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType.GetCustomAttribute<NodeAttribute>() is not null);

    [Fact]
    public void Elements_render_with_their_attributes()
    {
        Assert.Equal(
            "<html lang=\"en\"><head><title>A &amp; B</title><meta charset=\"utf-8\"><meta http-equiv=\"refresh\" content=\"1\">"
            + "<link rel=\"stylesheet\" href=\"/u?a=1&amp;b=2\"></head><body><h1 class=\"big\">Hi</h1><p>One<br><span>Two</span></p>"
            + "<a href=\"/u?a=1&amp;b=2\" target=\"_blank\" hidden>Go</a><img src=\"/u?a=1&amp;b=2\" alt=\"x&lt;y\">"
            + "<object data=\"/u?a=1&amp;b=2\"></object><label for=\"f\">Name</label>"
            + "<input type=\"checkbox\" checked disabled data-a=\"/u?a=1&amp;b=2\"><ul><li>x</li></ul><var>v</var></body></html>",
            new ElementsPage("/u?a=1&b=2", true).RenderToString());
    }

    [Fact]
    public void The_table_is_fully_available()
    {
        Assert.True(Factories().Count() > 100, $"Only {Factories().Count()} elements");
    }

    [Fact]
    public void Every_factory_matches_its_element()
    {
        foreach (var factory in Factories())
        {
            var node = factory.PropertyType.GetCustomAttribute<NodeAttribute>()!;

            // A variant such as InputEmail is the input element with its type already written.
            var variant = factory.PropertyType.Name.EndsWith("Input", StringComparison.Ordinal) && factory.Name != "Input" ? factory.PropertyType.Name[..^"Input".Length] : null;
            var expected = variant is null
                ? "<" + factory.Name.ToLowerInvariant()
                : $"<input type=\"{string.Concat(variant.Select((c, i) => i > 0 && char.IsUpper(c) ? "-" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()))}\"";

            Assert.Equal(expected, node.Start);
            Assert.Equal(">", node.Body);
            Assert.Equal(node.IsVoid ? "" : $"</{factory.Name.ToLowerInvariant()}>", node.End);
            Assert.Equal(!node.IsVoid, factory.PropertyType.GetProperty("Item") is not null);
        }
    }

    [Fact]
    public void Every_element_compiles_and_renders_in_a_template()
    {
        var factories = Factories().ToArray();
        string Call(PropertyInfo f) => "H." + f.Name;

        var source = $$"""
            using System.Collections.Generic;
            using TemplateDsl;
            using TemplateDsl.Html;

            public sealed partial class T : HtmlTemplate
            {
                public override IEnumerable<HtmlElement> Build() => [{{string.Join(", ", factories.Select(Call))}}];
            }
            """;

        var compilation = CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText(source)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        CSharpGeneratorDriver.Create(new TemplateGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        var expected = string.Concat(factories.Select(f =>
        {
            var node = f.PropertyType.GetCustomAttribute<NodeAttribute>()!;
            return node.Start + node.Body + node.End;
        }));

        Assert.Contains(SymbolDisplay.FormatLiteral(expected, quote: true), output.SyntaxTrees.Last().ToString());
    }

    [Theory]
    [InlineData("""=> [H.Br [ "x" ]];""")]
    [InlineData("""=> [H.Img.Src("a") [ "x" ]];""")]
    [InlineData("""=> [H.Hr [ "x" ]];""")]
    public void Void_elements_can_not_have_children(string build)
    {
        var compilation = CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText($$"""
                using System.Collections.Generic;
                using TemplateDsl;
                using TemplateDsl.Html;
                using TemplateDsl.Html.Elements;

                public sealed partial class T : HtmlTemplate
                {
                    public override IEnumerable<HtmlElement> Build() {{build}}
                }
                """)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var result = CSharpGeneratorDriver.Create(new TemplateGenerator()).RunGenerators(compilation).GetRunResult();

        Assert.Equal("TDSL004", Assert.Single(result.Diagnostics).Id);
    }
}
