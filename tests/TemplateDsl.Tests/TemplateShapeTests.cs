using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TemplateDsl.Generators;
using Xunit;

namespace Tests;

public abstract partial class BasePage : IHtmlTemplate
{
    public abstract string Name { get; }

    public virtual IHtmlTemplate? Extra => null;
}

public sealed partial class FirstPage : BasePage
{
    public override string Name => "first";

    public override IHtmlTemplate? Extra => new SmallPart();

    public override IEnumerable<HtmlElement> Build() => [H.P [ Name ]];
}

public sealed partial class SmallPart : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [H.Span [ "extra" ]];
}

public sealed partial class BaseLayout(BasePage page) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [H.Div [ page.Name, page.Extra, page ]];
}

public readonly partial record struct ShapeRow(string Title, int Count) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [H.Li [ Title, " ", Count ]];
}

public partial struct ShapeStruct(string text) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [H.Span [ text ]];
}

public partial record ShapeRecord(string Label) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [H.B [ Label ]];
}

/// <summary>Not sealed: what it writes can be changed by a class derived from it.</summary>
public partial class ShapeGreeting(string name) : IHtmlTemplate
{
    protected virtual string Word => "Hi";

    public virtual IEnumerable<HtmlElement> Build() => [H.P [ Word, " ", name ]];
}

/// <summary>Has no Build of its own, so it renders with the one of its base class.</summary>
public sealed partial class ShapeShout(string name) : ShapeGreeting(name)
{
    protected override string Word => "HEY";
}

public sealed partial class ShapeFancy(string name) : ShapeGreeting(name)
{
    public override IEnumerable<HtmlElement> Build() => [H.Em [ "fancy" ]];
}

public sealed partial class ShapeList(IEnumerable<ShapeRow> rows, ShapeRow? extra) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [H.Ul [ H.Each(rows, row => row), extra, new ShapeStruct("s") ]];
}

/// <summary>Takes content that needs no context.</summary>
public sealed partial class ShapeSlot(IHtmlContent? content) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [H.Div [ content ]];
}

/// <summary>Takes content that renders with a TestContext: one that needs it, less of it, or none.</summary>
public sealed partial class ShapeContentLayout(IHtmlContent<TestContext> content) : IHtmlTemplate<TestContext>
{
    public IEnumerable<HtmlElement> Build(TestContext context) => [H.Main [ content ]];
}

/// <summary>Takes the content as a type parameter, which keeps its own type.</summary>
public sealed partial class ShapeGenericLayout<TContent>(TContent content) : IHtmlTemplate<TestContext>
    where TContent : IHtmlContent<TestContext>
{
    public IEnumerable<HtmlElement> Build(TestContext context) => [H.Main [ content ]];
}

/// <summary>Repeats content of an interface type with Each.</summary>
public sealed partial class ShapeContentList(IEnumerable<IHtmlContent> items) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [H.Ul [ H.Each(items, item => item) ]];
}

public sealed partial class ShapeGreetingPart : IHtmlTemplate<IHasGreeting>
{
    public IEnumerable<HtmlElement> Build(IHasGreeting context) => [H.I [ context.Greeting ]];
}

public sealed partial class ShapeContextPart : IHtmlTemplate<TestContext>
{
    public IEnumerable<HtmlElement> Build(TestContext context) => [H.B [ context.User ]];
}

public class TemplateShapeTests
{
    private const string Usings = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using TemplateDsl;
        using TemplateDsl.Html;
        using FormatSamples;

        """;

    private static CSharpCompilation Compile(string source) =>
        CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText(Usings + source)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static (string[] Generated, string[] Diagnostics, string[] Errors) Run(string source)
    {
        CSharpGeneratorDriver.Create(new TemplateGenerator()).RunGeneratorsAndUpdateCompilation(Compile(source), out var output, out var diagnostics);

        return (
            output.SyntaxTrees.Skip(1).Select(tree => tree.ToString()).ToArray(),
            diagnostics.Select(d => d.Id).ToArray(),
            output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id).ToArray());
    }

    [Fact]
    public void Templates_are_nodes_also_through_an_abstract_contract()
    {
        Assert.Equal("<div>first<span>extra</span><p>first</p></div>", new BaseLayout(new FirstPage()).RenderToString());
    }

    [Fact]
    public void Structs_and_records_are_templates()
    {
        Assert.Equal("<li>a 1</li>", new ShapeRow("a", 1).RenderToString());
        Assert.Equal("<span>s</span>", new ShapeStruct("s").RenderToString());
        Assert.Equal("<b>r</b>", new ShapeRecord("r").RenderToString());
    }

    [Fact]
    public void Struct_templates_are_nested_also_when_they_may_be_null()
    {
        Assert.Equal(
            "<ul><li>x 1</li><li>y 2</li><li>m 3</li><span>s</span></ul>",
            new ShapeList([new("x", 1), new("y", 2)], new ShapeRow("m", 3)).RenderToString());
        Assert.Equal("<ul><span>s</span></ul>", new ShapeList([], null).RenderToString());
    }

    [Fact]
    public void A_template_that_isnt_sealed_can_be_derived_from()
    {
        Assert.Equal("<p>Hi g</p>", new ShapeGreeting("g").RenderToString());
        Assert.Equal("<p>HEY s</p>", new ShapeShout("s").RenderToString());
        Assert.Equal("<p>HEY s</p>", ((ShapeGreeting)new ShapeShout("s")).RenderToString());
        Assert.Equal("<em>fancy</em>", ((ShapeGreeting)new ShapeFancy("f")).RenderToString());
    }

    [Fact]
    public void Content_renders_where_it_is_placed()
    {
        Assert.Equal("<div><span>extra</span></div>", new ShapeSlot(new SmallPart()).RenderToString());
        Assert.Equal("<div><li>a 1</li></div>", new ShapeSlot(new ShapeRow("a", 1)).RenderToString());
        Assert.Equal("<div></div>", new ShapeSlot(null).RenderToString());
    }

    [Fact]
    public void Content_with_a_context_takes_templates_that_need_it_less_of_it_or_none()
    {
        var context = new TestContext("Hello", "Ann");

        Assert.Equal("<main><b>Ann</b></main>", new ShapeContentLayout(new ShapeContextPart()).RenderToString(context));
        Assert.Equal("<main><i>Hello</i></main>", new ShapeContentLayout(new ShapeGreetingPart()).RenderToString(context));
        Assert.Equal("<main><span>extra</span></main>", new ShapeContentLayout(new SmallPart()).RenderToString(context));
        Assert.Equal("<main><li>a 1</li></main>", new ShapeGenericLayout<ShapeRow>(new ShapeRow("a", 1)).RenderToString(context));
        Assert.Equal("<main><b>Ann</b></main>", new ShapeGenericLayout<ShapeContextPart>(new ShapeContextPart()).RenderToString(context));
    }

    [Fact]
    public void Each_repeats_content_of_an_interface_type()
    {
        Assert.Equal("<ul><span>extra</span><li>a 1</li></ul>", new ShapeContentList([new SmallPart(), new ShapeRow("a", 1)]).RenderToString());
    }

    [Fact]
    public void A_node_value_in_build_is_reported_with_what_to_do_instead()
    {
        var compilation = Compile("public sealed partial class T(HtmlElement content) : IHtmlTemplate { public IEnumerable<HtmlElement> Build() => [H.Main [ content ]]; }");
        var result = CSharpGeneratorDriver.Create(new TemplateGenerator()).RunGenerators(compilation).GetRunResult();

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("TDSL004", diagnostic.Id);
        Assert.Contains("IHtmlContent<TContext>", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Content_that_needs_another_context_does_not_compile()
    {
        var (_, _, errors) = Run("""
            public sealed record A;
            public sealed record B;
            public sealed partial class NeedsB : IHtmlTemplate<B> { public IEnumerable<HtmlElement> Build(B b) => []; }
            public sealed partial class Layout(IHtmlContent<A> content) : IHtmlTemplate<A> { public IEnumerable<HtmlElement> Build(A a) => [H.Main [ content ]]; }
            public static class Use { public static object Make() => new Layout(new NeedsB()); }
            """);

        Assert.Contains("CS1503", errors);
    }

    [Fact]
    public void Content_that_needs_a_context_in_a_template_without_one_is_reported()
    {
        var (_, diagnostics, _) = Run("""
            public sealed record A;
            public sealed partial class Layout(IHtmlContent<A> content) : IHtmlTemplate { public IEnumerable<HtmlElement> Build() => [H.Main [ content ]]; }
            """);

        Assert.Equal("TDSL008", Assert.Single(diagnostics));
    }

    [Fact]
    public void Only_sync_templates_convert_to_a_node()
    {
        static bool Converts(Type type) => type.GetMethods().Any(m => m.Name == "op_Implicit" && m.ReturnType == typeof(HtmlElement));

        Assert.True(Converts(typeof(SmallPart)));
        Assert.True(Converts(typeof(ShapeRow)));
        Assert.False(Converts(typeof(AsyncContent)));
        Assert.False(typeof(SmallPart).IsAssignableTo(typeof(HtmlElement)));
    }

    [Fact]
    public void An_abstract_contract_gets_abstract_render_methods_and_build()
    {
        var (generated, diagnostics, errors) = Run("""
            public abstract partial class Contract : IHtmlTemplate { public abstract string Name { get; } }
            public sealed partial class T : Contract
            {
                public override string Name => "x";
                public override IEnumerable<HtmlElement> Build() => [Name];
            }
            """);

        Assert.Empty(diagnostics);
        Assert.Empty(errors);
        var contract = Assert.Single(generated, source => source.Contains("partial class Contract"));
        Assert.Contains("public abstract global::System.Collections.Generic.IEnumerable<global::TemplateDsl.Html.HtmlElement> Build();", contract);
        Assert.Contains("public abstract void RenderTo(", contract);
        Assert.Contains("public override void RenderTo(", Assert.Single(generated, source => source.Contains("partial class T")));
    }

    [Fact]
    public void An_abstract_template_with_a_build_method_renders_with_virtual_methods()
    {
        var (generated, diagnostics, errors) = Run("""
            public abstract partial class Base : IHtmlTemplate
            {
                public IEnumerable<HtmlElement> Build() => [H.Div];
            }
            """);

        Assert.Empty(diagnostics);
        Assert.Empty(errors);
        Assert.Contains("public virtual void RenderTo(", Assert.Single(generated));
    }

    [Fact]
    public void Partial_build_method_uses_its_implementing_declaration()
    {
        var (generated, diagnostics, errors) = Run("""
            public sealed partial class T : IHtmlTemplate
            {
                public partial IEnumerable<HtmlElement> Build();
                public partial IEnumerable<HtmlElement> Build() => [H.Div];
            }
            """);

        Assert.Empty(diagnostics);
        Assert.Empty(errors);
        Assert.Contains("<div></div>", Assert.Single(generated));
    }

    [Theory]
    [InlineData("""public sealed partial record T(string A) : IHtmlTemplate { public IEnumerable<HtmlElement> Build() => [A]; }""")]
    [InlineData("""public readonly partial record struct T(string A) : IHtmlTemplate { public IEnumerable<HtmlElement> Build() => [A]; }""")]
    [InlineData("""public partial struct T : IHtmlTemplate { public IEnumerable<HtmlElement> Build() => []; }""")]
    [InlineData("""public partial class T : IHtmlTemplate { public IEnumerable<HtmlElement> Build() => []; }""")]
    [InlineData("""public sealed partial class T : ITemplate<HtmlElement> { public IEnumerable<HtmlElement> Build() => []; }""")]
    public void Classes_structs_and_records_are_templates(string source)
    {
        var (generated, diagnostics, errors) = Run(source);

        Assert.Empty(diagnostics);
        Assert.Empty(errors);
        Assert.Single(generated);
    }

    [Theory]
    [InlineData("""public static partial class T : IHtmlTemplate { public static IEnumerable<HtmlElement> Build() => []; }""")]
    [InlineData("""public sealed partial class T : IHtmlTemplate { public static IEnumerable<HtmlElement> Build() => []; }""")]
    [InlineData("""public sealed partial class T : IHtmlTemplate, IAsyncHtmlTemplate { public IEnumerable<HtmlElement> Build() => []; }""")]
    public void Shapes_that_cant_implement_a_template_do_not_compile(string source)
    {
        Assert.NotEmpty(Run(source).Errors);
    }

    [Theory]
    [InlineData("IHtmlTemplate, IAsyncTemplate<HtmlElement>")]
    [InlineData("IJsonTemplate, ITemplate<RssNode>")]
    public void Implementing_more_than_one_template_is_reported(string bases)
    {
        var (_, diagnostics, _) = Run($"public sealed partial class T : {bases};");

        Assert.Equal("TDSL005", Assert.Single(diagnostics));
    }

    /// <summary>The generated members are there anyway, so that the reason is the only error, and not what is missing because of it.</summary>
    [Theory]
    [InlineData("public sealed partial class T : IHtmlTemplate { public IEnumerable<HtmlElement> Build() => new List<HtmlElement>(); }", "TDSL004")]
    [InlineData("public sealed partial class T : IHtmlTemplate { public IEnumerable<HtmlElement> Build() => [new Card() [ \"x\" ]]; }", null)]
    [InlineData("public partial struct T : IHtmlTemplate { public IEnumerable<HtmlElement> Build() => [new Card() [ \"x\" ]]; }", null)]
    public void A_template_with_an_error_has_no_errors_about_missing_members(string source, string? expected)
    {
        var (_, diagnostics, errors) = Run(source + """

            public sealed partial class Card : IHtmlTemplate, IAcceptsChildren { public IEnumerable<HtmlElement> Build() => [H.Div [ Children ]]; }
            public sealed partial class Parent : IHtmlTemplate { public IEnumerable<HtmlElement> Build() => [new T()]; }
            """);

        Assert.Equal(expected is null ? [] : [expected], diagnostics);
        Assert.Empty(errors);
    }

    [Fact]
    public void A_template_without_build_has_only_the_compiler_error_about_it()
    {
        var (_, diagnostics, errors) = Run("public sealed partial class T : IHtmlTemplate;");

        Assert.Empty(diagnostics);
        Assert.Equal("CS0535", Assert.Single(errors));
    }

    [Fact]
    public void Each_of_a_factory_class_in_the_same_project_is_explained()
    {
        var (_, diagnostics, _) = Run("""
            [TemplateFormat(typeof(string))] public abstract partial class F : TemplateNode;
            public interface IFTemplate : ITemplate<F>;
            [TemplateTags(typeof(F))] public static partial class Fs;
            public sealed partial class T(string[] items) : IFTemplate
            {
                public IEnumerable<F> Build() => [Fs.Each(items, i => i)];
            }
            """);

        Assert.Contains("TDSL004", diagnostics);
    }

    [Fact]
    public void Template_property_of_another_format_does_not_compile()
    {
        var (_, _, errors) = Run("""
            public sealed partial class T(JsonNode page) : IHtmlTemplate
            {
                public IEnumerable<HtmlElement> Build() => [H.Div [ page ]];
            }
            """);

        Assert.NotEmpty(errors);
    }
}
