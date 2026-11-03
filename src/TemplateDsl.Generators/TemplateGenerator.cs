using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TemplateDsl.Generators;

/// <summary>
/// Finds the implementers of <c>ITemplate&lt;TNode&gt;</c>, enforces the shape they must have and generates
/// instance render methods from the body of their <c>Build</c> method. Everything format-specific comes
/// from the attributes on <c>TNode</c> and its nodes.
/// </summary>
[Generator]
public sealed class TemplateGenerator : IIncrementalGenerator
{
    private const string Hidden = "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]";

    private const string Checkpoint =
        "await global::TemplateDsl.TemplateStreaming.FlushIfNeededAsync(__writer, __ct).ConfigureAwait(false);";

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var templates = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) =>
                    node is ClassDeclarationSyntax or StructDeclarationSyntax or RecordDeclarationSyntax
                    && ((TypeDeclarationSyntax)node).BaseList is not null,
                transform: static (ctx, cancellationToken) => Analyze(ctx, cancellationToken))
            .Where(static template => template is not null);

        context.RegisterSourceOutput(templates, static (ctx, template) =>
        {
            foreach (var diagnostic in template!.Diagnostics)
            {
                ctx.ReportDiagnostic(diagnostic.ToDiagnostic());
            }

            if (template.Segments is not null)
            {
                ctx.AddSource(GetHintName(template), Emit(template));
            }
        });
    }

    private static TemplateModel? Analyze(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        // The node is a type declaration, which always declares a named type.
        var symbol = (INamedTypeSymbol)context.SemanticModel.GetDeclaredSymbol(context.Node, cancellationToken)!;

        if (symbol.IsStatic) // A static class implementing an interface is already compiler error CS0714.
        {
            return null;
        }

        var templates = symbol.AllInterfaces.Where(FormatReader.IsTemplateInterface).ToArray();

        if (templates.Length == 0)
        {
            return null;
        }

        var template = templates[0];
        var isAsync = template.Name == "IAsyncTemplate";
        var contextType = template.TypeArguments.Length > 1 ? template.TypeArguments[1] : null;
        var buildMethod = FindBuildMethod(symbol, isAsync ? "BuildAsync" : "Build", (isAsync ? 1 : 0) + (contextType is null ? 0 : 1));

        // A base class and a contract like an abstract Page have nothing to generate, as they have no output of their own.
        if (FormatReader.IsTemplateBase(symbol)
            || (symbol.IsAbstract && buildMethod?.Body is null && buildMethod?.ExpressionBody is null))
        {
            return null;
        }

        // A partial type can have a base list on several declarations, but must be handled only once.
        var declaration = symbol.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken))
            .OfType<TypeDeclarationSyntax>()
            .First(d => d.BaseList is not null);

        if (declaration.SyntaxTree != context.Node.SyntaxTree || declaration.Span != context.Node.Span)
        {
            return null;
        }

        var diagnostics = CheckShape(symbol, declaration);
        var usings = new List<string>();
        Segment[]? segments = null;
        string? cancellationParameter = null;
        string? contextParameter = null;
        var acceptsChildren = FormatReader.Implements(symbol, FormatReader.AcceptsChildren);
        var format = template.TypeArguments[0];

        if (templates.Length > 1)
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.BothSyncAndAsync, declaration.Identifier.GetLocation(), symbol.Name));
        }

        if (!FormatReader.IsFormat(format))
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.InvalidFormat, declaration.Identifier.GetLocation(), $"'{format.Name}' is not marked with [TemplateFormat]"));
        }

        if (diagnostics.Count == 0 && !FormatReader.BaseTypes(symbol).Any(FormatReader.IsTemplateBase))
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.InvalidFormat, declaration.Identifier.GetLocation(), $"'{symbol.Name}' must derive from the template base class of its format, such as HtmlTemplate"));
        }

        if (diagnostics.Count == 0 && buildMethod is not null)
        {
            var translator = new BuildTranslator(context.SemanticModel.Compilation, format, contextType, isAsync, acceptsChildren, cancellationToken);

            // The method was found by its number of parameters, so these exist.
            cancellationParameter = isAsync ? buildMethod.ParameterList.Parameters.Last().Identifier.ToString() : null;
            contextParameter = contextType is null ? null : buildMethod.ParameterList.Parameters.First().Identifier.ToString();
            segments = translator.Translate(buildMethod);
            diagnostics.AddRange(translator.Diagnostics);
            usings.AddRange(BuildTranslator.GetUsings(buildMethod));
        }

        var containingNamespace = symbol.ContainingNamespace;

        return new TemplateModel(
            containingNamespace.IsGlobalNamespace ? null : containingNamespace.ToDisplayString(),
            // With its type parameters, which a partial declaration repeats without their constraints.
            symbol.IsGenericType ? $"{declaration.Identifier}<{string.Join(", ", symbol.TypeParameters.Select(parameter => parameter.Name))}>" : declaration.Identifier.ToString(),
            symbol.MetadataName,
            symbol.IsRecord ? "record" : "class",
            new EquatableArray<string>(usings.Distinct().ToArray()),
            segments is null ? null : new EquatableArray<Segment>(segments),
            acceptsChildren,
            isAsync,
            cancellationParameter,
            contextType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            contextParameter,
            format.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()));
    }

    private static List<DiagnosticInfo> CheckShape(INamedTypeSymbol symbol, TypeDeclarationSyntax declaration)
    {
        var diagnostics = new List<DiagnosticInfo>();
        var location = declaration.Identifier.GetLocation();

        if (!declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.MustBePartial, location, symbol.Name));
        }

        if (symbol.TypeKind != TypeKind.Class || !symbol.IsSealed)
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.MustBeSealed, location, symbol.Name));
        }

        // A nested type would need its containing types to be partial too, and a file-local one isn't visible to generated code.
        if (symbol.ContainingType is not null || symbol.IsFileLocal)
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.UnsupportedTemplateShape, location, symbol.Name));
        }

        return diagnostics;
    }

    /// <summary>The method by name, because the abstract method it overrides may be generated and not visible yet.</summary>
    private static MethodDeclarationSyntax? FindBuildMethod(INamedTypeSymbol symbol, string methodName, int parameterCount)
    {
        var method = symbol.GetMembers(methodName).OfType<IMethodSymbol>().FirstOrDefault(m => m.Parameters.Length == parameterCount);

        // The body of a partial method is on its implementing declaration. A missing method is a compiler error already.
        return (method?.PartialImplementationPart ?? method)?.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax())
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault();
    }

    private static string GetHintName(TemplateModel template)
    {
        var prefix = template.Namespace is null ? "" : template.Namespace.Replace("@", "") + ".";
        return $"{prefix}{template.MetadataName}.Rendering.g.cs";
    }

    private static string Emit(TemplateModel template)
    {
        var source = new StringBuilder();
        // The copied code is the template author's, whose warnings are reported on Build itself.
        source.Append("// <auto-generated/>\n#nullable enable\n#pragma warning disable\n\n");

        foreach (var directive in template.Usings)
        {
            source.Append(directive).Append('\n');
        }

        if (template.Usings.Length > 0)
        {
            source.Append('\n');
        }

        if (template.Namespace is not null)
        {
            source.Append("namespace ").Append(template.Namespace).Append(";\n\n");
        }

        source.Append("partial ").Append(template.Keyword).Append(' ').Append(template.Name).Append("\n{\n");

        if (!template.IsAsync)
        {
            EmitSyncCore(source, template);
        }

        // A sync template without children is only ever rendered in one go. Children can await, so
        // a template that places them has to be able to wait in the middle of its output.
        if (template.IsAsync || template.AcceptsChildren)
        {
            EmitAsyncCore(source, template);
        }

        EmitPublicMethods(source, template);

        if (template.AcceptsChildren)
        {
            EmitChildrenMembers(source, template);
        }

        source.Append("}\n");
        return source.ToString();
    }

    private static void EmitSyncCore(StringBuilder source, TemplateModel template)
    {
        // The parameters have these names so that the copied expressions can't see or clash with them,
        // which an override is free to do, even on the public RenderTo.
        source.Append(template.AcceptsChildren
                ? $"    {Hidden}\n    public override void __RenderWithChildren("
                : "    /// <summary>Writes this template as UTF-8 to <paramref name=\"__writer\"/>, without flushing.</summary>\n    public override void RenderTo(")
            .Append("global::System.Buffers.IBufferWriter<byte> __writer")
            .Append(template.ContextType is null ? "" : $", {template.ContextType} __context")
            .Append(template.AcceptsChildren ? ", global::System.Action? __content" : "")
            .Append(")\n    {\n");

        var indent = "        ";
        EmitContextAlias(source, template, indent);

        foreach (var segment in template.Segments!.Value)
        {
            switch (segment.Kind)
            {
                case SegmentKind.Template:
                    source.Append(indent).Append('(').Append(segment.Text).Append(")?.RenderTo(__writer").Append(ContextArgument(segment)).Append(");\n");
                    break;

                case SegmentKind.TemplateWithChildrenStart:
                    source.Append(indent).Append('(').Append(segment.Text).Append(")?.__RenderWithChildren(__writer").Append(ContextArgument(segment)).Append(", () =>\n")
                        .Append(indent).Append("{\n");
                    indent += "    ";
                    break;

                case SegmentKind.TemplateWithChildrenEnd:
                    indent = indent.Substring(4);
                    source.Append(indent).Append("});\n");
                    break;

                case SegmentKind.Children:
                    source.Append(indent).Append("__content?.Invoke();\n");
                    break;

                default:
                    indent = EmitShared(source, segment, indent);
                    break;
            }
        }

        source.Append("    }\n\n");
    }

    /// <summary>
    /// The async form of the render method: the same segments, with a flush checkpoint after every loop iteration
    /// and every sync child, and nested templates that can wait too. If the client is gone, an
    /// OperationCanceledException unwinds all of them.
    /// </summary>
    private static void EmitAsyncCore(StringBuilder source, TemplateModel template)
    {
        // The template's code. It's public because other templates call it, and hidden because nobody else should.
        // The parameters have these names so that the copied expressions can't see or clash with them.
        source.Append("    ").Append(Hidden).Append('\n');
        source.Append("    public override async global::System.Threading.Tasks.ValueTask ")
            .Append(template.AcceptsChildren ? "__RenderWithChildrenAsync" : "__RenderAsync")
            .Append("(global::System.IO.Pipelines.PipeWriter __writer")
            .Append(template.ContextType is null ? "" : $", {template.ContextType} __context")
            .Append(", global::System.Threading.CancellationToken __ct")
            .Append(template.AcceptsChildren ? ", global::System.Func<global::System.Threading.Tasks.ValueTask>? __content" : "")
            .Append(")\n    {\n");

        var indent = "        ";
        var nextTemporary = 0;

        // The copied expressions use the token under the name BuildAsync gave it.
        if (template.CancellationParameter is not null)
        {
            source.Append(indent).Append("global::System.Threading.CancellationToken ").Append(template.CancellationParameter).Append(" = __ct;\n");
        }

        EmitContextAlias(source, template, indent);

        foreach (var segment in template.Segments!.Value)
        {
            switch (segment.Kind)
            {
                case SegmentKind.FlushBeforeAwait:
                    source.Append(indent).Append("await global::TemplateDsl.TemplateStreaming.FlushBufferedAsync(__writer, __ct).ConfigureAwait(false);\n");
                    break;

                case SegmentKind.LoopEnd:
                    source.Append(indent).Append(Checkpoint).Append('\n');
                    indent = EmitShared(source, segment, indent);
                    break;

                case SegmentKind.Template:
                {
                    var temporary = EmitTemporary(source, segment, indent, nextTemporary++);
                    source.Append(indent).Append("    if (").Append(temporary).Append(" is not null)\n").Append(indent).Append("    {\n");

                    // A sync template has no async code. This is where its output gets the chance to be sent.
                    if (segment.IsSyncChild)
                    {
                        source.Append(indent).Append("        ").Append(temporary).Append(".RenderTo(__writer").Append(ContextArgument(segment)).Append(");\n");
                        source.Append(indent).Append("        ").Append(Checkpoint).Append('\n');
                    }
                    else
                    {
                        source.Append(indent).Append("        await ").Append(temporary).Append(".__RenderAsync(__writer").Append(ContextArgument(segment)).Append(", __ct).ConfigureAwait(false);\n");
                    }

                    source.Append(indent).Append("    }\n").Append(indent).Append("}\n");
                    break;
                }

                case SegmentKind.TemplateWithChildrenStart:
                {
                    var temporary = EmitTemporary(source, segment, indent, nextTemporary++);
                    source.Append(indent).Append("    if (").Append(temporary).Append(" is not null) await ").Append(temporary)
                        .Append(".__RenderWithChildrenAsync(__writer").Append(ContextArgument(segment)).Append(", __ct, async () =>\n");
                    source.Append(indent).Append("    {\n");
                    indent += "        ";
                    break;
                }

                case SegmentKind.TemplateWithChildrenEnd:
                    indent = indent.Substring(8);
                    source.Append(indent).Append("    }).ConfigureAwait(false);\n");
                    source.Append(indent).Append("}\n");
                    break;

                case SegmentKind.Children:
                    source.Append(indent).Append("if (__content is not null) await __content().ConfigureAwait(false);\n");
                    break;

                default:
                    indent = EmitShared(source, segment, indent);
                    break;
            }
        }

        source.Append(indent).Append(Checkpoint).Append("\n    }\n\n");
    }

    /// <summary>Opens a scope with the template in a variable, and returns the name of the variable.</summary>
    private static string EmitTemporary(StringBuilder source, Segment segment, string indent, int number)
    {
        var temporary = $"__t{number}";
        source.Append(indent).Append("{\n");
        source.Append(indent).Append("    var ").Append(temporary).Append(" = ").Append(segment.Text).Append(";\n");
        return temporary;
    }

    private static string ContextArgument(Segment segment) => segment.PassContext ? ", __context" : "";

    /// <summary>Emits what the sync and async forms have in common, and returns the new indentation.</summary>
    internal static string EmitShared(StringBuilder source, Segment segment, string indent)
    {
        switch (segment.Kind)
        {
            case SegmentKind.BlockStart:
                if (segment.Text.Length > 0)
                {
                    source.Append(indent).Append(segment.Text).Append('\n');
                }

                source.Append(indent).Append("{\n");
                return indent + "    ";

            case SegmentKind.Else:
                indent = indent.Substring(4);
                source.Append(indent).Append("}\n").Append(indent).Append("else\n").Append(indent).Append("{\n");
                return indent + "    ";

            case SegmentKind.BlockEnd:
            case SegmentKind.LoopEnd:
                indent = indent.Substring(4);
                source.Append(indent).Append("}\n");
                return indent;

            case SegmentKind.Statement:
                source.Append(indent).Append(segment.Text).Append('\n');
                return indent;

            case SegmentKind.Literal:
                source.Append(indent).Append(LiteralWrite(segment.Text)).Append('\n');
                return indent;

            case SegmentKind.Value:
                source.Append(indent).Append(ValueWrite($"(string?)({segment.Text})", segment.EscapeMethod)).Append('\n');
                return indent;

            case SegmentKind.FormattedValue when segment.IsUtf8:
                source.Append(indent).Append("global::TemplateDsl.TemplateValues.WriteUtf8(").Append(segment.Text).Append(", __writer, ")
                    .Append(segment.ValueFormat is null ? "null" : SymbolDisplay.FormatLiteral(segment.ValueFormat, quote: true))
                    .Append(segment.IsInvariant ? ", global::System.Globalization.CultureInfo.InvariantCulture, " : ", null, ")
                    .Append(segment.EscapeMethod is null ? "null, null" : $"{segment.EscapeMethod.Replace("__WriteEscaped_", "__SpecialBytes_")}, {segment.EscapeMethod}")
                    .Append(");\n");
                return indent;

            case SegmentKind.FormattedValue:
                source.Append(indent).Append(segment.EscapeMethod ?? "global::TemplateDsl.TemplateValues.Write").Append('(').Append(segment.Text).Append(", __writer");

                if (segment.ValueFormat is not null || segment.IsInvariant)
                {
                    source.Append(", ").Append(segment.ValueFormat is null ? "null" : SymbolDisplay.FormatLiteral(segment.ValueFormat, quote: true));
                }

                source.Append(segment.IsInvariant ? ", global::System.Globalization.CultureInfo.InvariantCulture" : "").Append(");\n");
                return indent;

            case SegmentKind.OptionalAttribute:
                source.Append(indent).Append("{\n");
                source.Append(indent).Append("    string? __value = ").Append(segment.Text).Append(";\n");
                source.Append(indent).Append("    if (__value is not null)\n").Append(indent).Append("    {\n");
                source.Append(indent).Append("        ").Append(LiteralWrite(segment.AttributePrefix!)).Append('\n');
                source.Append(indent).Append("        ").Append(ValueWrite("__value", segment.EscapeMethod)).Append('\n');
                source.Append(indent).Append("        ").Append(LiteralWrite(segment.AttributeSuffix!)).Append('\n');
                source.Append(indent).Append("    }\n").Append(indent).Append("}\n");
                return indent;

            default:
                throw new InvalidOperationException($"Segment {segment.Kind} has to be handled by the form of the render method.");
        }
    }

    /// <summary>The copied expressions use the context under the name Build gave it.</summary>
    private static void EmitContextAlias(StringBuilder source, TemplateModel template, string indent)
    {
        if (template.ContextParameter is not null)
        {
            source.Append(indent).Append(template.ContextType).Append(' ').Append(template.ContextParameter).Append(" = __context;\n");
        }
    }

    /// <summary>What templates with children need on top of their render methods. Everything else is in TemplateRendering.</summary>
    private static void EmitPublicMethods(StringBuilder source, TemplateModel template)
    {
        var parameter = template.ContextType is null ? "" : $", {template.ContextType} context";
        var argument = template.ContextType is null ? "" : ", context";

        if (template.IsAsync)
        {
            // Without children, the template's code itself implements the interface.
            if (template.AcceptsChildren)
            {
                source.Append($$"""

                        /// <summary>Used by the render methods: renders without children.</summary>
                        {{Hidden}}
                        public override global::System.Threading.Tasks.ValueTask __RenderAsync(global::System.IO.Pipelines.PipeWriter writer{{parameter}}, global::System.Threading.CancellationToken cancellationToken)
                        {
                            return __RenderWithChildrenAsync(writer{{argument}}, cancellationToken, null);
                        }

                    """);
            }

            return;
        }

        if (!template.AcceptsChildren)
        {
            return;
        }

        source.Append($$"""

                /// <summary>Writes this template as UTF-8 to <paramref name="writer"/>, without flushing.</summary>
                public override void RenderTo(global::System.Buffers.IBufferWriter<byte> writer{{parameter}})
                {
                    __RenderWithChildren(writer{{argument}}, null);
                }

            """);
    }

    private static void EmitChildrenMembers(StringBuilder source, TemplateModel template)
    {
        source.Append($$"""

                /// <summary>The children given to this template. Place this in Build where they should go.</summary>
                private {{template.FormatType}} Children => throw global::TemplateDsl.TemplateBuild.NotExecutable();

                /// <summary>The children of this template, in the Build method of another one.</summary>
                public {{template.Name}} this[params {{template.FormatType}}?[] children] => throw global::TemplateDsl.TemplateBuild.NotExecutable();

            """);
    }

    private static string LiteralWrite(string text) =>
        $"global::System.Buffers.BuffersExtensions.Write(__writer, {SymbolDisplay.FormatLiteral(text, quote: true)}u8);";

    private static string ValueWrite(string expression, string? escapeMethod) =>
        escapeMethod is null
            ? $"global::System.Text.EncodingExtensions.GetBytes(global::System.Text.Encoding.UTF8, global::System.MemoryExtensions.AsSpan({expression}), __writer);"
            : $"{escapeMethod}({expression}, __writer);";
}
