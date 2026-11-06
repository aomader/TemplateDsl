using System.Collections;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using FormatSamples;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using TemplateDsl.Generators;
using Xunit;

namespace Tests;

public sealed partial class ThrowingBranch(bool ok) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [H.P [ ok ? H.B : throw new InvalidOperationException("not ok") ]];
}

public sealed partial class ConstantPresence : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [H.Div.Hidden(true), H.Div.Hidden(false)];
}

public sealed partial class GuidAttributes(Guid id) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [H.Div.Id(id).Attr("data-id", id)];
}

public sealed partial class NamedFactoryArgument(string value) : IJsonTemplate
{
    public IEnumerable<JsonNode> Build() => [Json.Obj [ Json.Prop(name: "a\"b") [ value ] ]];
}

public sealed partial class AwaitInLoopHeader(Task<int[]> numbers) : IAsyncHtmlTemplate
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken cancellationToken)
    {
        var sum = 0;

        foreach (var number in await numbers)
        {
            sum += number;
        }

        return [H.P [ sum ]];
    }
}

public class GeneratorEdgeCaseTests
{
    private const string Usings = "using System; using System.Collections.Generic; using System.Threading; using System.Threading.Tasks; using TemplateDsl; using TemplateDsl.Html;\n";

    /// <summary>A minimal format whose factories take attributes as parameters.</summary>
    private const string Format = """
        [TemplateFormat(typeof(XText))]
        [Escape("t", "<", "&lt;")]
        public abstract partial class XNode : TemplateNode;

        [TextNode(Escape = "t")]
        public sealed partial class XText : XNode;

        [Node(Start = "<i", Body = ">", End = "</i>")]
        public sealed partial class XItem : XNode
        {
            [Attr(" id=\"", "\"", Escape = "t")]
            public partial XItem Id(string? value);
        }

        [TemplateTags(typeof(XNode))]
        public static partial class X
        {
            public static XItem Item(string id, string other) => new();

            public static XItem Twice(string id, string ID) => new();
        }

        [TemplateBase]
        public abstract partial class XTemplate : XNode, ITemplate<XNode>;

        """;

    [Fact]
    public void A_ternary_branch_can_throw()
    {
        Assert.Equal("<p><b></b></p>", new ThrowingBranch(ok: true).RenderToString());
        Assert.Equal("not ok", Assert.Throws<InvalidOperationException>(() => new ThrowingBranch(ok: false).RenderToString()).Message);
    }

    [Fact]
    public void A_constant_presence_attribute_is_written_only_if_true()
    {
        Assert.Equal("<div hidden></div><div></div>", new ConstantPresence().RenderToString());
    }

    [Fact]
    public void A_guid_is_written_in_attributes_as_it_is()
    {
        var id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

        Assert.Equal($"<div id=\"{id}\" data-id=\"{id}\"></div>", new GuidAttributes(id).RenderToString());
    }

    [Fact]
    public void A_factory_argument_can_be_named()
    {
        Assert.Equal("{\"a\\\"b\":\"v\"}", new NamedFactoryArgument("v").RenderToString());
    }

    [Fact]
    public async Task A_loop_can_await_in_its_header()
    {
        Assert.Equal("<p>6</p>", await new AwaitInLoopHeader(Task.FromResult<int[]>([1, 2, 3])).RenderToStringAsync());
    }

    [Theory]
    [InlineData("public extern IEnumerable<HtmlElement> Build();", "the method must have a body")]
    [InlineData("public IEnumerable<HtmlElement> Build() { var children = Children; return [H.Div]; }", "can only be part of the output")]
    public void Unsupported_build_methods_are_reported(string build, string message)
    {
        var result = Run($$"""
            public sealed partial class T : IHtmlTemplate, IAcceptsChildren
            {
                {{build}}
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "TDSL004" && diagnostic.GetMessage(CultureInfo.InvariantCulture).Contains(message));
    }

    [Theory]
    [InlineData("X.Item(\"a\", \"b\")", "'other' is not an attribute of this node")]
    [InlineData("X.Twice(\"a\", \"b\")", "'ID' appears more than once")]
    public void Factory_parameters_must_be_attributes_and_set_each_once(string node, string message)
    {
        var result = Run(Format + $$"""
            public sealed partial class T : XTemplate
            {
                public IEnumerable<XNode> Build() => [{{node}}];
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "TDSL004" && diagnostic.GetMessage(CultureInfo.InvariantCulture).EndsWith(message, StringComparison.Ordinal));
    }

    [Fact]
    public void An_async_template_with_children_in_a_sync_one_is_left_to_the_compiler()
    {
        var result = Run("""
            public sealed partial class Box : IAsyncHtmlTemplate, IAcceptsChildren
            {
                public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken cancellationToken) => [H.Div [ Children ]];
            }

            public sealed partial class T : IHtmlTemplate
            {
                public IEnumerable<HtmlElement> Build() => [new Box() [ "x" ]];
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id.StartsWith("TDSL", StringComparison.Ordinal));
        Assert.NotEmpty(result.Errors);
    }

    [Theory]
    [InlineData("""[TemplateTags(typeof(XNode))] public partial class Y { }""", "'Y' must be a static class")]
    [InlineData("""[System.ComponentModel.Description("tags")] [TemplateTags(typeof(XNode))] public partial class Y { }""", "'Y' must be a static class")]
    [InlineData("""[TemplateFormat(typeof(XText))] public abstract partial class YNode;""", "'YNode' must derive from TemplateNode")]
    [InlineData("""[TemplateFormat(typeof(YText))] [Escape("no name", "<", "&lt;")] public abstract partial class YNode : TemplateNode; [TextNode] public sealed partial class YText : YNode;""", "invalid escape rule 'no name'")]
    [InlineData("""[TextNode(Escape = "missing")] public sealed partial class YText : XNode;""", "uses the escape context 'missing', which its format doesn't declare")]
    public void Invalid_format_declarations_are_reported(string declaration, string message)
    {
        var result = Run(Format + declaration);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "TDSL006" && diagnostic.GetMessage(CultureInfo.InvariantCulture).Contains(message));
    }

    [Fact]
    public void Build_without_await_is_not_a_warning_but_other_methods_without_await_are()
    {
        var result = Run("""
            public sealed partial class T : IAsyncHtmlTemplate
            {
                public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken cancellationToken) => [H.Div];

                private static async Task<int> OtherAsync() => 1;
            }
            """);

        var warnings = result.Diagnostics.Where(diagnostic => diagnostic.Id == "CS1998").ToArray();

        Assert.Equal(2, warnings.Length);
        Assert.Single(warnings, warning => warning.IsSuppressed);
        Assert.Single(warnings, warning => !warning.IsSuppressed && warning.GetMessage(CultureInfo.InvariantCulture).Length > 0
            && warning.Location.SourceTree!.GetRoot().FindNode(warning.Location.SourceSpan).ToString().Contains("OtherAsync"));
    }

    [Fact]
    public void A_node_named_in_an_assembly_attribute_is_allowed()
    {
        var result = Run("""[assembly: System.Reflection.AssemblyMetadata("element", nameof(H.Div))]""");

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "TDSL007");
    }

    [Fact]
    public void An_unrelated_edit_reuses_what_was_generated()
    {
        var source = CSharpSyntaxTree.ParseText(Usings + """
            public sealed partial class T(string name) : IHtmlTemplate
            {
                public IEnumerable<HtmlElement> Build() => [H.P.Class("c") [ name ]];
            }
            """);
        var compilation = CreateCompilation(source);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new TemplateGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);
        driver = driver.RunGenerators(compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("public class Unrelated;")));

        var outputs = driver.GetRunResult().Results.Single().TrackedOutputSteps.SelectMany(step => step.Value).SelectMany(step => step.Outputs).ToArray();

        Assert.NotEmpty(outputs);
        Assert.All(outputs, output => Assert.Contains(output.Reason, new[] { IncrementalStepRunReason.Cached, IncrementalStepRunReason.Unchanged }));
    }

    [Fact]
    public void Equal_arrays_are_equal_as_objects_and_hash_alike()
    {
        var first = new EquatableArray<string?>(["a", null]);
        object second = new EquatableArray<string?>(["a", null]);

        Assert.True(first.Equals(second));
        Assert.False(first.Equals(new EquatableArray<string?>(["b"])));
        Assert.False(first.Equals("a"));
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        var enumerator = ((IEnumerable)first).GetEnumerator();
        Assert.True(enumerator.MoveNext());
        Assert.Equal("a", enumerator.Current);
    }

    [Fact]
    public void A_segment_kind_that_does_not_exist_is_not_shared()
    {
        Assert.Throws<InvalidOperationException>(() => TemplateGenerator.EmitShared(new StringBuilder(), new Segment((SegmentKind)999, ""), ""));
    }

    [Theory]
    [InlineData("Template")]
    [InlineData("Children")]
    public void Segments_of_one_form_of_the_render_method_are_not_shared(string name)
    {
        var kind = Enum.Parse<SegmentKind>(name);
        var error = Assert.Throws<InvalidOperationException>(() => TemplateGenerator.EmitShared(new StringBuilder(), new Segment(kind, ""), ""));

        Assert.Contains(kind.ToString(), error.Message);
    }

    [Theory]
    [InlineData("H.Each(items: numbers, build: number => H.Li)")]
    [InlineData("H.Each(numbers, (first, second) => H.Li)")]
    [InlineData("H.Each(numbers, async number => H.Li)")]
    [InlineData("H.Each(numbers, number => { return H.Li; })")]
    public void Each_needs_a_collection_and_a_lambda_with_one_parameter(string each)
    {
        var result = Run($$"""
            public sealed partial class T(int[] numbers) : IHtmlTemplate
            {
                public IEnumerable<HtmlElement> Build() => [H.Ul [ {{each}} ]];
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "TDSL004" && diagnostic.GetMessage(CultureInfo.InvariantCulture).Contains("Each needs"));
    }

    [Fact]
    public void Flags_that_are_set_to_false_explicitly_are_off()
    {
        var result = Run("""
            [TemplateFormat(typeof(FText), FormatsValues = false, TextCulture = ValueCulture.Current)]
            [Escape("t", "<", "&lt;")]
            public abstract partial class FNode : TemplateNode;

            [TextNode(Escape = "t")]
            public sealed partial class FText : FNode;

            [Node(Start = "<i", Body = ">", End = "</i>", IsVoid = false)]
            public sealed partial class FItem : FNode
            {
                [Attr(" a=\"", "\"", Escape = "t", TextOnly = false, AnyNumber = false, AlsoText = false, Culture = ValueCulture.Invariant)]
                public partial FItem A(string? value);
            }

            [TemplateTags(typeof(FNode))]
            public static partial class F
            {
                public static FItem Item => new();
            }

            public interface IFTemplate : ITemplate<FNode>;

            public sealed partial class T : IFTemplate
            {
                public IEnumerable<FNode> Build() => [F.Item.A("x") [ "y" ]];
            }
            """);

        Assert.Empty(result.Errors);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id.StartsWith("TDSL", StringComparison.Ordinal));
    }

    [Fact]
    public void Types_named_like_the_core_ones_in_other_namespaces_are_not_them()
    {
        var result = Run("""
            namespace Other.TemplateDsl { public interface IAcceptsChildren; }
            namespace Other { public interface IAcceptsChildren; }

            public sealed partial class Plain : IHtmlTemplate, Other.TemplateDsl.IAcceptsChildren, Other.IAcceptsChildren
            {
                public IEnumerable<HtmlElement> Build() => [H.Div];
            }

            public sealed partial class T : IHtmlTemplate
            {
                public IEnumerable<HtmlElement> Build() => [new Plain() [ "x" ]];
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "TDSL004" && diagnostic.GetMessage(CultureInfo.InvariantCulture).Contains("does not accept children"));
    }

    [Theory]
    [InlineData("[TemplateTags(typeof(string))] public static partial class Y { }", "'Y' must name a class marked with [TemplateFormat]")]
    [InlineData("public static partial class Outer { public sealed partial class Inner : XNode { [Attr(\" a\", \"\")] public partial Inner A(bool? value = true); } }", "'Inner.A' must be a partial method without a body, in a top-level partial class")]
    [InlineData("public partial struct S { [Attr(\" a\", \"\")] public partial S A(bool? value = true); }", "'S.A' must be a partial method without a body, in a top-level partial class")]
    [InlineData("public sealed partial class Loose { [Attr(\" a\", \"\")] public partial Loose A(bool? value = true); }", "'Loose' doesn't derive from a class marked with [TemplateFormat]")]
    public void Invalid_tags_and_attribute_methods_are_reported(string declaration, string message)
    {
        var result = Run(Format + declaration);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "TDSL006" && diagnostic.GetMessage(CultureInfo.InvariantCulture).Contains(message));
    }

    [Fact]
    public void A_format_can_derive_from_a_class_of_its_own_and_name_parameters_like_keywords()
    {
        var result = Run("""
            public abstract class Shared : TemplateNode;

            [TemplateFormat(typeof(KText))]
            public abstract partial class KNode : Shared;

            [TextNode]
            public sealed partial class KText : KNode;

            [Node(Start = "<k", Body = ">", End = "</k>")]
            public sealed partial class KItem : KNode
            {
                [Attr(" default=\"", "\"")]
                public partial KItem Default(string? @default);
            }
            """);

        Assert.Empty(result.Errors);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id.StartsWith("TDSL", StringComparison.Ordinal));
    }

    [Fact]
    public void A_ref_struct_that_implements_a_template_interface_is_reported()
    {
        var result = Run("""
            public ref partial struct S : ITemplate<HtmlElement>
            {
                public IEnumerable<HtmlElement> Build() => [H.Div];
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "TDSL002");
    }

    [Fact]
    public void Nodes_of_another_format_are_reported()
    {
        var result = Run("""
            public sealed partial class T : FormatSamples.IJsonTemplate
            {
                public IEnumerable<FormatSamples.JsonNode> Build() => [H.Div, H.Text("x")];
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "TDSL004" && diagnostic.GetMessage(CultureInfo.InvariantCulture).EndsWith("'H.Div' is not a template node", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "TDSL004" && diagnostic.GetMessage(CultureInfo.InvariantCulture).EndsWith("'H.Text(\"x\")' is not a template node", StringComparison.Ordinal));
    }

    [Fact]
    public void Async_lambdas_in_BuildAsync_and_BuildAsync_outside_templates_keep_their_warning()
    {
        var result = Run("""
            public sealed partial class T : IAsyncHtmlTemplate
            {
                private static readonly Func<Task> Field = async () => { };

                public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken cancellationToken)
                {
                    Func<Task> inner = async () => { };
                    await inner();
                    return [H.Div];
                }
            }

            public sealed class NotATemplate
            {
                public async Task BuildAsync() { }
            }
            """);

        // The lambda in BuildAsync is also copied into the generated code, which has the warning too.
        var warnings = result.Diagnostics.Where(diagnostic => diagnostic.Id == "CS1998" && !diagnostic.Location.SourceTree!.FilePath.EndsWith(".g.cs", StringComparison.Ordinal)).ToArray();

        Assert.Equal(3, warnings.Length);
        Assert.All(warnings, warning => Assert.False(warning.IsSuppressed));
    }

    /// <summary>
    /// The generators run on every keystroke in the IDE, so on code that doesn't compile yet. They must not throw
    /// there, and leave the errors to the compiler.
    /// </summary>
    [Theory]
    [InlineData("public sealed partial class T : IHtmlTemplate { public IEnumerable<HtmlElement> Build() => [H.Div [ missing ], H.Div.Hidden(missing), missing.Title]; }")]
    [InlineData("public sealed partial class T : IAsyncHtmlTemplate { public async Task<IEnumerable<HtmlElement>> BuildAsync() => [H.Div]; }")]
    [InlineData("public sealed partial class T : IHtmlTemplate<string> { public IEnumerable<HtmlElement> Build() => [H.Div]; }")]
    [InlineData("public static class Helpers { public static object Make() => new Missing(); public static object Index(object missing) => missing[0]; }")]
    [InlineData("[TemplateFormat] public abstract partial class BNode : TemplateNode; [TemplateTags] public static partial class B { }")]
    [InlineData("[Missing] public abstract partial class BNode : XNode;")]
    [InlineData("public sealed partial class T : IHtmlTemplate { public IEnumerable<HtmlElement> Build() => [() => 1, H.Div.Hidden(ToString)]; }")]
    [InlineData("public sealed partial class T : IAsyncHtmlTemplate { public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken cancellationToken) => [await H.EachAsync(() => 1, number => H.Li)]; }")]
    [InlineData("public static class Helpers { public static void Make() { var made = new(); var indexed = (() => 1)[0]; } }")]
    [InlineData("public sealed partial class BItem : XNode { [Attr] public partial BItem A(string? value); }")]
    public void Code_that_does_not_compile_yet_is_left_to_the_compiler(string source)
    {
        var result = Run(Format + source);

        Assert.NotEmpty(result.Errors);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id is "CS8784" or "CS8785" or "AD0001");
    }

    private static CSharpCompilation CreateCompilation(params SyntaxTree[] trees) =>
        CSharpCompilation.Create(
            "Test",
            trees,
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    /// <summary>Runs the generators and the analyzers, as the build does, and returns everything they and the compiler report.</summary>
    private static (Diagnostic[] Diagnostics, string[] Errors) Run(string source)
    {
        var compilation = CreateCompilation(CSharpSyntaxTree.ParseText(Usings + source));

        CSharpGeneratorDriver.Create(new FormatGenerator().AsSourceGenerator(), new TemplateGenerator().AsSourceGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generated);

        var analyzed = output
            .WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new TemplateUsageAnalyzer(), new AsyncBuildSuppressor()),
                new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), onAnalyzerException: null, concurrentAnalysis: false, logAnalyzerExecutionTime: false, reportSuppressedDiagnostics: true))
            .GetAllDiagnosticsAsync().GetAwaiter().GetResult();

        var diagnostics = generated.Concat(analyzed).ToArray();

        return (diagnostics, diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && !d.IsSuppressed).Select(d => d.Id).ToArray());
    }
}
