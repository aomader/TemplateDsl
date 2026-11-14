using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using TemplateDsl.Generators;
using Xunit;

namespace Tests;

/// <summary>
/// TDSL012 explains the error of an element that a name of the app's own hides, which C# reports without mentioning
/// the element. It is only reported where that use fails, so valid code with the same names is left alone.
/// </summary>
public class ShadowedElementTests
{
    private const string Header = """
        using System.Collections.Generic;
        using TemplateDsl;
        using TemplateDsl.Html;
        using static TemplateDsl.Html.Elements;
        using static Test.Templates;

        public sealed partial class Button(string text) : IHtmlTemplate
        {
            public IEnumerable<HtmlElement> Build() => [P [ text ]];
        }

        public sealed class Label;

        namespace Footer { }

        """;

    private static async Task<(Diagnostic[] Analyzer, Diagnostic[] Generator, Diagnostic[] GeneratedErrors)> AnalyzeAsync(string source, string header = Header)
    {
        var compilation = CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText(header + source)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        CSharpGeneratorDriver.Create(new TemplateGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generator);

        var diagnostics = await output.WithAnalyzers([new ShadowedElementAnalyzer()]).GetAnalyzerDiagnosticsAsync();
        var generatedErrors = output.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Location.SourceTree?.FilePath.EndsWith(".g.cs", StringComparison.Ordinal) == true)
            .ToArray();

        return (diagnostics.ToArray(), generator.ToArray(), generatedErrors);
    }

    private static string Page(string build, string members = "", string parameters = "") => $$"""
        public sealed partial class Page({{parameters}}) : IHtmlTemplate
        {
            {{members}}
            public IEnumerable<HtmlElement> Build() {{build}}
        }
        """;

    [Theory]
    [InlineData("=> [Button [ \"x\" ]];", "", "", "'Button' is the template 'Button' here")]
    [InlineData("=> [Div [ Button ]];", "", "", "'Button' is the template 'Button' here")]
    [InlineData("=> [Label.For(\"x\") [ \"y\" ]];", "", "", "'Label' is the type 'Label' here")]
    [InlineData("=> [Footer [ \"x\" ]];", "", "", "'Footer' is the namespace 'Footer' here")]
    [InlineData("=> [Title [ Title ]];", "public string Title => \"t\";", "", "'Title' is the property 'Title' here")]
    [InlineData("=> [Header [ \"x\" ]];", "private readonly int Header = 1;", "", "'Header' is the field 'Header' here")]
    [InlineData("=> [Text(\"x\")];", "private static string Text(int number) => \"\";", "", "'Text' is the method 'Text' here")]
    [InlineData("{ var Main = 1; return [Main [ \"x\" ]]; }", "", "", "'Main' is the local variable 'Main' here")]
    [InlineData("=> [Data [ \"x\" ]];", "", "string Data", "'Data' is the parameter 'Data' here")]
    [InlineData("=> [Footer [ \"x\" ]];", "private Button Footer => new(\"x\");", "", "'Footer' is the property 'Footer' here")]
    public async Task An_element_hidden_by_a_name_of_your_own_is_explained(string build, string members, string parameters, string expected)
    {
        var (diagnostics, generator, generatedErrors) = await AnalyzeAsync(Page(build, members, parameters));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("TDSL012", diagnostic.Id);

        // The translator leaves it to C# and TDSL012, instead of an error about what the name is.
        Assert.DoesNotContain(generator, diagnostic => diagnostic.Id == "TDSL004");
        Assert.Empty(generatedErrors);
        Assert.StartsWith(expected, diagnostic.GetMessage(CultureInfo.InvariantCulture));
        Assert.EndsWith("write Elements." + expected.Split('\'')[1] + " for the element, or rename what hides it", diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("=> [Elements.Title [ Title ]];", "public string Title => \"t\";")]           // the element, written with its class
    [InlineData("=> [P [ Title ]];", "public string Title => \"t\";")]                       // the property, as text
    [InlineData("=> [P.Id(Title.ToUpperInvariant()) [ \"x\" ]];", "public string Title => \"t\";")]   // the property, in a chain that works
    [InlineData("=> [Div [ Button(\"Save\") ]];", "")]                                       // the template, created by its factory
    [InlineData("=> [P [ Data[0] ]];", "private readonly int[] Data = [1];")]                 // an array, indexed as it can be
    [InlineData("=> [Div [ \"x\" ]]; private static object Other() => Button [ \"x\" ];", "")]   // outside of Build
    public async Task Names_of_your_own_that_are_used_as_what_they_are_are_left_alone(string build, string members) =>
        Assert.Empty((await AnalyzeAsync(Page(build, members))).Analyzer);

    [Fact]
    public async Task A_template_named_like_an_element_that_takes_the_children_is_written_as_the_template()
    {
        // The brackets of a template are generated, so the generator can't see that they work here. They do.
        var (analyzer, generator, generatedErrors) = await AnalyzeAsync("""
            public sealed partial class Card : IHtmlTemplate, IAcceptsChildren
            {
                public IEnumerable<HtmlElement> Build() => [Section [ Children ]];
            }

            """ + Page("=> [Footer [ \"x\" ]];", "private Card Footer => new();"));

        Assert.Empty(analyzer);
        Assert.Empty(generator);
        Assert.Empty(generatedErrors);
    }

    [Fact]
    public async Task Without_the_elements_imported_there_is_nothing_they_could_be_hidden_by()
    {
        var header = Header.Replace("using static TemplateDsl.Html.Elements;", "using H = TemplateDsl.Html.Elements;", StringComparison.Ordinal);

        var (analyzer, generator, _) = await AnalyzeAsync(Page("=> [H.Div [ Button [ \"x\" ] ]];"), header);

        // Nothing hides an element here, so the translator reports what it found.
        Assert.Empty(analyzer);
        Assert.Contains(generator, diagnostic => diagnostic.Id == "TDSL004");
    }

    [Fact]
    public async Task Outside_of_a_template_there_are_no_elements_to_hide() =>
        Assert.Empty((await AnalyzeAsync("public static class Helper { public static object Build() => Button [ \"x\" ]; }")).Analyzer);
}
