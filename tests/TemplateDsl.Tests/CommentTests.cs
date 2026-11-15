using System.Buffers;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TemplateDsl.Generators;
using Xunit;
using static TemplateDsl.Html.Elements;

namespace Tests;

public sealed partial class CommentPage(string? note, string first, string second) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        Comment("build 1.2.3"),            // nothing the writer changes: written while compiling
        Comment("a -- b --> c"),           // written by the writer while rendering
        Comment(note),                     // null writes nothing
        Comment($"{first}-{second}")       // one string, so the writer sees dashes that meet across the holes
    ];
}

/// <summary>
/// An HTML comment can't be escaped with character references, which a browser doesn't decode there, so its text gets
/// a zero-width space where it would otherwise end or break the comment, and nowhere else.
/// </summary>
public class CommentTests
{
    private const string Zwsp = "​";

    private static string Write(string text)
    {
        var writer = new ArrayBufferWriter<byte>();
        HtmlElement.__WriteEscaped_comment(text, writer);
        return Encoding.UTF8.GetString(writer.WrittenSpan);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("build 1.2.3", "build 1.2.3")]
    [InlineData("2026-11-14 > 3", "2026-11-14 > 3")]                       // a single dash and a > inside are harmless
    [InlineData("a -> b", "a -> b")]
    [InlineData("a--b", "a-" + Zwsp + "-b")]                              // -- , which --> , --!> and <!-- need, and XML forbids
    [InlineData("a---b", "a-" + Zwsp + "-" + Zwsp + "-b")]
    [InlineData("x --> y", "x -" + Zwsp + "-> y")]
    [InlineData("x --!> y", "x -" + Zwsp + "-!> y")]
    [InlineData("<!-- x", "<!-" + Zwsp + "- x")]
    [InlineData(">x", Zwsp + ">x")]                                       // closes the comment at once
    [InlineData("->x", "-" + Zwsp + ">x")]
    [InlineData("x-", "x-" + Zwsp)]                                       // would touch the --> of the comment
    [InlineData("x <!-", "x <!-" + Zwsp)]
    [InlineData("-", "-" + Zwsp)]
    [InlineData("ä-€--😀", "ä-€-" + Zwsp + "-😀")]
    public void Text_is_only_changed_where_it_would_end_or_break_the_comment(string text, string expected) =>
        Assert.Equal(expected, Write(text));

    [Fact]
    public void A_comment_renders_with_its_text_safe_for_constants_values_and_holes() =>
        Assert.Equal(
            "<!--build 1.2.3-->"
            + "<!--a -" + Zwsp + "- b -" + Zwsp + "-> c-->"
            + "<!--x-" + Zwsp + "-" + Zwsp + "-y-->",
            new CommentPage(null, "x-", "-y").RenderToString());

    [Fact]
    public void Constant_text_is_written_while_compiling_unless_the_writer_may_change_it()
    {
        var generated = Generate("""
            public sealed partial class T : IHtmlTemplate
            {
                public IEnumerable<HtmlElement> Build() => [H.Comment("build 1.2.3"), H.Comment("a--b")];
            }
            """);

        Assert.Contains("\"<!--build 1.2.3--><!--\"u8", generated);
        Assert.Contains("__WriteEscaped_comment((string?)(\"a--b\"), __writer);", generated);
    }

    private static string Generate(string source)
    {
        var compilation = CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText("using System.Collections.Generic; using TemplateDsl; using TemplateDsl.Html; using H = TemplateDsl.Html.Elements;\n" + source)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));

        CSharpGeneratorDriver.Create(new TemplateGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        Assert.Empty(output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        return output.SyntaxTrees.Skip(1).First(GeneratorTests.IsRenderFile).ToString();
    }
}

/// <summary>Escape rules with a writer, in a format of their own: how the generators check them, and what they write.</summary>
public class EscapeWriterTests
{
    private const string Usings = "using System; using System.Buffers; using System.Collections.Generic; using TemplateDsl;\n";

    private const string Writer = "public sealed class Upper : ITextWriter { public static void Write(ReadOnlySpan<char> text, IBufferWriter<byte> writer) { } }\n";

    private static (Diagnostic[] Diagnostics, string[] Generated, Diagnostic[] Errors) Run(string source)
    {
        var compilation = CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText(Usings + source)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));

        CSharpGeneratorDriver.Create([new FormatGenerator().AsSourceGenerator(), new TemplateGenerator().AsSourceGenerator()])
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        return (
            diagnostics.ToArray(),
            output.SyntaxTrees.Skip(1).Select(tree => tree.ToString()).ToArray(),
            output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray());
    }

    [Theory]
    [InlineData("""[Escape("w", "x", "y", Writer = typeof(Upper))]""", "both a writer and replacements")]
    [InlineData("""[Escape("w", Writer = typeof(string))]""", "'String' as the writer of an escape rule, but it doesn't implement ITextWriter")]
    public void A_writer_needs_to_be_one_and_to_be_alone(string rule, string expected)
    {
        var (diagnostics, _, _) = Run($$"""
            [TemplateFormat(typeof(WText))]
            {{rule}}
            public abstract partial class WNode : TemplateNode;

            [TextNode] public sealed partial class WText : WNode;
            """ + Writer);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("TDSL006", diagnostic.Id);
        Assert.Contains(expected, diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Without_the_characters_it_changes_a_writer_writes_all_text_also_constants_and_numbers()
    {
        var (diagnostics, generated, errors) = Run("""
            [TemplateFormat(typeof(WText), FormatsValues = true, TextCulture = ValueCulture.Invariant)]
            [Escape("w", Writer = typeof(Upper))]
            public abstract partial class WNode : TemplateNode;

            [TextNode(Escape = "w")] public sealed partial class WText : WNode;

            [Node(Start = "<i", Body = ">", End = "</i>")]
            public sealed partial class WItem : WNode
            {
                [Attr(" n=\"", "\"", Escape = "w")]
                public partial WItem N(int? value);
            }

            [TemplateTags(typeof(WNode))]
            public static partial class W
            {
                public static WItem Item => new();
            }

            public sealed partial class Page(int count) : ITemplate<WNode>
            {
                public IEnumerable<WNode> Build() => ["abc", W.Item.N(count) [ $"x{count}" ]];
            }
            """ + Writer);

        Assert.Empty(diagnostics);
        Assert.Empty(errors);

        var format = Assert.Single(generated, source => source.Contains("partial class WNode", StringComparison.Ordinal));
        var page = Assert.Single(generated, source => source.Contains("partial class Page", StringComparison.Ordinal));

        Assert.Contains("__SpecialBytes_w = null;", format);
        Assert.Contains("global::Upper.Write(rest, writer);", format);
        Assert.Contains("__WriteEscaped_w((string?)(\"abc\"), __writer);", page);
        Assert.Contains("global::WNode.__SpecialBytes_w, global::WNode.__WriteEscaped_w);", page);
        Assert.Contains("__WriteEscaped_w((string?)(string.Create(global::System.Globalization.CultureInfo.InvariantCulture, $\"x{count}\")), __writer);", page);
    }

    [Fact]
    public void A_value_in_a_comment_goes_through_its_writer()
    {
        var writer = new ArrayBufferWriter<byte>();
        HtmlElement.__WriteEscaped_comment(-5, writer, provider: System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal("-5", Encoding.UTF8.GetString(writer.WrittenSpan));
    }
}
