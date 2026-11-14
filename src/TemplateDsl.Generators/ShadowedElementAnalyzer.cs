using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TemplateDsl.Generators;

/// <summary>
/// Explains the error of an element that a name of the app's own hides. With <c>using static</c>, a member of the
/// template, a local or a type in scope wins over an imported element of the same name, as C# looks there first:
/// <c>Button [ ... ]</c> with a template class <c>Button</c>, or <c>Title [ ... ]</c> with a property <c>Title</c>. C#
/// reports what the name is instead, such as "'Button' is a type, which is not valid in the given context", which
/// doesn't mention the element. This only reports where that use fails, so valid code such as <c>Title.Length</c> is left alone.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ShadowedElementAnalyzer : DiagnosticAnalyzer
{
    private enum Use
    {
        /// <summary><c>Button [ ... ]</c></summary>
        Brackets,

        /// <summary><c>Button.Class("x")</c>, the start of a chain.</summary>
        Chain,

        /// <summary><c>Text("x")</c>, for an element written as a method.</summary>
        Call,

        /// <summary><c>Br</c>, as an element of Build's collection or a child in brackets.</summary>
        Child,
    }

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Diagnostics.ShadowedElement);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.IdentifierName);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var identifier = (IdentifierNameSyntax)context.Node;
        var name = identifier.Identifier.ValueText;

        // Element names are PascalCase, so most identifiers end here, before anything is looked up.
        if (name.Length == 0 || !char.IsUpper(name[0]) || GetUse(identifier) is not { } use || !IsInBuild(context))
        {
            return;
        }

        var model = context.SemanticModel;
        var cancellationToken = context.CancellationToken;

        if (FindHiddenElement(model, identifier, cancellationToken) is not var (tags, symbol)
            || !Fails(model, identifier, use, symbol, cancellationToken))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Diagnostics.ShadowedElement, identifier.GetLocation(), name, Describe(symbol), tags.Name));
    }

    /// <summary>
    /// The class of an element that <paramref name="identifier"/> would be, imported with <c>using static</c>, and what the
    /// name is instead, or null if it is that element or no element has its name. The translator uses it too, to leave
    /// such a use to the errors of C# and this analyzer.
    /// </summary>
    internal static (INamedTypeSymbol Tags, ISymbol Symbol)? FindHiddenElement(SemanticModel model, IdentifierNameSyntax identifier, CancellationToken cancellationToken)
    {
        var name = identifier.Identifier.ValueText;
        var tags = model.GetImportScopes(identifier.SpanStart, cancellationToken)
            .SelectMany(scope => scope.Imports)
            .Select(import => import.NamespaceOrType)
            .OfType<INamedTypeSymbol>()
            .FirstOrDefault(type => FormatReader.IsTags(type) && type.GetMembers(name).Any(member => member.IsStatic));

        if (tags is null)
        {
            return null;
        }

        var info = model.GetSymbolInfo(identifier, cancellationToken);

        return (info.Symbol ?? info.CandidateSymbols.FirstOrDefault()) is { } symbol && !SymbolEqualityComparer.Default.Equals(symbol.ContainingType, tags)
            ? (tags, symbol)
            : null;
    }

    private static Use? GetUse(IdentifierNameSyntax identifier) => identifier.Parent switch
    {
        ElementAccessExpressionSyntax access when access.Expression == identifier => Use.Brackets,
        MemberAccessExpressionSyntax member when member.Expression == identifier => Use.Chain,
        InvocationExpressionSyntax invocation when invocation.Expression == identifier => Use.Call,
        ExpressionElementSyntax => Use.Child,
        ArgumentSyntax { Parent: BracketedArgumentListSyntax } => Use.Child,
        _ => null,
    };

    /// <summary>Elements are only written in Build, so only there can a name hide one.</summary>
    private static bool IsInBuild(SyntaxNodeAnalysisContext context) =>
        context.ContainingSymbol?.ContainingType is { } type
        && FormatReader.Implements(type, FormatReader.Renderable)
        && context.Node.FirstAncestorOrSelf<MemberDeclarationSyntax>() is MethodDeclarationSyntax { Identifier.ValueText: "Build" or "BuildAsync" };

    /// <summary>
    /// Whether the use fails with what the name is, which is when C# reports an error that doesn't mention the element.
    /// Brackets and a call are asked as an operation: in Build, a value converts to a node, and the symbol of the
    /// expression is then that conversion, while its operation is what it is, such as an indexer that can't take the children.
    /// </summary>
    private static bool Fails(SemanticModel model, IdentifierNameSyntax identifier, Use use, ISymbol symbol, CancellationToken cancellationToken) => use switch
    {
        Use.Brackets or Use.Call => model.GetOperation(identifier.Parent!, cancellationToken) is Microsoft.CodeAnalysis.Operations.IInvalidOperation,
        Use.Chain => model.GetSymbolInfo(identifier.Parent!, cancellationToken).Symbol is null,
        _ => symbol is INamespaceOrTypeSymbol,
    };

    private static string Describe(ISymbol symbol) => symbol switch
    {
        INamedTypeSymbol type when FormatReader.Implements(type, FormatReader.Renderable) => $"the template '{type.ToDisplayString()}'",
        INamedTypeSymbol type => $"the type '{type.ToDisplayString()}'",
        INamespaceSymbol => $"the namespace '{symbol.ToDisplayString()}'",
        IPropertySymbol => $"the property '{symbol.Name}'",
        IFieldSymbol => $"the field '{symbol.Name}'",
        IMethodSymbol => $"the method '{symbol.Name}'",
        ILocalSymbol => $"the local variable '{symbol.Name}'",
        _ => $"the parameter '{symbol.Name}'",
    };
}
