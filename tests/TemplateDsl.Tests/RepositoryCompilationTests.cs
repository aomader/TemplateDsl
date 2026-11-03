using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using TemplateDsl.Generators;
using Xunit;

namespace Tests;

/// <summary>
/// Runs the generators and analyzers over the projects of the packages, as the build does. The build runs them
/// inside the compiler, where they can't be measured, so this is also what makes the generators' code coverage
/// complete. It reads what it needs from the project files, so it doesn't depend on an earlier build.
/// </summary>
public class RepositoryCompilationTests
{
    /// <summary>
    /// The projects of the packages and their tests. The samples are left out on purpose: the build compiles them
    /// anyway, and the coverage of the packages shouldn't depend on what the samples happen to use.
    /// </summary>
    public static TheoryData<string> Projects => new()
    {
        "src/TemplateDsl.Html",
        "tests/TemplateDsl.Tests",
    };

    /// <summary>The usings that ImplicitUsings adds.</summary>
    private static readonly string[] ImplicitUsings =
        ["System", "System.Collections.Generic", "System.IO", "System.Linq", "System.Net.Http", "System.Threading", "System.Threading.Tasks"];

    [Theory]
    [MemberData(nameof(Projects))]
    public async Task The_generators_compile_the_project_without_errors(string directory)
    {
        var root = Path.Combine(Repository(), directory);
        var name = Path.GetFileName(directory);
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var project = XDocument.Load(Path.Combine(root, name + ".csproj"));

        var trees = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(Path.GetRelativePath(root, file)))
            .Select(file => CSharpSyntaxTree.ParseText(File.ReadAllText(file), parseOptions, file))
            .Append(CSharpSyntaxTree.ParseText(GlobalUsings(project), parseOptions, "GlobalUsings.g.cs"));

        // The test process has every assembly the projects need, but the project itself must come from its sources.
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => Path.GetFileNameWithoutExtension(path) != name)
            .Select(path => MetadataReference.CreateFromFile(path));

        var compilation = CSharpCompilation.Create(name, trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        CSharpGeneratorDriver.Create([new FormatGenerator().AsSourceGenerator(), new TemplateGenerator().AsSourceGenerator()], parseOptions: parseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        var diagnostics = await output
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new TemplateUsageAnalyzer(), new AsyncBuildSuppressor()))
            .GetAllDiagnosticsAsync();

        var errors = generatorDiagnostics.Concat(diagnostics)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && !diagnostic.IsSuppressed)
            .Select(diagnostic => diagnostic.ToString())
            .ToArray();

        Assert.True(errors.Length == 0, string.Join("\n", errors.Take(20)));
    }

    private static bool IsBuildOutput(string relativePath) =>
        relativePath.Split(Path.DirectorySeparatorChar).FirstOrDefault() is "bin" or "obj" or "Generated";

    private static string GlobalUsings(XDocument project) =>
        string.Concat(ImplicitUsings.Select(name => $"global using {name};\n"))
        + string.Concat(project.Descendants("Using").Select(item => item.Attribute("Alias") is { } alias
            ? $"global using {alias.Value} = {item.Attribute("Include")!.Value};\n"
            : $"global using {item.Attribute("Include")!.Value};\n"));

    private static string Repository([CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));
}
