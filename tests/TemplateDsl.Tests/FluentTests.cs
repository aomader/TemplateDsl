using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TemplateDsl.Generators;
using Xunit;

namespace Tests;

public sealed partial class FluentPage(string id, bool done) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        H.Div.Class("box").Id(id).Attr("data-id", id) [
            H.InputCheckbox.Checked(done).Disabled(),
            H.P [ "One", H.Br, "Two" ],
            done ? H.Span [ "yes" ] : "no"
        ]
    ];
}

public class FluentTests
{
    [Theory]
    [InlineData(true, "<input type=\"checkbox\" checked disabled><p>One<br>Two</p><span>yes</span>")]
    [InlineData(false, "<input type=\"checkbox\" disabled><p>One<br>Two</p>no")]
    public void Attributes_follow_the_source_and_children_the_brackets(bool done, string content)
    {
        Assert.Equal(
            $"<div class=\"box\" id=\"a&amp;b\" data-id=\"a&amp;b\">{content}</div>",
            new FluentPage("a&b", done).RenderToString());
    }

    [Theory]
    [InlineData("""=> [H.Div.Id("a").Id("b")];""")]
    [InlineData("""=> [H.Div.Id("a").Attr("ID", "b")];""")]
    [InlineData("""=> [H.Div.Nope("a")];""")]
    [InlineData("""=> [H.Div.Id()];""")]
    [InlineData("""=> [H.Div.Id("a", "b")];""")]
    [InlineData("""=> [H.Div ["a"] ["b"]];""")]
    [InlineData("""=> [H.Div ["a"].Id("b")];""")]
    public void Wrong_chains_are_reported_or_do_not_compile(string build)
    {
        var compilation = CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText($$"""
                using System.Collections.Generic;
                using TemplateDsl;
                using TemplateDsl.Html;

                public sealed partial class T : IHtmlTemplate
                {
                    public IEnumerable<HtmlElement> Build() {{build}}
                }
                """)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        CSharpGeneratorDriver.Create(new TemplateGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        Assert.True(
            diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)
            || output.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void Lowercase_parameters_do_not_clash_with_elements()
    {
        Assert.Equal("<div id=\"x\"><p>a</p></div>", new ClashPage("x", "a").RenderToString());
    }
}

public sealed partial class ClashPage(string id, string p) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [H.Div.Id(id) [ H.P [ p ] ]];
}
