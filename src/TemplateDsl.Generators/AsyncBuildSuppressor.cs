using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TemplateDsl.Generators;

/// <summary>
/// BuildAsync is never run, and an async template is also how a page with nothing to wait for streams,
/// so a BuildAsync without await is fine. It has to be async for C# to accept its collection expression.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AsyncBuildSuppressor : DiagnosticSuppressor
{
    private static readonly SuppressionDescriptor NoAwaitNeeded = new(
        "TDSLSPR001",
        "CS1998",
        "BuildAsync never runs, and an async template streams even if it awaits nothing.");

    /// <inheritdoc/>
    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions { get; } =
        ImmutableArray.Create(NoAwaitNeeded);

    /// <inheritdoc/>
    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        foreach (var diagnostic in context.ReportedDiagnostics)
        {
            // CS1998 is always reported in source, on the name of the method or the arrow of a lambda, so this is the
            // method itself. An async lambda inside BuildAsync is a function of its own and keeps its warning.
            var tree = diagnostic.Location.SourceTree!;

            if (tree.GetRoot(context.CancellationToken).FindNode(diagnostic.Location.SourceSpan) is not MethodDeclarationSyntax { Identifier.ValueText: "BuildAsync" } method)
            {
                continue;
            }

            var symbol = context.GetSemanticModel(tree).GetDeclaredSymbol(method, context.CancellationToken)!;

            if (FormatReader.Implements(symbol.ContainingType, FormatReader.Renderable))
            {
                context.ReportSuppression(Suppression.Create(NoAwaitNeeded, diagnostic));
            }
        }
    }
}
