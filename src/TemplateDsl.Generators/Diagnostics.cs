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

    public static readonly DiagnosticDescriptor UnsupportedTemplateType = new(
        "TDSL002",
        "Template can't be a ref struct",
        "Template '{0}' can't be a ref struct; make it a class, a struct or a record",
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

    public static readonly DiagnosticDescriptor TemplateAsNode = new(
        "TDSL009",
        "A template is only a node in Build",
        "'{0}' is converted to a node, which only Build does to place it; pass it as content instead, such as an IHtmlContent or IHtmlContent<TContext> parameter",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink + "TDSL009.md");

    public static readonly DiagnosticDescriptor InvalidTemplatesClass = new(
        "TDSL010",
        "The class of the template factories can't have its name",
        "'{0}' can't be the class of the template factories, because {1}; set the MSBuild property TemplateDslTemplatesClass to another name, such as <TemplateDslTemplatesClass>App.Ui.Templates</TemplateDslTemplatesClass>",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink + "TDSL010.md");

    public static readonly DiagnosticDescriptor SameTemplateName = new(
        "TDSL011",
        "Templates with the same name get no factory",
        "More than one template is named '{0}' ('{1}'), so none of them gets a factory in the class of the template factories; rename one, or create them with new",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink + "TDSL011.md");

    public static readonly DiagnosticDescriptor ShadowedElement = new(
        "TDSL012",
        "A name of your own hides an element",
        "'{0}' is {1} here, which hides the element '{0}'; write {2}.{0} for the element, or rename what hides it",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink + "TDSL012.md");
}
