using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TemplateDsl.Generators;
using Xunit;

namespace Tests;

public abstract class BasePage : HtmlTemplate
{
    public abstract string Name { get; }

    public virtual HtmlTemplate? Extra => null;
}

public sealed partial class FirstPage : BasePage
{
    public override string Name => "first";

    public override HtmlTemplate? Extra => new SmallPart();

    public override IEnumerable<HtmlElement> Build() => [H.P [ Name ]];
}

public sealed partial class SmallPart : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() => [H.Span [ "extra" ]];
}

public sealed partial class BaseLayout(BasePage page) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() => [H.Div [ page.Name, page.Extra, page ]];
}

public class TemplateBaseTests
{
    private const string Usings = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using TemplateDsl;
        using TemplateDsl.Html;
        using FormatSamples;

        """;

    private static (string[] Generated, string[] Diagnostics, string[] Errors) Run(string source, string usings = Usings)
    {
        var compilation = CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText(usings + source)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        CSharpGeneratorDriver.Create(new TemplateGenerator()).RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

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
    public void A_template_is_an_element()
    {
        Assert.IsAssignableFrom<HtmlElement>(new SmallPart());
        Assert.False(typeof(AsyncContent).IsAssignableTo(typeof(HtmlElement)));
    }

    [Fact]
    public void Generated_code_has_no_conversion()
    {
        Assert.DoesNotContain(typeof(SmallPart).GetMethods(), m => m.Name == "op_Implicit" && m.DeclaringType == typeof(SmallPart));
    }

    [Fact]
    public void Abstract_contract_without_build_generates_nothing_and_needs_nothing()
    {
        var (generated, diagnostics, errors) = Run("""
            public abstract class Contract : HtmlTemplate { public abstract string Name { get; } }
            public sealed partial class T : Contract
            {
                public override string Name => "x";
                public override IEnumerable<HtmlElement> Build() => [Name];
            }
            """);

        Assert.Empty(diagnostics);
        Assert.Empty(errors);
        Assert.Single(generated);
    }

    [Fact]
    public void Abstract_template_with_a_build_method_is_reported()
    {
        var (generated, diagnostics, _) = Run("""
            public abstract partial class Base : HtmlTemplate
            {
                public override IEnumerable<HtmlElement> Build() => [H.Div];
            }
            """);

        Assert.Equal("TDSL002", Assert.Single(diagnostics));
        Assert.Empty(generated);
    }

    [Fact]
    public void Partial_build_method_uses_its_implementing_declaration()
    {
        var (generated, diagnostics, errors) = Run("""
            public sealed partial class T : HtmlTemplate
            {
                public override partial IEnumerable<HtmlElement> Build();
                public override partial IEnumerable<HtmlElement> Build() => [H.Div];
            }
            """);

        Assert.Empty(diagnostics);
        Assert.Empty(errors);
        Assert.Contains("<div></div>", Assert.Single(generated));
    }

    [Theory]
    [InlineData("""public sealed partial record T(string A) : HtmlTemplate { public override IEnumerable<HtmlElement> Build() => []; }""")]
    [InlineData("""public partial struct T : HtmlTemplate { public IEnumerable<HtmlElement> Build() => []; }""")]
    [InlineData("""public static partial class T : HtmlTemplate { public static IEnumerable<HtmlElement> Build() => []; }""")]
    [InlineData("""public sealed partial class T : HtmlTemplate { public static IEnumerable<HtmlElement> Build() => []; }""")]
    [InlineData("""public sealed partial class T : HtmlTemplate, AsyncHtmlTemplate { public override IEnumerable<HtmlElement> Build() => []; }""")]
    public void Shapes_that_cant_derive_or_override_do_not_compile(string source)
    {
        Assert.NotEmpty(Run(source).Errors);
    }

    [Fact]
    public void Implementing_an_interface_without_a_base_class_is_reported()
    {
        var (generated, diagnostics, _) = Run("""
            public sealed partial class T : ITemplate<HtmlElement>
            {
                public IEnumerable<HtmlElement> Build() => [];
            }
            """);

        Assert.Equal("TDSL006", Assert.Single(diagnostics));
        Assert.Empty(generated);
    }

    [Theory]
    [InlineData("HtmlTemplate, IAsyncTemplate<HtmlElement>")]
    [InlineData("JsonTemplate, ITemplate<RssNode>")]
    public void Implementing_more_than_one_template_is_reported(string bases)
    {
        var (generated, diagnostics, _) = Run($"public sealed partial class T : {bases};");

        Assert.Equal("TDSL005", Assert.Single(diagnostics));
        Assert.Empty(generated);
    }

    [Theory]
    [InlineData("[TemplateBase] public partial class B : HtmlElement, ITemplate<HtmlElement>;")]
    [InlineData("[TemplateBase] public abstract class B : HtmlElement, ITemplate<HtmlElement>;")]
    [InlineData("[TemplateBase] public abstract partial class B : HtmlElement;")]
    public void Invalid_template_bases_are_reported(string source)
    {
        var compilation = CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText(Usings + source)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var result = CSharpGeneratorDriver.Create(new FormatGenerator()).RunGenerators(compilation).GetRunResult();

        Assert.Equal("TDSL006", Assert.Single(result.Diagnostics).Id);
    }

    [Fact]
    public void Each_of_a_factory_class_in_the_same_project_is_explained()
    {
        var (_, diagnostics, _) = Run("""
            [TemplateFormat(typeof(string))] public abstract partial class F : TemplateNode;
            [TemplateBase] public abstract partial class FT : F, ITemplate<F>;
            [TemplateTags(typeof(F))] public static partial class Fs;
            public sealed partial class T(string[] items) : FT
            {
                public override IEnumerable<F> Build() => [Fs.Each(items, i => i)];
            }
            """);

        Assert.Contains("TDSL004", diagnostics);
    }

    [Fact]
    public void Template_property_of_another_format_does_not_compile()
    {
        var (_, _, errors) = Run("""
            public sealed partial class T(JsonTemplate page) : HtmlTemplate
            {
                public override IEnumerable<HtmlElement> Build() => [H.Div [ page ]];
            }
            """);

        Assert.NotEmpty(errors);
    }
}
