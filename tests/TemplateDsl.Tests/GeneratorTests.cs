using TemplateDsl.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Tests;

public class GeneratorTests
{
    private const string Usings = """
        using System.Collections.Generic;
        using TemplateDsl;
        using TemplateDsl.Html;
        using TemplateDsl.Html.Elements;

        """;

    private static CSharpCompilation CreateCompilation(params string[] sources) =>
        CSharpCompilation.Create(
            "Test",
            sources.Select(source => CSharpSyntaxTree.ParseText(Usings + source)),
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static (string[] Generated, string[] DiagnosticIds, string[] CompileErrors) Run(params string[] sources)
    {
        var compilation = CreateCompilation(sources);

        CSharpGeneratorDriver.Create(new TemplateGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        var generated = output.SyntaxTrees.Skip(sources.Length).Select(tree => tree.ToString()).ToArray();
        var errors = output.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.Id)
            .ToArray();

        return (generated, diagnostics.Select(d => d.Id).ToArray(), errors);
    }

    private static string Template(string build) => $$"""
        public sealed partial class T(string model) : IHtmlTemplate
        {
            public IEnumerable<HtmlElement> Build() {{build}}
        }
        """;

    [Fact]
    public void RenderToString_writes_the_compiled_template()
    {
        Assert.Equal(
            "<html><body><div></div><div><div></div></div></body></html>",
            new PageTemplate().RenderToString());
    }

    [Theory]
    [InlineData("=> [H.Html[H.Body[H.Div]]];")]
    [InlineData("{ return [H.Html[H.Body[H.Div]]]; }")]
    [InlineData("=> ([(H.Html[(H.Body[(H.Div)])])]);")]
    [InlineData("=> new HtmlElement[] { H.Html[H.Body[H.Div]] };")]
    public void Supported_forms_generate_the_same_html(string build)
    {
        var (generated, diagnostics, errors) = Run(Template(build));

        Assert.Empty(diagnostics);
        Assert.Empty(errors);
        Assert.Contains(
            """BuffersExtensions.Write(__writer, "<html><body><div></div></body></html>"u8);""",
            Assert.Single(generated));
    }

    [Theory]
    [InlineData("""=> [H.Div.Id("a")[H.Div]];""", "<div id=\\\"a\\\"><div></div></div>")]
    [InlineData("""=> [(H.Div.Id("a"))[(H.Div)]];""", "<div id=\\\"a\\\"><div></div></div>")]
    [InlineData("""=> [H.Div[H.Div.Id("1"), H.Div.Id("2"), H.Div.Id("3")]];""", "<div><div id=\\\"1\\\"></div><div id=\\\"2\\\"></div><div id=\\\"3\\\"></div></div>")]
    [InlineData("""=> [H.Div.Id("a").Class("b")];""", "<div id=\\\"a\\\" class=\\\"b\\\"></div>")]
    [InlineData("""=> [H.Div.Class("b").Id("a")];""", "<div class=\\\"b\\\" id=\\\"a\\\"></div>")]
    [InlineData("""=> [H.Div.Id("")];""", "<div id=\\\"\\\"></div>")]
    [InlineData("""=> [H.Div.Id(null)];""", "<div></div>")]
    public void Attributes_and_bracket_children_are_translated(string build, string expected)
    {
        var (generated, diagnostics, errors) = Run(Template(build));

        Assert.Empty(diagnostics);
        Assert.Empty(errors);
        Assert.Contains($"BuffersExtensions.Write(__writer, \"{expected}\"u8);", Assert.Single(generated));
    }

    [Theory]
    [InlineData("""=> [H.Div[Helper()]]; private static HtmlElement Helper() => H.Div;""")]
    [InlineData("=> [H.Div[children: H.Div]];")]
    [InlineData("=> [Items[0]]; private static readonly HtmlElement[] Items = [H.Div];")]
    public void Invalid_bracket_children_are_reported(string build)
    {
        var (generated, diagnostics, _) = Run(Template(build));

        Assert.Equal("TDSL004", Assert.Single(diagnostics));
        Assert.DoesNotContain("BuffersExtensions.Write", string.Concat(generated));
    }

    [Theory]
    [InlineData("=> [H.Div[model]];")]
    [InlineData("=> [model];")]
    [InlineData("=> [H.Text(model)];")]
    [InlineData("""=> [H.Div[$"hi {model}"]];""")]
    [InlineData("=> [H.Div[model.Length > 0 ? model : null]];")]
    [InlineData("=> [H.Div.Id(model)];")]
    [InlineData("=> [H.Div.Class(model)[H.Div]];")]
    [InlineData("""=> [H.Div.Id($"a{model}").Class(model)];""")]
    [InlineData("""=> [H.Div.Id($"{model.Length:D3}")];""")]
    public void Dynamic_values_generate_code_that_compiles(string build)
    {
        var (generated, diagnostics, errors) = Run(Template(build));

        Assert.Empty(diagnostics);
        Assert.Empty(errors);
        Assert.Single(generated);
    }

    [Theory]
    [InlineData("""=> [H.Div[model = "x"]];""")]
    [InlineData("""=> [H.Div.Id(model = "x")];""")]
    [InlineData("""=> [H.Div[$"{model = "x"}"]];""")]
    [InlineData("=> [H.Div[(Counter++).ToString()]]; private static int Counter;")]
    public void Dynamic_values_with_side_effects_are_allowed(string build)
    {
        var (generated, diagnostics, errors) = Run(Template(build));

        Assert.Empty(diagnostics);
        Assert.Empty(errors);
        Assert.Single(generated);
    }

    [Theory]
    [InlineData("{ if (model.Length > 0) { return [H.Div]; } return []; }")]
    [InlineData("{ if (model.Length > 0) return [H.Div]; else return [H.Body]; }")]
    [InlineData("{ if (model is { Length: > 0 } text) { yield return text; } yield break; }")]
    [InlineData("""{ if (model is "a") { yield return H.Div; } if (model is { } first) { yield return first; } if (model is { } second) { yield return second; } }""")]
    [InlineData("=> model.Length > 0 ? [H.Div] : [];")]
    [InlineData("=> [model.Length > 0 ? H.Div : H.Body];")]
    [InlineData("""=> [H.Div.Id(model.Length > 0 ? "a" : model)];""")]
    public void Conditionals_generate_code_that_compiles(string build)
    {
        var (generated, diagnostics, errors) = Run(Template(build));

        Assert.Empty(diagnostics);
        Assert.Empty(errors);
        Assert.Contains("if (", Assert.Single(generated));
    }

    [Fact]
    public void Generated_code_from_an_unreachable_branch_does_not_warn()
    {
        var compilation = CreateCompilation(Template("=> [true ? H.Div : H.Body];"));

        CSharpGeneratorDriver.Create(new TemplateGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        var generatedTree = output.SyntaxTrees.Last();
        Assert.DoesNotContain(
            output.GetDiagnostics(),
            d => d.Location.SourceTree == generatedTree && d.Severity >= DiagnosticSeverity.Warning);
    }

    [Theory]
    [InlineData("writer")]
    [InlineData("stream")]
    [InlineData("cancellationToken")]
    [InlineData("@class")]
    public void Constructor_parameter_can_have_any_name(string parameter)
    {
        var (generated, diagnostics, errors) = Run($$"""
            public sealed partial class T(string {{parameter}}) : IHtmlTemplate
            {
                public IEnumerable<HtmlElement> Build() => [H.Div.Id({{parameter}})[{{parameter}}]];
            }
            """);

        Assert.Empty(diagnostics);
        Assert.Empty(errors);
        Assert.Single(generated);
    }

    [Fact]
    public void Usings_of_the_template_file_are_available_to_copied_expressions()
    {
        var (_, diagnostics, errors) = Run("""
            using System.Globalization;
            using Alias = System.String;

            namespace My.App
            {
                using System.Text;

                public sealed partial class T(int model) : IHtmlTemplate
                {
                    public IEnumerable<HtmlElement> Build() =>
                    [
                        model.ToString(CultureInfo.InvariantCulture),
                        Alias.Empty + Encoding.UTF8.WebName + model
                    ];
                }
            }
            """);

        Assert.Empty(diagnostics);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("""=> ["text"];""")]
    [InlineData("""=> new HtmlElement[] { "text" };""")]
    public void Strings_are_allowed_at_the_top_level(string build)
    {
        var (generated, diagnostics, errors) = Run(Template(build));

        Assert.Empty(diagnostics);
        Assert.Empty(errors);
        Assert.Contains("\"text\"u8", Assert.Single(generated));
    }

    [Fact]
    public void Text_without_a_value_does_not_crash_the_generator()
    {
        var (generated, _, errors) = Run(Template("=> [H.Div[H.Text()]];"));

        Assert.Contains("CS7036", errors);
        Assert.DoesNotContain("BuffersExtensions.Write", string.Concat(generated));
    }

    [Fact]
    public void Multiple_top_level_elements_are_written_in_order()
    {
        var (generated, _, _) = Run(Template("=> [H.Div, H.Body];"));

        Assert.Contains("\"<div></div><body></body>\"", Assert.Single(generated));
    }

    [Fact]
    public void Empty_template_generates_a_method_without_appends()
    {
        var (generated, diagnostics, errors) = Run(Template("=> [];"));

        Assert.Empty(diagnostics);
        Assert.Empty(errors);
        Assert.DoesNotContain("BuffersExtensions.Write", Assert.Single(generated));
    }

    [Fact]
    public void Generated_methods_are_instance_methods_in_the_templates_namespace()
    {
        var (generated, _, errors) = Run("""
            namespace My.App;

            public sealed partial class T : IHtmlTemplate
            {
                public IEnumerable<HtmlElement> Build() => [H.Div];
            }
            """);

        Assert.Empty(errors);
        var source = Assert.Single(generated);
        Assert.Contains("namespace My.App;", source);
        Assert.Contains("public void RenderTo(", source);
        Assert.DoesNotContain("static void", source);
    }

    [Fact]
    public void Static_build_with_a_context_does_not_compile()
    {
        var (_, _, errors) = Run("""
            public sealed partial class T : IHtmlTemplate<string>
            {
                public static IEnumerable<HtmlElement> Build(string model) => [H.Div];
            }
            """);

        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Partial_declarations_with_several_base_lists_generate_once()
    {
        var (generated, diagnostics, errors) = Run(
            """
            public sealed partial class T : IHtmlTemplate
            {
                public IEnumerable<HtmlElement> Build() => [H.Div];
            }
            """,
            "public sealed partial class T : System.IDisposable { public void Dispose() { } }");

        Assert.Empty(diagnostics);
        Assert.Empty(errors);
        Assert.Single(generated);
    }

    [Fact]
    public void Non_templates_are_ignored()
    {
        var (generated, diagnostics, _) = Run("public class T : System.IDisposable { public void Dispose() { } }");

        Assert.Empty(generated);
        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("public sealed class T", "TDSL001")]
    [InlineData("public ref partial struct T", "TDSL002")]
    [InlineData("file sealed partial class T", "TDSL003")]
    public void Invalid_template_shapes_are_reported(string declaration, string expectedId)
    {
        var (generated, diagnostics, _) = Run($$"""
            {{declaration}} : IHtmlTemplate
            {
                public IEnumerable<HtmlElement> Build() => [H.Div];
            }
            """);

        Assert.Equal(expectedId, Assert.Single(diagnostics));
        Assert.Empty(generated);
    }

    [Fact]
    public void Missing_partial_is_the_only_error_and_sealed_is_optional()
    {
        var (_, diagnostics, errors) = Run("""
            public class T : IHtmlTemplate
            {
                public IEnumerable<HtmlElement> Build() => [H.Div];
            }
            """);

        Assert.Equal("TDSL001", Assert.Single(diagnostics));
        Assert.Empty(errors);
    }

    [Fact]
    public void Nested_template_is_reported()
    {
        var (generated, diagnostics, _) = Run("""
            public partial class Outer
            {
                public sealed partial class T : IHtmlTemplate
                {
                    public IEnumerable<HtmlElement> Build() => [H.Div];
                }
            }
            """);

        Assert.Equal("TDSL003", Assert.Single(diagnostics));
        Assert.Empty(generated);
    }

    [Theory]
    [InlineData("{ var div = H.Div; return [div]; }")]
    [InlineData("=> [Helper()]; private static HtmlElement Helper() => H.Div;")]
    [InlineData("=> [model.Length > 0 ? Helper() : H.Div]; private static HtmlElement Helper() => H.Div;")]
    [InlineData("=> [default(TemplateDsl.Html.Elements.Div)];")]
    [InlineData("=> [.. new HtmlElement[] { H.Div }];")]
    [InlineData("""{ do { yield return "x"; } while (false); }""")]
    [InlineData("=> [H.Div[H.Each(model, c => { return H.Div; })]];")]
    [InlineData("=> [H.Div[H.Each(model, Make)]]; private static HtmlElement Make(char c) => H.Div;")]
    [InlineData("=> [H.Div[new HtmlElement[] { H.Div }]];")]
    [InlineData("=> [H.Html(H.Body)];")]
    [InlineData("=> new List<HtmlElement>();")]
    [InlineData("=> new HtmlElement[1];")]
    [InlineData("{ var unused = H.Div; return []; }")]
    [InlineData("{ HtmlElement unused = H.Div; return []; }")]
    [InlineData("""{ HtmlElement unused = "a"; return []; }""")]
    [InlineData("{ System.Func<HtmlElement> unused = () => H.Div; return []; }")]
    [InlineData("{ new List<HtmlElement>().Add(H.Div); return []; }")]
    [InlineData("{ HtmlElement unused; unused = H.Div; return []; }")]
    public void Unsupported_build_bodies_are_reported(string build)
    {
        var (generated, diagnostics, _) = Run(Template(build));

        Assert.All(diagnostics, id => Assert.Equal("TDSL004", id));
        Assert.NotEmpty(diagnostics);
        Assert.DoesNotContain("BuffersExtensions.Write", string.Concat(generated));
    }

    [Fact]
    public void Nested_template_generates_a_direct_render_call()
    {
        var (generated, diagnostics, errors) = Run(
            "public sealed partial class Child(string label) : IHtmlTemplate { public IEnumerable<HtmlElement> Build() => [label]; }",
            Template("""=> [H.Div[new Child("a") { }, new Child(model)]];"""));

        Assert.Empty(diagnostics);
        Assert.Empty(errors);
        Assert.Contains(generated, source => source.Contains("""if ((new Child("a") { }) is { } __t0) __t0.RenderTo(__writer);"""));
        Assert.Contains(generated, source => source.Contains("if ((new Child(model)) is { } __t1) __t1.RenderTo(__writer);"));
    }

    [Fact]
    public void Template_from_a_referenced_assembly_can_be_nested()
    {
        var library = CreateCompilation(
            "public sealed partial class Child(string label) : IHtmlTemplate { public IEnumerable<HtmlElement> Build() => [label]; }");
        CSharpGeneratorDriver.Create(new TemplateGenerator())
            .RunGeneratorsAndUpdateCompilation(library, out var libraryOutput, out _);

        var consumer = CreateCompilation(Template("""=> [H.Div[new Child("a")]];"""))
            .AddReferences(libraryOutput.ToMetadataReference());
        CSharpGeneratorDriver.Create(new TemplateGenerator())
            .RunGeneratorsAndUpdateCompilation(consumer, out var output, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.DoesNotContain(output.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("=> [H.Div[new System.Text.StringBuilder()]];")]
    public void Other_values_in_nesting_positions_are_still_reported(string build)
    {
        var (generated, diagnostics, _) = Run(Template(build));

        Assert.Equal("TDSL004", Assert.Single(diagnostics));
        Assert.DoesNotContain("BuffersExtensions.Write", string.Concat(generated));
    }

    private const string ChildlessTemplate =
        "public sealed partial class Child(string label) : IHtmlTemplate { public IEnumerable<HtmlElement> Build() => [label]; }";

    private const string ParentTemplate =
        "public sealed partial class Parent(string label) : IHtmlTemplate, IAcceptsChildren { public IEnumerable<HtmlElement> Build() => [H.Div[Children]]; }";

    [Fact]
    public void Passing_children_to_a_template_that_does_not_accept_them_is_reported()
    {
        var (_, diagnostics, errors) = Run(ChildlessTemplate, Template("""=> [new Child("a")["x"]];"""));

        Assert.Equal("TDSL004", Assert.Single(diagnostics));
        Assert.Contains("CS0021", errors);
    }

    [Fact]
    public void Children_property_only_exists_on_templates_that_opt_in()
    {
        var (_, _, errors) = Run(Template("=> [H.Div[Children]];"));

        Assert.Contains("CS0103", errors);
    }

    [Fact]
    public void Children_cannot_be_inspected()
    {
        var (generated, diagnostics, _) = Run("""
            public sealed partial class T : IHtmlTemplate, IAcceptsChildren
            {
                public IEnumerable<HtmlElement> Build() => [H.Div.Id(Children.Elements.Length.ToString())];
            }
            """);

        Assert.Equal("TDSL004", Assert.Single(diagnostics));
        Assert.DoesNotContain("BuffersExtensions.Write", string.Concat(generated));
    }

    [Fact]
    public void Children_in_a_template_compile_to_code_instead_of_a_tree()
    {
        var (generated, diagnostics, errors) = Run(ParentTemplate, ChildlessTemplate, Template("""=> [new Parent("a")["x", new Child(model)]];"""));

        Assert.Empty(diagnostics);
        Assert.Empty(errors);
        var source = Assert.Single(generated, s => s.Contains("partial class T"));
        Assert.Contains("""if ((new Parent("a")) is { } __t0) __t0.__RenderWithChildren(__writer, null!, new __Children0(this));""", source);
        Assert.Contains("private void __WriteChildren0(global::System.Buffers.IBufferWriter<byte> __writer, __Children0 __state)", source);
        Assert.Contains("\"x\"u8", source);
        Assert.Contains("if ((new Child(model)) is { } __t0) __t0.RenderTo(__writer);", source);
        Assert.DoesNotContain("=>\n", source);
    }

    [Fact]
    public void Invalid_children_of_a_template_are_reported()
    {
        var (_, diagnostics, _) = Run(ParentTemplate, Template("""=> [new Parent("a")[new System.Text.StringBuilder()]];"""));

        Assert.Equal("TDSL004", Assert.Single(diagnostics));
    }

    [Fact]
    public void Template_with_children_from_a_referenced_assembly_can_be_used()
    {
        var library = CreateCompilation(ParentTemplate);
        CSharpGeneratorDriver.Create(new TemplateGenerator())
            .RunGeneratorsAndUpdateCompilation(library, out var libraryOutput, out _);

        var consumer = CreateCompilation(Template("""=> [new Parent("a")["x"]["y"]];"""))
            .AddReferences(libraryOutput.ToMetadataReference());
        CSharpGeneratorDriver.Create(new TemplateGenerator())
            .RunGeneratorsAndUpdateCompilation(consumer, out var output, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.DoesNotContain(output.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
    }

    private const string AsyncChildTemplate =
        "public sealed partial class AsyncChild : IAsyncHtmlTemplate { public async System.Threading.Tasks.Task<IEnumerable<HtmlElement>> BuildAsync(System.Threading.CancellationToken ct) => [await System.Threading.Tasks.Task.FromResult(\"x\")]; }";

    [Theory]
    [InlineData("=> [new AsyncChild()];")]
    [InlineData("=> [H.Div[new AsyncChild()]];")]
    [InlineData("=> [H.Each(model, c => new AsyncChild())];")]
    [InlineData("=> [H.EachAsync(model, async c => H.Div)];")]
    [InlineData("=> [await new AsyncChild()];")]
    [InlineData("=> [H.Div[await new AsyncChild()]];")]
    [InlineData("=> [await H.EachAsync(model, async c => H.Div)];")]
    public void Async_content_in_a_sync_template_is_rejected_by_csharp(string build)
    {
        var (_, diagnostics, errors) = Run(AsyncChildTemplate, Template(build));

        Assert.Empty(diagnostics);
        Assert.NotEmpty(errors);
        Assert.DoesNotContain("CS1061", errors); // Follow-up errors from generated code would look like this.
    }

    private static string AsyncTemplate(string build) => $$"""
        public sealed partial class T(string model, System.Collections.Generic.IAsyncEnumerable<string> rows, IAsyncHtmlTemplate page) : IAsyncHtmlTemplate
        {
            public async System.Threading.Tasks.Task<IEnumerable<HtmlElement>> BuildAsync(System.Threading.CancellationToken ct) {{build}}
        }
        """;

    [Theory]
    [InlineData("=> [H.Div[new AsyncChild()]];")]
    [InlineData("=> [H.Div[H.EachAsync(rows, r => H.Div)]];")]
    [InlineData("=> [H.Div[page]];")]
    public void Forgetting_await_is_rejected_by_csharp(string build)
    {
        var (_, diagnostics, errors) = Run(AsyncChildTemplate, AsyncTemplate(build));

        // In brackets, what isn't a node goes to the Add method for templates, whose constraint an async one doesn't meet.
        Assert.Empty(diagnostics);
        Assert.Contains("CS0311", errors);
    }

    [Fact]
    public void Awaiting_a_sync_template_renders_it_in_one_go()
    {
        // Every template is content for an async template, so that one parameter can take a sync or an async page.
        var (generated, diagnostics, errors) = Run(ChildlessTemplate, AsyncTemplate("""=> [await new Child("x")];"""));

        Assert.Empty(diagnostics);
        Assert.Empty(errors);
        Assert.Contains(generated, source => source.Contains(".RenderTo(__writer);"));
    }

    [Theory]
    [InlineData("=> [await new AsyncChild()];")]
    [InlineData("=> [H.Div[await new AsyncChild()]];")]
    [InlineData("=> [(await (new AsyncChild()))];")]
    [InlineData("=> [H.Div[await H.EachAsync(rows, r => H.Div[r])]];")]
    [InlineData("=> [H.Div[await H.EachAsync(rows, async r => H.Div[await System.Threading.Tasks.Task.FromResult(r)])]];")]
    [InlineData("=> [H.Div[await page]];")]
    public void Awaited_async_content_compiles(string build)
    {
        var (generated, diagnostics, errors) = Run(AsyncChildTemplate, AsyncTemplate(build));

        Assert.Empty(diagnostics);
        Assert.Empty(errors);
        Assert.NotEmpty(generated);
    }

    [Fact]
    public void Async_templates_only_get_async_methods()
    {
        var (generated, diagnostics, errors) = Run(AsyncChildTemplate);

        Assert.Empty(diagnostics);
        Assert.Empty(errors);
        var source = Assert.Single(generated);
        Assert.Contains("__RenderAsync", source);
        Assert.DoesNotContain("void RenderTo(", source);
        Assert.DoesNotContain("string RenderToString(", source);
        Assert.Contains("FlushBufferedAsync", source);
    }

    [Theory]
    [InlineData("""{ await foreach (var x in Items()) { return []; } return []; } private static async System.Collections.Generic.IAsyncEnumerable<string> Items() { yield break; }""")]
    [InlineData("""=> System.Threading.Tasks.Task.FromResult<IEnumerable<HtmlElement>>([]);""")]
    [InlineData("""{ await System.Threading.Tasks.Task.FromResult(H.Div); return []; }""")]
    public void Unsupported_async_bodies_are_reported(string body)
    {
        var (generated, diagnostics, _) = Run($$"""
            public sealed partial class T : IAsyncHtmlTemplate
            {
                public {{(body.StartsWith('{') ? "async " : "")}}System.Threading.Tasks.Task<IEnumerable<HtmlElement>> BuildAsync(System.Threading.CancellationToken ct) {{body}}
            }
            """);

        Assert.Contains("TDSL004", diagnostics);
        Assert.DoesNotContain("BuffersExtensions.Write", string.Concat(generated));
    }

    [Fact]
    public void Sync_template_without_children_has_no_async_render_method_of_its_own()
    {
        var source = Assert.Single(Run(Template("=> [H.Div];")).Generated);

        Assert.DoesNotContain("__RenderAsync", source);
        Assert.DoesNotContain("async", source);
        Assert.DoesNotContain("__RenderWithChildren", source);
    }

    [Fact]
    public void Sync_template_with_children_can_wait_for_them_but_renders_alone_in_one_go()
    {
        var source = Assert.Single(Run(ParentTemplate).Generated);

        Assert.Contains("__RenderWithChildrenAsync<__TChildren>(global::System.IO.Pipelines.PipeWriter __writer", source);
        Assert.DoesNotContain("__RenderAsync", source);
        Assert.Contains("__RenderWithChildren(writer, null!, default(global::TemplateDsl.NoChildren));", source);
    }

    [Fact]
    public void Generated_file_name_is_not_tied_to_one_method()
    {
        var compilation = CreateCompilation("namespace My.App; " + Template("=> [H.Div];"));

        var result = CSharpGeneratorDriver.Create(new TemplateGenerator()).RunGenerators(compilation).GetRunResult();

        Assert.EndsWith("My.App.T.Rendering.g.cs", Assert.Single(result.GeneratedTrees).FilePath);
    }

    [Theory]
    [InlineData("""{ foreach (var c in model) { yield return H.Div; } }""")]
    [InlineData("""{ foreach (var c in model) yield return H.Div; yield return "end"; }""")]
    [InlineData("""{ foreach (var (a, b) in new[] { (model, 1) }) { yield return a; } }""")]
    [InlineData("""{ for (var i = 0; i < model.Length; i++) { if (i == 1) continue; if (i == 3) break; yield return $"{i}"; } }""")]
    [InlineData("""{ while (model.Length > 5) { yield return H.Div; break; } }""")]
    [InlineData("""{ foreach (var c in model) { return [H.Div]; } return []; }""")]
    [InlineData("""=> [H.Each(model, c => H.Div[$"{c}"])];""")]
    [InlineData("""=> [H.Div[H.Each(model, (char c) => c == 'x' ? null : H.Div)]];""")]
    [InlineData("""=> [H.Div[H.Each(model.Split(','), s => s), H.Each(model, c => H.Each(model, d => H.Div))]];""")]
    [InlineData("""=> [H.Each(model, c => new Item($"{c}") { Css = "x" })];""")]
    public void Loops_generate_code_that_compiles(string build)
    {
        var item = "public sealed partial class Item(string name) : IHtmlTemplate { public string? Css { get; init; } public IEnumerable<HtmlElement> Build() => [name]; }";

        var (generated, diagnostics, errors) = Run(item, Template(build));

        Assert.Empty(diagnostics);
        Assert.Empty(errors);
        Assert.NotEmpty(generated);
    }

    [Fact]
    public void Each_over_items_whose_members_are_used_with_a_template_body_is_translated()
    {
        var (_, diagnostics, errors) = Run(
            "public sealed record Row(string Name, int Size);",
            ChildlessTemplate,
            "public sealed partial class Rows(List<Row> rows) : IHtmlTemplate { public IEnumerable<HtmlElement> Build() => [H.Div[H.Each(rows, row => new Child(row.Name))], H.Each(rows, row => row.Size > 0 ? new Child(row.Name) : row.Name)]; }");

        Assert.Empty(diagnostics);
        Assert.Empty(errors);
    }

    [Fact]
    public void Custom_element_type_is_reported()
    {
        var (generated, diagnostics, _) = Run(
            "public sealed class Custom : HtmlElement;",
            Template("=> [H.Div[new Custom()]];"));

        Assert.Equal("TDSL004", Assert.Single(diagnostics));
        Assert.DoesNotContain("BuffersExtensions.Write", string.Concat(generated));
    }

    [Theory]
    [InlineData("""{ do { yield return "x"; } while (false); }""")]
    [InlineData("{ var unused = H.Div; return []; }")]
    [InlineData("=> new List<HtmlElement>();")]
    public void A_build_that_cannot_be_translated_only_reports_the_reason(string build)
    {
        var (_, diagnostics, errors) = Run(Template(build));

        Assert.Equal("TDSL004", Assert.Single(diagnostics));
        Assert.Empty(errors);
    }

    [Fact]
    public void A_failed_template_with_children_only_reports_the_reason()
    {
        var (_, diagnostics, errors) = Run("""
            public sealed partial class Box : IHtmlTemplate, IAcceptsChildren
            {
                public IEnumerable<HtmlElement> Build() { lock (this) { return [H.Div [ Children ]]; } }
            }
            """);

        Assert.Equal("TDSL004", Assert.Single(diagnostics));
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("{ model.ToString(); return []; }")]
    [InlineData("{ var type = typeof(HtmlElement); return []; }")]
    [InlineData("{ var nested = new T(model); return [H.Div [ nested ]]; }")]
    [InlineData("{ var x = 1; x++; x += 2; yield return H.Div [ x ]; }")]
    public void Statements_without_nodes_are_allowed(string build)
    {
        var (_, diagnostics, errors) = Run(Template(build));

        Assert.Empty(diagnostics);
        Assert.Empty(errors);
    }

    [Fact]
    public void Output_is_cached_when_an_unrelated_file_changes()
    {
        var compilation = CreateCompilation(Template("=> [H.Div];"));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new TemplateGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);
        driver = driver.RunGenerators(compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("class Unrelated;")));

        var outputs = driver.GetRunResult().Results[0].TrackedOutputSteps
            .SelectMany(step => step.Value)
            .SelectMany(step => step.Outputs)
            .ToArray();

        Assert.NotEmpty(outputs);
        Assert.All(outputs, output => Assert.Equal(IncrementalStepRunReason.Cached, output.Reason));
    }

    [Theory]
    [InlineData("""H.Td.ColSpan("wide")""")]
    [InlineData("""H.Div.Class(5)""")]
    [InlineData("""H.A.Href(5)""")]
    [InlineData("""H.Div.Draggable("yes")""")]
    [InlineData("""H.InputCheckbox.Min(3)""")]
    [InlineData("""H.InputText.Step(1)""")]
    [InlineData("""H.InputNumber.Value("5")""")]
    [InlineData("""H.InputDate.Value(System.DateTime.Now)""")]
    [InlineData("""H.Input.Type("number")""")]
    public void A_value_of_the_wrong_type_does_not_compile(string node) =>
        Assert.NotEmpty(Run(Template($"=> [{node}];")).CompileErrors);

    [Fact]
    public void A_constant_integer_in_an_attribute_is_plain_text()
    {
        var source = Assert.Single(Run(Template("=> [H.Td.ColSpan(2)];")).Generated);

        Assert.Contains("colspan=\\\"2\\\"", source);
        Assert.DoesNotContain("TemplateValues", source);
    }

    [Fact]
    public void A_constant_enum_is_plain_text()
    {
        var source = Assert.Single(Run(Template("=> [H.Input.Type(InputType.DatetimeLocal)];")).Generated);

        Assert.Contains("type=\\\"datetime-local\\\"", source);
        Assert.DoesNotContain("switch", source);
    }
}
