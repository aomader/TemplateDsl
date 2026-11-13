using System.Buffers;
using System.Text;
using TemplateDsl.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Tests;

/// <summary>A node declared outside the library, with a layout unlike the built-in ones.</summary>
[Node(Start = "<section", Body = ">", End = "</section>")]
public sealed partial class Section : HtmlElement
{
    [Attr(" data-name='", "'", Escape = "attribute")]
    public partial Section Name(string? value);

    [Attr(" raw=", "")]
    public partial Section Raw(string? value);
}

[TemplateTags(typeof(HtmlElement))]
public static partial class Sections
{
    public static Section Section => new();
}

public sealed partial class SectionPage(string value) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        Sections.Section.Name("a&b").Raw("x")["text"],
        Sections.Section.Name(value)[Sections.Section],
        Sections.Section.Name($"n-{value}")
    ];
}

public class FormatTests
{
    private const string Usings = """
        using System.Collections.Generic;
        using TemplateDsl;
        using TemplateDsl.Html;

        """;

    private static string[] RunFormatGenerator(out string[] generated, string source)
    {
        var compilation = CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText(Usings + source)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var result = CSharpGeneratorDriver.Create(new FormatGenerator()).RunGenerators(compilation).GetRunResult();

        generated = result.GeneratedTrees.Select(tree => tree.ToString()).ToArray();
        return result.Diagnostics.Select(d => d.Id).ToArray();
    }

    [Fact]
    public void Compiled_template_renders_a_node_from_its_attributes()
    {
        Assert.Equal(
            "<section data-name='a&amp;b' raw=x>text</section>"
            + "<section data-name='v'><section></section></section>"
            + "<section data-name='n-v'></section>",
            new SectionPage("v").RenderToString());
    }

    [Theory]
    [InlineData("""=> [H.Div];""", "<div></div>")]
    [InlineData("""=> [H.Div.Class("c").Id("a&b")];""", "<div class=\\\"c\\\" id=\\\"a&amp;b\\\"></div>")]
    [InlineData("""=> [H.Div.Id(null)["x"]];""", "<div>x</div>")]
    public void Chained_methods_set_attributes_in_source_order(string build, string expected)
    {
        var (diagnostics, errors, generated) = RunTemplate($$"""
            public sealed partial class T : IHtmlTemplate
            {
                public IEnumerable<HtmlElement> Build() {{build}}
            }
            """);

        Assert.Empty(diagnostics);
        Assert.Empty(errors);
        Assert.Contains($"\"{expected}\"u8", Assert.Single(generated));
    }

    [Theory]
    [InlineData("""=> [H.Div.Children(null)];""")]
    [InlineData("""=> [H.Div("x")];""")]
    public void Anything_else_on_a_node_is_reported(string build)
    {
        var (diagnostics, errors, generated) = RunTemplate($$"""
            public sealed partial class T : IHtmlTemplate
            {
                public IEnumerable<HtmlElement> Build() {{build}}
            }
            """);

        Assert.Contains("TDSL004", diagnostics);
        Assert.NotEmpty(errors);
        Assert.DoesNotContain("BuffersExtensions.Write", string.Concat(generated));
    }

    private static (string[] Diagnostics, string[] Errors, string[] Generated) RunTemplate(string source)
    {
        var compilation = CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText("using System.Collections.Generic; using TemplateDsl; using TemplateDsl.Html; using TemplateDsl.Html.Elements;\n" + source)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            // Release, as the generated code these tests look for is the one that ships, with u8 literals.
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));

        CSharpGeneratorDriver.Create(new TemplateGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        return (
            diagnostics.Select(d => d.Id).ToArray(),
            output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id).ToArray(),
            output.SyntaxTrees.Skip(1).Select(tree => tree.ToString()).ToArray());
    }

    [Fact]
    public void Escape_methods_are_generated_from_the_format()
    {
        Assert.Equal("&lt;&quot;&amp;&#39;", Escaped(HtmlElement.__WriteEscaped_attribute, "<\"&'"));
        Assert.Equal("&lt;\"&amp;'", Escaped(HtmlElement.__WriteEscaped_text, "<\"&'"));
    }

    private static string Escaped(Action<string?, IBufferWriter<byte>> write, string? value)
    {
        var writer = new ArrayBufferWriter<byte>();
        write(value, writer);
        return Encoding.UTF8.GetString(writer.WrittenSpan);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("plain", "plain")]
    [InlineData("<", "&lt;")]
    [InlineData("<>", "&lt;&gt;")]
    [InlineData("a<b", "a&lt;b")]
    [InlineData("<a", "&lt;a")]
    [InlineData("a<", "a&lt;")]
    [InlineData("&amp;", "&amp;amp;")]
    [InlineData("ä€😀<😀", "ä€😀&lt;😀")]
    public void Escaper_handles_every_position(string? value, string expected)
    {
        Assert.Equal(expected, Escaped(HtmlElement.__WriteEscaped_text, value));
    }

    [Theory]
    [InlineData("""[TemplateFormat(typeof(string))] [Escape("a b", "x", "y")] public partial class F : TemplateNode;""")]
    [InlineData("""[TemplateFormat(typeof(string))] [Escape("a", "x")] public partial class F : TemplateNode;""")]
    [InlineData("""[TemplateFormat(typeof(string))] [Escape("a", "xx", "y")] public partial class F : TemplateNode;""")]
    public void Invalid_escape_rules_are_reported(string source)
    {
        var diagnostics = RunFormatGenerator(out var generated, source);

        Assert.Equal("TDSL006", Assert.Single(diagnostics));
        Assert.Empty(generated);
    }

    [Fact]
    public void Escape_rules_are_applied_in_one_pass_regardless_of_their_order()
    {
        var diagnostics = RunFormatGenerator(out var generated, """
            [TemplateFormat(typeof(Q))] [Escape("x", "<", "&lt;", "&", "&amp;")] public abstract partial class F : TemplateNode;
            [TextNode] public sealed partial class Q : F;
            """);

        Assert.Empty(diagnostics);
        Assert.Contains("__WriteEscaped_x", Assert.Single(generated));
    }

    [Theory]
    [InlineData("[Node] public sealed class N : HtmlElement;")]
    [InlineData("[Node] public sealed partial class N;")]
    public void Invalid_nodes_are_reported(string source)
    {
        var diagnostics = RunFormatGenerator(out var generated, source);

        Assert.Equal("TDSL006", Assert.Single(diagnostics));
        Assert.Empty(generated);
    }

    [Theory]
    [InlineData("""[Attr("a", "b", Escape = "unknown")] public partial N A(string? value);""")]
    [InlineData("""[Attr("a", "b")] public partial N A(int value, int other);""")]
    [InlineData("""[Attr("a", "b")] public partial N A(object? value);""")]
    [InlineData("""[AttrList] public partial N Attr(string name);""")]
    public void Invalid_attribute_methods_are_reported_and_only_get_a_body(string method)
    {
        var diagnostics = RunFormatGenerator(out var generated, $$"""[Node] public sealed partial class N : HtmlElement { {{method}} }""");

        Assert.Equal("TDSL006", Assert.Single(diagnostics));
        var attributes = Assert.Single(generated, source => source.Contains("public partial"));
        Assert.DoesNotContain("TValue", attributes);
    }

    [Fact]
    public void An_attribute_method_with_a_body_is_reported()
    {
        var diagnostics = RunFormatGenerator(out _, """
            [Node] public sealed partial class N : HtmlElement { [Attr("a", "b")] public N A(string? value) => this; }
            """);

        Assert.Equal("TDSL006", Assert.Single(diagnostics));
    }

    [Fact]
    public void Valid_node_gets_brackets_and_its_attribute_methods_get_bodies_and_overloads()
    {
        var diagnostics = RunFormatGenerator(out var generated, """
            [Node(Start = "<n", Body = ">", End = "</n>")]
            public sealed partial class N : HtmlElement { [Attr(" a=", "", Escape = "text")] public partial N A(string? value); }
            """);

        Assert.Empty(diagnostics);
        Assert.Contains(generated, source => source.Contains("this[params"));

        var attributes = Assert.Single(generated, source => source.Contains("public partial"));
        Assert.Contains("public partial global::N A(string? value) =>", attributes);
        Assert.Contains("public global::N A<TValue>(TValue value)", attributes);
    }

    [Theory]
    [InlineData("[Node] public sealed partial class Q : F;")]
    [InlineData("public sealed partial class Q : F;")]
    public void A_format_needs_a_real_text_node_or_its_strings_would_not_be_escaped(string textNode)
    {
        var diagnostics = RunFormatGenerator(out _, $$"""
            [TemplateFormat(typeof(Q))]
            public abstract partial class F : TemplateNode;
            {{textNode}}
            """);

        Assert.Contains("TDSL006", diagnostics);
    }

    [Fact]
    public void A_base_class_can_declare_attributes_that_return_the_node_itself()
    {
        var diagnostics = RunFormatGenerator(out var generated, """
            public abstract partial class Shared<TNode> : HtmlElement where TNode : Shared<TNode>
            {
                [Attr(" s=", "", Escape = "text")] public partial TNode S(string? value);
            }
            [Node(Start = "<n", Body = ">", End = "</n>")]
            public sealed partial class N : Shared<N>;
            """);

        Assert.Empty(diagnostics);
        Assert.Contains(generated, source => source.Contains("partial class Shared<TNode>") && source.Contains("public partial TNode S(string? value) =>"));
    }
}
