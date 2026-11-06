using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TemplateDsl.Generators;

/// <summary>
/// Reports nodes and children brackets outside of Build, which the source generator never sees and so
/// can't compile or check, and templates converted to nodes there, which is only how Build places them. Creating a
/// template, passing it as content and rendering it stays allowed everywhere.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TemplateUsageAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Diagnostics.NodeOutsideBuild, Diagnostics.TemplateAsNode);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(
            Analyze,
            SyntaxKind.InvocationExpression,
            SyntaxKind.SimpleMemberAccessExpression,
            SyntaxKind.IdentifierName,
            SyntaxKind.ObjectCreationExpression,
            SyntaxKind.ImplicitObjectCreationExpression,
            SyntaxKind.ElementAccessExpression);
        context.RegisterOperationAction(AnalyzeConversion, OperationKind.Conversion);
    }

    /// <summary>A template's conversion to a node only exists so that Build can place it. Content is passed as content.</summary>
    private static void AnalyzeConversion(OperationAnalysisContext context)
    {
        var conversion = (Microsoft.CodeAnalysis.Operations.IConversionOperation)context.Operation;

        if (conversion.OperatorMethod is not { ContainingType: { } owner }
            || !FormatReader.Implements(owner, FormatReader.Renderable)
            || conversion.Syntax.IsMissing
            || IsAllowedHere(context.ContainingSymbol, conversion.Syntax))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Diagnostics.TemplateAsNode, conversion.Syntax.GetLocation(), conversion.Syntax.ToString()));
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        if (!IsTemplateConstruct(context) || IsAllowedHere(context))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Diagnostics.NodeOutsideBuild, context.Node.GetLocation(), context.Node.ToString()));
    }

    private static bool IsTemplateConstruct(SyntaxNodeAnalysisContext context)
    {
        var model = context.SemanticModel;

        switch (context.Node)
        {
            // H.Div, or Div with a using static. The name in H.Div is part of the member access.
            case MemberAccessExpressionSyntax or IdentifierNameSyntax:
                return (context.Node.Parent is not MemberAccessExpressionSyntax { Name: var name } || name != context.Node)
                    && model.GetSymbolInfo(context.Node, context.CancellationToken).Symbol is IPropertySymbol property
                    && FormatReader.IsTags(property.ContainingType);

            case InvocationExpressionSyntax invocation:
                var info = model.GetSymbolInfo(invocation, context.CancellationToken);
                return (info.Symbol ?? info.CandidateSymbols.FirstOrDefault()) is IMethodSymbol method
                    && FormatReader.IsTags(method.ContainingType);

            case BaseObjectCreationExpressionSyntax creation:
                return model.GetTypeInfo(creation, context.CancellationToken).Type is { } created
                    && FormatReader.ReadNode(created) is not null;

            // The only kind left is brackets. The indexers are generated, so this looks at what they are applied to.
            default:
                return model.GetTypeInfo(((ElementAccessExpressionSyntax)context.Node).Expression, context.CancellationToken).Type is { } receiver
                    && (FormatReader.ReadNode(receiver) is { IsText: false }
                        || FormatReader.Implements(receiver, FormatReader.AcceptsChildren));
        }
    }

    private static bool IsAllowedHere(SyntaxNodeAnalysisContext context) => IsAllowedHere(context.ContainingSymbol, context.Node);

    private static bool IsAllowedHere(ISymbol? containingSymbol, SyntaxNode node)
    {
        var type = containingSymbol?.ContainingType;

        if (type is null)
        {
            return true;
        }

        // Templates derive from their format too, so this has to come first.
        if (FormatReader.Implements(type, FormatReader.Renderable))
        {
            var method = node.FirstAncestorOrSelf<MemberDeclarationSyntax>() as MethodDeclarationSyntax;

            return method?.Identifier.ValueText is "Build" or "BuildAsync";
        }

        // A format's own declarations have to create nodes.
        return FormatReader.IsTags(type) || FormatReader.FindFormat(type) is not null || FormatReader.IsFormat(type);
    }
}
