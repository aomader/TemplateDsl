using Microsoft.CodeAnalysis;

namespace TemplateDsl.Generators;

internal static class Diagnostics
{
    private const string Category = "TemplateDsl";

    /// <summary>Each diagnostic has a page there that explains its causes and fixes.</summary>
    private const string HelpLink = "https://github.com/aomader/TemplateDsl/blob/main/docs/diagnostics/";

    public static readonly DiagnosticDescriptor MustBePartial = new(
        "TDSL001",
        "Template must be partial",
        "Template '{0}' must be declared partial",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink + "TDSL001.md");

    public static readonly DiagnosticDescriptor MustBeSealed = new(
        "TDSL002",
        "Template must be a sealed class",
        "Template '{0}' must be a sealed class",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink + "TDSL002.md");

    public static readonly DiagnosticDescriptor UnsupportedTemplateShape = new(
        "TDSL003",
        "Template must be a top-level type",
        "Template '{0}' must be a top-level type that isn't file-local",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink + "TDSL003.md");

    public static readonly DiagnosticDescriptor UnsupportedBuildConstruct = new(
        "TDSL004",
        "Unsupported construct in Build",
        "Build cannot be translated: {0}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink + "TDSL004.md");

    public static readonly DiagnosticDescriptor BothSyncAndAsync = new(
        "TDSL005",
        "Template must implement exactly one template interface",
        "Template '{0}' must implement exactly one template interface",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink + "TDSL005.md");

    public static readonly DiagnosticDescriptor InvalidFormat = new(
        "TDSL006",
        "Invalid format declaration",
        "{0}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink + "TDSL006.md");

    public static readonly DiagnosticDescriptor NodeOutsideBuild = new(
        "TDSL007",
        "Template nodes can only be used in Build",
        "'{0}' can only be used in the Build method of a template; create a template for this output",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink + "TDSL007.md");

    public static readonly DiagnosticDescriptor ContextNotProvided = new(
        "TDSL008",
        "A nested template needs a context its parent doesn't have",
        "{0}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink + "TDSL008.md");
}
