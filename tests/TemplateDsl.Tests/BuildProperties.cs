using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Tests;

/// <summary>MSBuild properties for the generators, as the build hands them over: <c>build_property.RootNamespace</c> and the like.</summary>
internal sealed class BuildProperties(IReadOnlyDictionary<string, string> properties) : AnalyzerConfigOptionsProvider
{
    private static readonly Options None = new(new Dictionary<string, string>());

    public override AnalyzerConfigOptions GlobalOptions { get; } =
        new Options(properties.ToDictionary(property => "build_property." + property.Key, property => property.Value));

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => None;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => None;

    private sealed class Options(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) => values.TryGetValue(key, out value);
    }
}
