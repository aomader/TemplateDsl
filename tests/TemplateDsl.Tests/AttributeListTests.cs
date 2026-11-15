using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TemplateDsl.Generators;
using Xunit;

namespace Tests;

public sealed partial class AttrsPage(string value, string? optional, bool flag, int number) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        H.Div.Id("a").Attr("data-const", "c&d").Attr("data-value", value).Attr("data-null", null).Attr("data-optional", optional),
        H.Div.Attr("data-hole", $"n-{number}").Attr("data-ternary", flag ? value : null).Attr("aria-label", "x") [ "child" ],
        H.Div.Class("k").Attr("hx-get", "/x"),
        H.Div,
        H.Div
    ];
}

public sealed partial class DataPage(string value, string? optional, int number, DateOnly day, bool flag, bool? maybe) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        H.Li.Data("todo-id", number).Data("state", value).Data("note", optional).Data("due", day).Data("ratio", 1.5)
            .Data("done", flag).Data("selected", maybe).Data("on", true).Data("off", false).Data("const", "c&d") [ "x" ]
    ];
}

public sealed partial class AsyncAttrs(Task<string> value) : IAsyncHtmlTemplate
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) =>
        [H.Div.Attr("data-v", await value) [ "x" ]];
}

public sealed partial class HiddenPage(bool flag) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        H.Div.Hidden(flag).Id("d"),
        H.Div.Hidden(),
        H.Div.Hidden(false),
        H.Div.Class("c").Hidden(!flag),
        H.Div.Hidden(flag)
    ];
}

public class AttributeListTests
{
    private static readonly string Hostile = "\"><script>'&";

    private const string HostileEscaped = "&quot;&gt;&lt;script&gt;&#39;&amp;";

    [Fact]
    public void Constant_and_dynamic_values_are_written_and_null_is_left_out()
    {
        Assert.Equal(
            "<div id=\"a\" data-const=\"c&amp;d\" data-value=\"v\" data-optional=\"o\"></div>"
            + "<div data-hole=\"n-7\" data-ternary=\"v\" aria-label=\"x\">child</div>"
            + "<div class=\"k\" hx-get=\"/x\"></div>"
            + "<div></div>"
            + "<div></div>",
            new AttrsPage("v", "o", true, 7).RenderToString());
    }

    [Fact]
    public void Data_attributes_are_written_with_their_prefix_in_the_invariant_culture()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

        try
        {
            Assert.Equal(
                "<li data-todo-id=\"5\" data-state=\"open\" data-note=\"n\" data-due=\"2026-11-14\" data-ratio=\"1.5\""
                + " data-done=\"true\" data-selected=\"false\" data-on=\"true\" data-off=\"false\" data-const=\"c&amp;d\">x</li>",
                new DataPage("open", "n", 5, new DateOnly(2026, 11, 14), true, false).RenderToString());

            // Null leaves an attribute out, for a string and a bool alike.
            Assert.Equal(
                "<li data-todo-id=\"5\" data-state=\"&quot;&gt;\" data-due=\"2026-11-14\" data-ratio=\"1.5\""
                + " data-done=\"false\" data-on=\"true\" data-off=\"false\" data-const=\"c&amp;d\">x</li>",
                new DataPage("\">", null, 5, new DateOnly(2026, 11, 14), false, null).RenderToString());
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void Null_at_runtime_leaves_the_attribute_out()
    {
        var output = new AttrsPage("v", null, false, 7).RenderToString();

        Assert.DoesNotContain("data-optional", output);
        Assert.DoesNotContain("data-ternary", output);
    }

    [Fact]
    public void Dynamic_values_are_escaped()
    {
        Assert.Contains($"data-value=\"{HostileEscaped}\"", new AttrsPage(Hostile, null, false, 1).RenderToString());
    }

    [Fact]
    public async Task Values_can_be_awaited()
    {
        Assert.Equal("<div data-v=\"a&amp;b\">x</div>", await new AsyncAttrs(Task.FromResult("a&b")).RenderToStringAsync());
    }

    [Fact]
    public void Bool_attributes_render_without_a_value()
    {
        Assert.Equal(
            "<div hidden id=\"d\"></div><div hidden></div><div></div><div class=\"c\"></div><div hidden></div>",
            new HiddenPage(true).RenderToString());

        Assert.Equal(
            "<div id=\"d\"></div><div hidden></div><div></div><div class=\"c\" hidden></div><div></div>",
            new HiddenPage(false).RenderToString());
    }

    [Theory]
    [InlineData("""=> [H.Div.Attr(model, "v")];""")]
    [InlineData("""=> [H.Div.Attr($"data-{model}", "v")];""")]
    [InlineData("""=> [H.Div.Attr("bad name", "v")];""")]
    [InlineData("""=> [H.Div.Attr("a\"b", "v")];""")]
    [InlineData("""=> [H.Div.Attr("a=b", "v")];""")]
    [InlineData("""=> [H.Div.Attr("", "v")];""")]
    [InlineData("""=> [H.Div.Attr("id", "v")];""")]
    [InlineData("""=> [H.Div.Attr("CLASS", "v")];""")]
    [InlineData("""=> [H.Div.Attr("data-a", "1").Attr("data-a", "2")];""")]
    [InlineData("""=> [H.Div.Attr(Shared, "b")]; private static readonly string Shared = "a";""")]
    [InlineData("""=> [H.Div.Attr("data-a")];""")]
    [InlineData("""=> [H.Div.Data(model, "v")];""")]                          // a name that isn't a constant
    [InlineData("""=> [H.Div.Data("", "v")];""")]                             // data- alone
    [InlineData("""=> [H.Div.Data("userId", "v")];""")]                       // uppercase, which data names can't have
    [InlineData("""=> [H.Div.Data("bad name", "v")];""")]
    [InlineData("""=> [H.Div.Data("a", "1").Data("a", "2")];""")]
    [InlineData("""=> [H.Div.Attr("data-a", "1").Data("a", "2")];""")]       // the same attribute, written both ways
    public void Invalid_attribute_lists_are_reported(string build)
    {
        var compilation = CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText($$"""
                using System.Collections.Generic;
                using TemplateDsl;
                using TemplateDsl.Html;
                using H = TemplateDsl.Html.Elements;

                public sealed partial class T(string model) : IHtmlTemplate
                {
                    public IEnumerable<HtmlElement> Build() {{build}}
                }
                """)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var result = CSharpGeneratorDriver.Create(new TemplateGenerator()).RunGenerators(compilation).GetRunResult();

        Assert.Equal("TDSL004", Assert.Single(result.Diagnostics).Id);
        Assert.DoesNotContain("BuffersExtensions.Write", string.Concat(result.GeneratedTrees.Select(tree => tree.ToString())));
    }
}
