using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TemplateDsl.Generators;
using Xunit;
using static TemplateDsl.Html.Elements;

namespace Tests;

public sealed partial class ClassPage(bool done, bool editing, string? extra, double size) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        Li.Class("todo", ("done", done), ("editing", editing), extra),
        Div.Class(("done", done), extra, "last"),
        Span.Class(extra, $"size-{size}"),
        P.Class(("a", false), null, "", ("b", true)),
        Em.Class(("a", false))
    ];
}

public sealed partial class TokenListPage(string? error, bool userContent, bool canSubmit) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        Input.AriaDescribedBy("hint", ("error", error is not null)).AriaLabelledBy("label", "unit").AriaControls(("list", userContent)).AriaOwns(("popup", canSubmit)),
        A.Href("/x").Rel("noopener", ("nofollow", userContent), ("ugc", userContent)),
        Iframe.Sandbox("allow-scripts", ("allow-forms", canSubmit))
    ];
}

/// <summary>
/// A class is a list of names, each written unless it is null or empty, or its condition is false, with a space only
/// between those written, and no attribute at all if none is.
/// </summary>
public class ClassTokenTests
{
    [Theory]
    [InlineData(true, true, "x", "<li class=\"todo done editing x\"></li><div class=\"done x last\"></div><span class=\"x size-1.5\"></span>")]
    [InlineData(false, false, null, "<li class=\"todo\"></li><div class=\"last\"></div><span class=\"size-1.5\"></span>")]
    [InlineData(false, true, "", "<li class=\"todo editing\"></li><div class=\"last\"></div><span class=\"size-1.5\"></span>")]
    [InlineData(false, false, "\"<", "<li class=\"todo &quot;&lt;\"></li><div class=\"&quot;&lt; last\"></div><span class=\"&quot;&lt; size-1.5\"></span>")]
    public void Names_are_written_if_they_are_there(bool done, bool editing, string? extra, string expected)
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");

        try
        {
            Assert.Equal(expected + "<p class=\"b\"></p><em></em>", new ClassPage(done, editing, extra, 1.5).RenderToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Theory]
    [InlineData("e", true, true, "<input aria-describedby=\"hint error\" aria-labelledby=\"label unit\" aria-controls=\"list\" aria-owns=\"popup\"><a href=\"/x\" rel=\"noopener nofollow ugc\"></a><iframe sandbox=\"allow-scripts allow-forms\"></iframe>")]
    [InlineData(null, false, false, "<input aria-describedby=\"hint\" aria-labelledby=\"label unit\"><a href=\"/x\" rel=\"noopener\"></a><iframe sandbox=\"allow-scripts\"></iframe>")]
    public void Other_lists_of_names_are_written_the_same_way(string? error, bool userContent, bool canSubmit, string expected) =>
        Assert.Equal(expected, new TokenListPage(error, userContent, canSubmit).RenderToString());

    [Fact]
    public void Constant_names_are_written_while_compiling()
    {
        var (diagnostics, generated) = Translate("""=> [H.P.Class("a", ("b", true), ("c", false), null, "", "d")];""");

        Assert.Empty(diagnostics);
        Assert.Contains("<p class=\\\"a b d\\\"></p>", generated);
        Assert.DoesNotContain("__tokens", generated);
    }

    [Fact]
    public void Only_names_that_may_not_be_written_need_a_flag()
    {
        var (_, constantFirst) = Translate("""=> [H.P.Class("a", ("b", System.DateTime.Now.Second > 1))];""");
        var (_, conditionalFirst) = Translate("""=> [H.P.Class(("b", System.DateTime.Now.Second > 1), "a")];""");

        Assert.DoesNotContain("__tokens", constantFirst);
        Assert.Contains("__tokens", conditionalFirst);
    }

    [Theory]
    [InlineData("""=> [H.Div.Class("a").Class(("b", true))];""", "'Class' appears more than once")]
    [InlineData("""=> [H.Div.Class("a", (Children.Elements.Length.ToString(), true))];""", "Children can only be placed as a child")]
    [InlineData("""=> [H.Div.Class(new AttributeToken[] { "a" })];""", "must be a name, or a pair of a name and whether to write it")]
    [InlineData("""=> [H.Div.Class("a", default(AttributeToken))];""", "'default(AttributeToken)' must be a name, or a pair")]
    public void A_class_is_written_once_from_names(string build, string expected)
    {
        var diagnostic = Assert.Single(Translate(build).Diagnostics);

        Assert.Equal("TDSL004", diagnostic.Id);
        Assert.Contains(expected, diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    private static (System.Collections.Immutable.ImmutableArray<Diagnostic> Diagnostics, string Generated) Translate(string build)
    {
        var compilation = CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText($$"""
                using System.Collections.Generic;
                using TemplateDsl;
                using TemplateDsl.Html;
                using H = TemplateDsl.Html.Elements;

                public sealed partial class T : IHtmlTemplate, IAcceptsChildren
                {
                    public IEnumerable<HtmlElement> Build() {{build}}
                }
                """)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var result = CSharpGeneratorDriver.Create(new TemplateGenerator()).RunGenerators(compilation).GetRunResult();

        return (result.Diagnostics, string.Concat(result.GeneratedTrees.Select(tree => tree.ToString())));
    }
}
