using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TemplateDsl.Generators;
using Xunit;

namespace Tests;

/// <summary>The class of the template factories: where it goes, which templates and constructors get a factory, and how the translator recognizes their calls.</summary>
public class TemplateFactoryGeneratorTests
{
    private const string Usings = "using System; using System.Collections.Generic; using System.Diagnostics.CodeAnalysis; using TemplateDsl; using TemplateDsl.Html; using H = TemplateDsl.Html.Elements;\n";

    private sealed record Result(string? Factories, string? FactoriesPath, Diagnostic[] Diagnostics, Diagnostic[] Errors);

    private static Result Run(string source, Dictionary<string, string>? properties = null)
    {
        var compilation = CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText(Usings + source)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        CSharpGeneratorDriver.Create([new TemplateGenerator().AsSourceGenerator()], optionsProvider: new BuildProperties(properties ?? []))
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        var factories = output.SyntaxTrees.FirstOrDefault(tree => !GeneratorTests.IsRenderFile(tree));
        var errors = output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();

        return new Result(factories?.ToString(), factories?.FilePath, diagnostics.ToArray(), errors);
    }

    private static string Template(string name, string parameters = "", string modifiers = "public sealed partial class") =>
        $"{modifiers} {name}({parameters}) : IHtmlTemplate {{ public IEnumerable<HtmlElement> Build() => [H.P [ \"{name}\" ]]; }}\n";

    [Fact]
    public void The_class_is_called_Templates_and_goes_in_the_root_namespace()
    {
        var result = Run("namespace My.App.Views { " + Template("Card", "string title") + " }", new() { ["RootNamespace"] = "My.App" });

        Assert.Empty(result.Errors);
        Assert.EndsWith("My.App.Templates.Factories.g.cs", result.FactoriesPath);
        Assert.Contains("namespace My.App;", result.Factories);
        Assert.Contains("public static partial class Templates", result.Factories);
        Assert.Contains("public static global::My.App.Views.Card Card(string title) =>\n        new(title);", result.Factories);
    }

    [Fact]
    public void Without_a_root_namespace_the_class_goes_in_the_namespace_named_like_the_assembly() =>
        Assert.Contains("namespace Test;", Run(Template("Card")).Factories);

    [Theory]
    [InlineData("Make", "namespace My.App;", "class Make")]
    [InlineData("Ui.Make", "namespace Ui;", "class Make")]
    public void The_property_names_the_class_in_the_root_namespace_or_its_own(string name, string expectedNamespace, string expectedClass)
    {
        var result = Run(Template("Card"), new() { ["RootNamespace"] = "My.App", ["TemplateDslTemplatesClass"] = name });

        Assert.Empty(result.Errors);
        Assert.Contains(expectedNamespace, result.Factories);
        Assert.Contains(expectedClass, result.Factories);
    }

    [Theory]
    [InlineData("namespace Test.Templates { public class X; }", "a namespace has that name")]
    [InlineData("namespace Test { public class Templates; }", "a type that isn't a static class has that name")]
    [InlineData("", "it isn't a valid name", "1Templates")]
    public void A_name_that_is_taken_or_invalid_is_reported_and_gets_no_class(string declaration, string reason, string? name = null)
    {
        var properties = name is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["TemplateDslTemplatesClass"] = name };
        var result = Run(declaration + Template("Card"), properties);

        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Id == "TDSL010");
        Assert.Contains(reason, diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Null(result.Factories);
    }

    [Fact]
    public void A_static_class_with_the_name_is_the_projects_own_part_of_it()
    {
        var result = Run("namespace Test { public static partial class Templates { public static int Answer() => 42; } }\n" + Template("Card"));

        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.Errors);
        Assert.Contains("Card Card()", result.Factories);
    }

    [Fact]
    public void Templates_with_the_same_name_are_reported_and_get_no_factory()
    {
        var result = Run("namespace A { " + Template("Card") + " } namespace B { " + Template("Card") + " }\n" + Template("Row"));

        Assert.Equal(2, result.Diagnostics.Count(diagnostic => diagnostic.Id == "TDSL011"));
        Assert.Contains("'A.Card', 'B.Card'", result.Diagnostics[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        Assert.DoesNotContain("Card Card(", result.Factories);
        Assert.Contains("Row Row(", result.Factories);
    }

    [Fact]
    public void Without_a_template_that_gets_a_factory_there_is_no_class()
    {
        Assert.Null(Run("public class Plain;").Factories);
        Assert.Null(Run(Template("Templates")).Factories);
        Assert.Null(Run("namespace A { " + Template("Card") + " } namespace B { " + Template("Card") + " }").Factories);
    }

    [Theory]
    [InlineData("public abstract partial class Card : IHtmlTemplate { public abstract IEnumerable<HtmlElement> Build(); }")]
    [InlineData("public sealed class Card : IHtmlTemplate { public IEnumerable<HtmlElement> Build() => []; }")]
    [InlineData("public sealed partial class Card : IHtmlTemplate { private Card() { } public IEnumerable<HtmlElement> Build() => []; }")]
    [InlineData("public sealed partial class Card : IHtmlTemplate { public Card(out int x) { x = 1; } public IEnumerable<HtmlElement> Build() => []; }")]
    [InlineData("public sealed partial class Card : IHtmlTemplate { public required string Title { get; init; } public IEnumerable<HtmlElement> Build() => []; }")]
    [InlineData("public sealed partial class Card : IHtmlTemplate { public required string Title; public IEnumerable<HtmlElement> Build() => []; }")]
    [InlineData("file sealed partial class Card : IHtmlTemplate { public IEnumerable<HtmlElement> Build() => []; }")]
    public void Templates_that_code_outside_them_can_not_create_get_no_factory(string source) =>
        Assert.Null(Run(source).Factories);

    [Fact]
    public void Each_constructor_others_can_call_is_a_factory_with_the_same_access()
    {
        var result = Run("""
            public sealed partial class Card : IHtmlTemplate
            {
                public Card() { }
                internal Card(int level) { }
                protected internal Card(string title) { }
                private Card(bool hidden) { }
                public Card(in DateTime when, params string[] tags) { }
                public IEnumerable<HtmlElement> Build() => [];
            }

            // With a required member, only a constructor that sets it can be called without setting it after.
            public sealed partial class Marked : IHtmlTemplate
            {
                public Marked() { }
                [SetsRequiredMembers] public Marked(char mark) { Mark = mark; }
                public required char Mark { get; init; }
                public IEnumerable<HtmlElement> Build() => [];
            }

            internal sealed partial class Hidden : IHtmlTemplate { public IEnumerable<HtmlElement> Build() => []; }
            public sealed partial record Shown(string Title) : IHtmlTemplate { public IEnumerable<HtmlElement> Build() => []; }
            public partial struct Empty : IHtmlTemplate { public IEnumerable<HtmlElement> Build() => []; }
            public partial struct Full(int value) : IHtmlTemplate { public IEnumerable<HtmlElement> Build() => [H.P [ value ]]; }
            """);

        Assert.Empty(result.Errors);
        Assert.Contains("internal static global::Card Card(int level)", result.Factories);
        Assert.Contains("internal static global::Card Card(string title)", result.Factories);
        Assert.Contains("public static global::Card Card(in global::System.DateTime when, params string[] tags)", result.Factories);
        Assert.Contains("public static global::Card Card()", result.Factories);
        Assert.DoesNotContain("bool hidden", result.Factories);
        Assert.Contains("public static global::Marked Marked(char mark)", result.Factories);
        Assert.DoesNotContain("Marked Marked()", result.Factories);
        Assert.Contains("internal static global::Hidden Hidden()", result.Factories);
        Assert.Contains("public static global::Shown Shown(string Title)", result.Factories);
        Assert.DoesNotContain("Shown(global::Shown", result.Factories);
        Assert.Contains("public static global::Empty Empty()", result.Factories);
        Assert.Contains("public static global::Full Full(int value)", result.Factories);
        Assert.DoesNotContain("Full Full()", result.Factories);
    }

    [Fact]
    public void The_class_is_internal_if_no_factory_is_public() =>
        Assert.Contains("internal static partial class Templates", Run(Template("Card", modifiers: "internal sealed partial class")).Factories);

    [Fact]
    public void Default_values_are_written_as_constants_of_their_type()
    {
        var result = Run("""
            public enum Size { Small = -1, Large = 2 }

            public sealed partial class Card(
                string text = "a\"b", string? none = null, char mark = 'x', bool flag = true, int number = -5, long big = 5,
                ulong huge = 6, uint positive = 7, float single = 1.5f, double real = 2.5, decimal money = 1.25m,
                double nan = double.NaN, float infinite = float.PositiveInfinity, double negative = double.NegativeInfinity,
                Size size = Size.Small, int? maybe = 3, DateTime when = default, string @class = "c") : IHtmlTemplate
            {
                public IEnumerable<HtmlElement> Build() => [H.P [ text ]];
            }
            """);

        Assert.Empty(result.Errors);
        Assert.Contains("string text = \"a\\\"b\"", result.Factories);
        Assert.Contains("string? none = default", result.Factories);
        Assert.Contains("char mark = 'x'", result.Factories);
        Assert.Contains("bool flag = true", result.Factories);
        Assert.Contains("double nan = global::System.Double.NaN", result.Factories);
        Assert.Contains("float infinite = global::System.Single.PositiveInfinity", result.Factories);
        Assert.Contains("double negative = global::System.Double.NegativeInfinity", result.Factories);
        Assert.Contains("global::Size size = (global::Size)(-1)", result.Factories);
        Assert.Contains("string @class = \"c\"", result.Factories);
        Assert.Contains("new(text, none, mark, flag, number, big, huge, positive, single, real, money, nan, infinite, negative, size, maybe, when, @class)", result.Factories);
    }

    [Fact]
    public void A_generic_template_gets_a_generic_factory_with_its_constraints()
    {
        var result = Run("""
            public interface IItem;

            public sealed partial class Grid<TItem, TKey, TValue, TNum, TOther>(TItem item) : IHtmlTemplate
                where TItem : class?, IItem, new()
                where TKey : notnull
                where TValue : struct
                where TNum : unmanaged
                where TOther : class
            {
                public IEnumerable<HtmlElement> Build() => [];
            }
            """);

        Assert.Empty(result.Errors);
        Assert.Contains("Grid<TItem, TKey, TValue, TNum, TOther>(TItem item)", result.Factories);
        Assert.Contains("where TItem : class?, global::IItem, new()", result.Factories);
        Assert.Contains("where TKey : notnull", result.Factories);
        Assert.Contains("where TValue : struct", result.Factories);
        Assert.Contains("where TNum : unmanaged", result.Factories);
        Assert.Contains("where TOther : class", result.Factories);
    }

    [Fact]
    public void A_template_whose_context_depends_on_its_type_parameter_gets_the_context_of_its_parent()
    {
        var result = Run("""
            using static Test.Templates;

            public sealed partial class Echo<TContext>(TContext value) : IHtmlTemplate<TContext> where TContext : notnull
            {
                public IEnumerable<HtmlElement> Build(TContext context) => [H.P [ $"{context}" ]];
            }

            public sealed partial class Page : IHtmlTemplate<string>
            {
                public IEnumerable<HtmlElement> Build(string context) => [Echo(context)];
            }
            """);

        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("TContext[]", "string[]")]
    [InlineData("List<TContext>", "List<string>")]
    public void A_context_built_from_the_type_parameter_is_left_to_the_compiler_to_check(string needed, string given)
    {
        var result = Run($$"""
            using static Test.Templates;

            public sealed partial class Echo<TContext>({{needed}} value) : IHtmlTemplate<{{needed}}> where TContext : notnull
            {
                public IEnumerable<HtmlElement> Build({{needed}} context) => [H.P [ "echo" ]];
            }

            public sealed partial class Page : IHtmlTemplate<{{given}}>
            {
                public IEnumerable<HtmlElement> Build({{given}} context) => [Echo(context)];
            }
            """);

        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void A_generic_template_whose_context_is_not_built_from_its_type_parameter_is_checked()
    {
        var result = Run("""
            using static Test.Templates;

            public sealed partial class Echo<TItem>(TItem item) : IHtmlTemplate<Uri>
            {
                public IEnumerable<HtmlElement> Build(Uri context) => [H.P [ "echo" ]];
            }

            public sealed partial class Page : IHtmlTemplate<string>
            {
                public IEnumerable<HtmlElement> Build(string context) => [Echo(1)];
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "TDSL008");
    }

    [Theory]
    [InlineData("Test.Templates.Grid<int>(1)", true)]     // named type arguments, through the class
    [InlineData("Grid<Nope>(1)", false)]                  // type arguments that don't exist, which C# reports
    public void Type_arguments_of_a_factory_call_are_bound_if_they_exist(string call, bool compiles)
    {
        var result = Run($$"""
            public sealed partial class Grid<TItem>(TItem item) : IHtmlTemplate { public IEnumerable<HtmlElement> Build() => [H.P [ $"{item}" ]]; }

            public sealed partial class Page : IHtmlTemplate
            {
                public IEnumerable<HtmlElement> Build() => [H.Div [ {{call}} ]];
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id.StartsWith("TDSL", StringComparison.Ordinal));
        Assert.Equal(compiles, result.Errors.Length == 0);
    }

    [Theory]
    [InlineData("Other(null)")]       // methods of the template's own, which the call could mean either of
    [InlineData("Nope()")]            // nothing of that name
    [InlineData("Nope()()")]          // not a name at all
    [InlineData("value.Card()")]      // a member of something that exists
    public void A_call_that_creates_no_template_of_this_project_is_left_to_the_compiler(string call)
    {
        var result = Run($$"""
            using static Test.Templates;

            public sealed partial class Card : IHtmlTemplate { public IEnumerable<HtmlElement> Build() => []; }

            public sealed partial class Page(string value) : IHtmlTemplate
            {
                public IEnumerable<HtmlElement> Build() => [H.Div [ {{call}} ]];
                private static Card Other(string text) => new();
                private static Page Other(Uri address) => new(address.ToString());
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "TDSL004");
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void A_method_of_the_template_wins_over_a_factory_of_the_same_name()
    {
        var result = Run("""
            using static Test.Templates;

            public sealed partial class Card(string title) : IHtmlTemplate { public IEnumerable<HtmlElement> Build() => [H.P [ title ]]; }

            public sealed partial class Page : IHtmlTemplate
            {
                public IEnumerable<HtmlElement> Build() => [H.Div [ Card() ]];
                private static Card Card() => new("own");
            }
            """);

        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.Errors);
    }
}
