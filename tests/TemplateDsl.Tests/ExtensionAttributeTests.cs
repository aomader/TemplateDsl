using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TemplateDsl.Generators;
using System.Runtime.Serialization;
using Xunit;

namespace Tests;

/// <summary>How htmx swaps content in. Its values are camel case, which the enum members say.</summary>
public enum HxSwapMode
{
    [EnumMember(Value = "innerHTML")]
    InnerHtml,

    [EnumMember(Value = "outerHTML")]
    OuterHtml,
}

/// <summary>
/// Attributes that a package adds to the elements of the HTML format, as one for htmx would: extension methods whose
/// parameters say which attribute they write. One with several parameters writes several attributes.
/// </summary>
public static partial class HxAttributes
{
    public static partial TElement HxGet<TElement>(this TElement element, [Attr(" hx-get=\"", "\"", Escape = "attribute")] string? url)
        where TElement : Nodes.HtmlTag<TElement>;

    public static partial TElement HxTarget<TElement>(this TElement element, [Attr(" hx-target=\"", "\"", Escape = "attribute")] string? target)
        where TElement : Nodes.HtmlTag<TElement>;

    public static partial TElement HxSwap<TElement>(this TElement element, [Attr(" hx-swap=\"", "\"", Escape = "attribute")] HxSwapMode? swap)
        where TElement : Nodes.HtmlTag<TElement>;

    public static partial TElement HxConfirm<TElement>(this TElement element, [Attr(" hx-confirm=\"", "\"", Escape = "attribute", Culture = ValueCulture.Current)] string? question)
        where TElement : Nodes.HtmlTag<TElement>;

    public static partial TElement HxBoost<TElement>(this TElement element, [Attr(" hx-boost=\"", "\"", Escape = "attribute")] bool? boost)
        where TElement : Nodes.HtmlTag<TElement>;

    public static partial TElement HxDisable<TElement>(this TElement element, [Attr(" hx-disable", "")] bool? disable = true)
        where TElement : Nodes.HtmlTag<TElement>;

    public static partial TElement HxDelay<TElement>(this TElement element, [Attr(" hx-delay=\"", "\"", Escape = "attribute", AnyNumber = true)] double? milliseconds)
        where TElement : Nodes.HtmlTag<TElement>;

    /// <summary>Only on a form.</summary>
    public static partial Nodes.Form HxEncoding(this Nodes.Form form, [Attr(" hx-encoding=\"", "\"", Escape = "attribute")] string? encoding);

    /// <summary>Several attributes in one call, in the order of the arguments, leaving out those that are left out.</summary>
    public static partial TElement HxPost<TElement>(
        this TElement element,
        [Attr(" hx-post=\"", "\"", Escape = "attribute")] string url,
        [Attr(" hx-target=\"", "\"", Escape = "attribute")] string? target = null,
        [Attr(" hx-swap=\"", "\"", Escape = "attribute")] HxSwapMode? swap = null)
        where TElement : Nodes.HtmlTag<TElement>;
}

public sealed partial class HxPage(string url, int delay, bool boost) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        H.Button.Type("button").HxGet(url).HxTarget("closest li").HxSwap(HxSwapMode.OuterHtml) [ "Load" ],
        H.Form.HxPost("/todos", target: "#app", swap: HxSwapMode.OuterHtml).HxEncoding("multipart/form-data") [ "x" ],
        H.Form.HxPost("/plain"),
        H.Form.HxPost(swap: HxSwapMode.InnerHtml, url: url),
        H.A.HxBoost(boost).HxConfirm($"Sure, {1.5}?").HxDisable() [ "Go" ],
        H.Div.HxDelay(delay).Attr("hx-ext", "debug")
    ];
}

public class ExtensionAttributeTests
{
    [Fact]
    public void Extension_methods_write_the_attributes_their_parameters_say()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");

        try
        {
            Assert.Equal(
                "<button type=\"button\" hx-get=\"/a?b=1&amp;c=2\" hx-target=\"closest li\" hx-swap=\"outerHTML\">Load</button>"
                + "<form hx-post=\"/todos\" hx-target=\"#app\" hx-swap=\"outerHTML\" hx-encoding=\"multipart/form-data\">x</form>"
                + "<form hx-post=\"/plain\"></form>"
                + "<form hx-swap=\"innerHTML\" hx-post=\"/a?b=1&amp;c=2\"></form>"
                + "<a hx-boost=\"false\" hx-confirm=\"Sure, 1,5?\" hx-disable>Go</a>"
                + "<div hx-delay=\"300\" hx-ext=\"debug\"></div>",
                new HxPage("/a?b=1&c=2", 300, false).RenderToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }
}

/// <summary>Attribute methods as the generators check them: extension methods, and several attributes in one method of a node.</summary>
public class ExtensionAttributeGeneratorTests
{
    private const string Extensions = """
        using System.Collections.Generic;
        using TemplateDsl;
        using TemplateDsl.Html;
        using TemplateDsl.Html.Nodes;
        using H = TemplateDsl.Html.Elements;

        public static partial class Hx
        {
            public static partial TElement HxGet<TElement>(this TElement element, [Attr(" hx-get=\"", "\"", Escape = "attribute")] string? url)
                where TElement : HtmlTag<TElement>;

            public static partial TElement HxTarget<TElement>(this TElement element, [Attr(" hx-target=\"", "\"", Escape = "attribute")] string? target)
                where TElement : HtmlTag<TElement>;

            public static partial TElement HxPost<TElement>(
                this TElement element,
                [Attr(" hx-post=\"", "\"", Escape = "attribute")] string url,
                [Attr(" hx-target=\"", "\"", Escape = "attribute")] string? target = null)
                where TElement : HtmlTag<TElement>;

            public static partial HtmlElement HxAny(this HtmlElement element, [Attr(" hx-any=\"", "\"", Escape = "attribute")] string? value);
        }

        """;

    private static (Diagnostic[] Generator, Diagnostic[] Errors, string[] Generated) Run(string source)
    {
        var compilation = CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText(source)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        CSharpGeneratorDriver.Create([new FormatGenerator().AsSourceGenerator(), new TemplateGenerator().AsSourceGenerator()])
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        return (
            diagnostics.ToArray(),
            output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray(),
            output.SyntaxTrees.Skip(1).Select(tree => tree.ToString()).ToArray());
    }

    private static string Page(string build) => Extensions + $$"""
        public sealed partial class Page : IHtmlTemplate
        {
            public IEnumerable<HtmlElement> Build() => [{{build}}];
        }
        """;

    [Fact]
    public void An_extension_method_for_any_node_of_the_format_is_written_too()
    {
        var (generator, errors, generated) = Run(Page("H.Div.HxGet(\"/a\").HxPost(\"/b\")"));

        Assert.Empty(generator);
        Assert.Empty(errors);
        Assert.Contains("<div hx-get=\\\"/a\\\" hx-post=\\\"/b\\\"></div>", string.Concat(generated));
    }

    [Theory]
    [InlineData("""H.Div.HxGet("/a").Attr("hx-get", "/b")""", "'hx-get' appears more than once")]
    [InlineData("""H.Div.HxPost("/a", target: "#x").HxTarget("#y")""", "'HxTarget' appears more than once")]
    [InlineData("""H.Div.HxPost("/a").HxPost("/b")""", "'url' of 'HxPost' is an attribute that appears more than once")]
    [InlineData("""H.Div.HxPost(url: "/a", nope: "x")""", "'nope: \"x\"' is not a parameter of 'HxPost'")]
    [InlineData("""H.Div.HxPost("/a", "#x", "extra")""", "'\"extra\"' is not a parameter of 'HxPost'")]
    [InlineData("""H.Div.HxGet()""", "'H.Div.HxGet()' must have one value")]
    public void Attributes_of_extension_methods_are_checked_like_those_of_the_node(string build, string expected)
    {
        var (generator, _, _) = Run(Page(build));

        Assert.Contains(generator, diagnostic => diagnostic.Id == "TDSL004" && diagnostic.GetMessage(CultureInfo.InvariantCulture).Contains(expected, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""public static partial TElement Bad<TElement>(this TElement element, [Attr(" a=\"", "\"", Escape = "attribute")] string? a, string? b) where TElement : HtmlTag<TElement>;""",
        "must say which attribute each of its parameters writes")]
    [InlineData("""public static partial TElement Bad<TElement>(this TElement element, [Attr(" a=\"", "\"", Escape = "attribute")] string? a = "x", [Attr(" b=\"", "\"", Escape = "attribute")] string? b = null) where TElement : HtmlTag<TElement>;""",
        "writes several attributes, so a parameter can only default to null")]
    [InlineData("""public static partial string Bad(this string text, [Attr(" a=\"", "\"")] string? a);""", "must extend a node of a format")]
    [InlineData("""public static partial int[] Bad(this int[] values, [Attr(" a=\"", "\"")] string? a);""", "must extend a node of a format")]
    [InlineData("""public static partial TElement Bad<TElement>(this TElement element, [Attr(" a=\"", "\"", Escape = "attribute")] string? a, [Attr(" b=\"", "\"", Escape = "unknown")] string? b) where TElement : HtmlTag<TElement>;""",
        "uses the escape context 'unknown'")]
    public void Extension_methods_that_can_not_write_their_attributes_are_reported(string method, string expected)
    {
        var (generator, _, _) = Run(Extensions + "public static partial class Bad { " + method + " }");

        var diagnostic = Assert.Single(generator);
        Assert.Equal("TDSL006", diagnostic.Id);
        Assert.Contains(expected, diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private const string Format = """
        using System.Collections.Generic;
        using TemplateDsl;

        [TemplateFormat(typeof(XText))]
        [Escape("t", "<", "&lt;")]
        public abstract partial class XNode : TemplateNode;

        [TextNode(Escape = "t")] public sealed partial class XText : XNode;

        [Node(Start = "<x", Body = ">", End = "</x>")]
        public sealed partial class XItem : XNode
        {
            public partial XItem Pair([Attr(" a=", "", Escape = "t")] string? first = null, [Attr(" b=", "", Escape = "t")] string? second = null);
        }

        [TemplateTags(typeof(XNode))]
        public static partial class X
        {
            public static XItem Item => new();

            public static XItem Make([Attr(" c=", "", Escape = "t")] string? first = null, [Attr(" d=", "", Escape = "t")] string? second = null) => new();
        }

        """;

    [Fact]
    public void A_method_of_a_node_writes_several_attributes_too()
    {
        var (generator, errors, generated) = Run(Format + """
            public sealed partial class Page : ITemplate<XNode>
            {
                public IEnumerable<XNode> Build() => [X.Item.Pair("1", "2"), X.Item.Pair(second: "<")];
            }
            """);

        Assert.Empty(generator);
        Assert.Empty(errors);
        Assert.Contains("\"<x a=1 b=2></x><x b=&lt;></x>\"", string.Concat(generated));
    }

    [Fact]
    public void The_parameters_of_a_factory_write_the_attributes_they_say()
    {
        var (generator, errors, generated) = Run(Format + """
            public sealed partial class Page : ITemplate<XNode>
            {
                public IEnumerable<XNode> Build() => [X.Make("1"), X.Make(second: "<"), X.Make("1", "2")];
            }
            """);

        Assert.Empty(generator);
        Assert.Empty(errors);
        Assert.Contains("\"<x c=1></x><x d=&lt;></x><x c=1 d=2></x>\"", string.Concat(generated));
    }

    [Theory]
    [InlineData("public static XItem Bad(string? plain) => new();", "must say which attribute each of its parameters writes, with [Attr]")]
    [InlineData("""public static XItem Bad([Attr(" a=", "", Escape = "t")] object? value) => new();""", "must take a string, bool or value type")]
    [InlineData("""public static XItem Bad([Attr(" a=", "", Escape = "t")] string? value = "x") => new();""", "can only default to null")]
    [InlineData("""public static XItem Bad([Attr(" a=", "", Escape = "unknown")] string? value) => new();""", "uses the escape context 'unknown'")]
    [InlineData("public static XText Bad(string? value, int extra) => new();", "creates a text node, so it takes one string, its text, which needs no [Attr]")]
    [InlineData("public static XText Bad() => new();", "creates a text node, so it takes one string")]
    [InlineData("public static XText Bad(int value) => new();", "creates a text node, so it takes one string")]
    [InlineData("""public static XText Bad([Attr(" a=", "", Escape = "t")] string? value) => new();""", "creates a text node, so it takes one string")]
    public void Factories_that_can_not_write_their_attributes_are_reported(string factory, string expected)
    {
        var (generator, _, _) = Run(Format.Replace("public static XItem Item => new();", "public static XItem Item => new();\n\n            " + factory, StringComparison.Ordinal));

        var diagnostic = Assert.Single(generator);
        Assert.Equal("TDSL006", diagnostic.Id);
        Assert.Contains("X.Bad' " + expected, diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void A_method_of_the_class_of_tags_that_creates_no_node_is_no_factory()
    {
        var (generator, _, _) = Run(Format.Replace("public static XItem Item => new();", "public static XItem Item => new();\n\n            public static string Helper(int count) => \"\";", StringComparison.Ordinal));

        Assert.Empty(generator);
    }
}
