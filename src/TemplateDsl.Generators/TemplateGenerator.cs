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
        var buildMethodName = isAsync ? "BuildAsync" : "Build";
        var buildMethod = FindBuildMethod(symbol, buildMethodName, (isAsync ? 1 : 0) + (contextType is null ? 0 : 1));
        var hasBuild = buildMethod?.Body is not null || buildMethod?.ExpressionBody is not null;

        // A template derived from another one has its render methods already, unless it has a Build of its own to render.
        var isDerived = FormatReader.BaseTypes(symbol).Any(baseType => baseType.AllInterfaces.Any(FormatReader.IsTemplateInterface));

        if (isDerived && !hasBuild)
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
        string? cancellationParameter = null;
        string? contextParameter = null;
        var acceptsChildren = FormatReader.Implements(symbol, FormatReader.AcceptsChildren);
        var format = template.TypeArguments[0];
        var isFormat = FormatReader.IsFormat(format);

        if (templates.Length > 1)
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.BothSyncAndAsync, declaration.Identifier.GetLocation(), symbol.Name));
        }

        if (!isFormat)
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.InvalidFormat, declaration.Identifier.GetLocation(), $"'{format.Name}' is not marked with [TemplateFormat]"));
        }

        // Generated members can only be added to a partial top-level type. Everything else is still generated when
        // something is wrong, so that the reason is the only error, instead of errors about what's missing.
        var canGenerate = declaration.Modifiers.Any(SyntaxKind.PartialKeyword)
            && symbol.ContainingType is null
            && !symbol.IsFileLocal
            && !symbol.IsRefLikeType
            && isFormat;

        var segments = canGenerate ? Array.Empty<Segment>() : null;

        // An abstract template without a body for Build is a contract. Any other Build is translated, also to report a missing body.
        if (canGenerate && diagnostics.Count == 0 && buildMethod is not null && (hasBuild || !symbol.IsAbstract))
        {
            var translator = new BuildTranslator(context.SemanticModel.Compilation, format, contextType, isAsync, acceptsChildren, cancellationToken);

            // The method was found by its number of parameters, so these exist.
            cancellationParameter = isAsync ? buildMethod!.ParameterList.Parameters.Last().Identifier.ToString() : null;
            contextParameter = contextType is null ? null : buildMethod!.ParameterList.Parameters.First().Identifier.ToString();
            segments = translator.Translate(buildMethod!);
            diagnostics.AddRange(translator.Diagnostics);
            usings.AddRange(BuildTranslator.GetUsings(buildMethod!));
        }

        var containingNamespace = symbol.ContainingNamespace;
        var formatType = format.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var contextTypeName = contextType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // Without Build, an abstract template is a contract that others derive from, so its render methods are abstract.
        // A type that isn't abstract and has no Build is compiler error CS0535 already, so its methods render nothing.
        var modifier = isDerived ? "override "
            : symbol.IsAbstract && !hasBuild ? "abstract "
            : symbol.IsSealed || symbol.IsValueType ? ""
            : "virtual ";

        return new TemplateModel(
            containingNamespace.IsGlobalNamespace ? null : containingNamespace.ToDisplayString(),
            // With its type parameters, which a partial declaration repeats without their constraints.
            symbol.IsGenericType ? $"{declaration.Identifier}<{string.Join(", ", symbol.TypeParameters.Select(parameter => parameter.Name))}>" : declaration.Identifier.ToString(),
            symbol.MetadataName,
            symbol.IsRecord ? (symbol.IsValueType ? "record struct" : "record") : symbol.IsValueType ? "struct" : "class",
            new EquatableArray<string>(usings.Distinct().ToArray()),
            segments is null ? null : new EquatableArray<Segment>(segments),
            acceptsChildren,
            isAsync,
            cancellationParameter,
            contextTypeName,
            contextParameter,
            formatType,
            modifier,
            isDerived || !acceptsChildren ? null : $"global::TemplateDsl.{(isAsync ? "IChildrenRenderer" : "ISyncChildrenRenderer")}<{contextTypeName ?? "object"}>",
            symbol.IsAbstract && buildMethod is null ? GetAbstractBuild(isAsync, formatType, contextTypeName) : null,
            // A template that is a node already needs none, and C# doesn't allow one to a base class.
            HasConversion: !isDerived && !isAsync && !FormatReader.BaseTypes(symbol).Any(baseType => SymbolEqualityComparer.Default.Equals(baseType, format)),
            new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()));
    }


    /// <summary>The Build method of the template interface, declared abstract, for a contract that doesn't declare it itself.</summary>
    private static string GetAbstractBuild(bool isAsync, string formatType, string? contextType)
    {
        var context = contextType is null ? "" : contextType + " context";

        return isAsync
            ? $"public abstract global::System.Threading.Tasks.Task<global::System.Collections.Generic.IEnumerable<{formatType}>> BuildAsync({(context.Length == 0 ? "" : context + ", ")}global::System.Threading.CancellationToken cancellationToken);"
            : $"public abstract global::System.Collections.Generic.IEnumerable<{formatType}> Build({context});";
    }

    private static List<DiagnosticInfo> CheckShape(INamedTypeSymbol symbol, TypeDeclarationSyntax declaration)
    {
        var diagnostics = new List<DiagnosticInfo>();
        var location = declaration.Identifier.GetLocation();

        if (!declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.MustBePartial, location, symbol.Name));
        }

        // A ref struct can't be converted to a node, or be held by one.
        if (symbol.IsRefLikeType)
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.UnsupportedTemplateType, location, symbol.Name));
        }

        // A nested type would need its containing types to be partial too, and a file-local one isn't visible to generated code.
        if (symbol.ContainingType is not null || symbol.IsFileLocal)
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.UnsupportedTemplateShape, location, symbol.Name));
        }

        return diagnostics;
    }

    /// <summary>The method by name, because the generated abstract method it may override isn't visible yet.</summary>
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

        source.Append("partial ").Append(template.Keyword).Append(' ').Append(template.Name);

        if (template.Interfaces is not null)
        {
            source.Append(" : ").Append(template.Interfaces);
        }

        source.Append("\n{\n");

        if (template.AbstractBuild is not null)
        {
            source.Append("    /// <summary>What this template outputs, which the templates derived from it describe.</summary>\n    ").Append(template.AbstractBuild).Append("\n\n");
        }

        if (template.Modifier == "abstract ")
        {
            EmitAbstractMembers(source, template);
        }
        else
        {
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
        }

        if (template.HasConversion)
        {
            EmitConversion(source, template);
        }

        // Once, at the root. Derived templates inherit it, as it calls the virtual render methods.
        if (template.Modifier != "override ")
        {
            EmitContentRendering(source, template);
        }

        if (template.AcceptsChildren)
        {
            EmitChildrenMembers(source, template);
        }

        source.Append("}\n");
        return source.ToString();
    }

    private const string BufferWriter = "global::System.Buffers.IBufferWriter<byte>";
    private const string PipeWriter = "global::System.IO.Pipelines.PipeWriter";
    private const string Token = "global::System.Threading.CancellationToken";
    private const string ValueTask = "global::System.Threading.Tasks.ValueTask";

    /// <summary>
    /// Children given to a nested template. They become a method of this template, where they can use its members as
    /// written, and a struct holding what else they need, which the nested template calls where it places them.
    /// </summary>
    private sealed class ChildrenBlock(string name, Segment start, List<Segment> segments)
    {
        public string Name { get; } = name;

        public Segment Start { get; } = start;

        public List<Segment> Segments { get; } = segments;

        /// <summary>The struct with its type argument, the children of this template it passes on.</summary>
        public string Type => Start.ForwardsChildren ? Name + "<__TChildren>" : Name;

        public string Method => "__Write" + Name.Substring(2);
    }

    /// <summary>What one form of the render method collects while it is written: the children blocks to write after it.</summary>
    private sealed class CoreState(bool isAsync)
    {
        public bool IsAsync { get; } = isAsync;

        public Queue<ChildrenBlock> Blocks { get; } = new();

        public int NextBlock { get; set; }

        public string ChildrenWriter => IsAsync ? "global::TemplateDsl.IAsyncChildrenWriter" : "global::TemplateDsl.IChildrenWriter";
    }

    private static void EmitSyncCore(StringBuilder source, TemplateModel template)
    {
        var state = new CoreState(isAsync: false);
        var context = template.ContextType is null ? "" : $", {template.ContextType} __context";

        // The parameters have these names so that the copied expressions can't see or clash with them,
        // which an override is free to do, even on the public RenderTo.
        if (template.AcceptsChildren)
        {
            source.Append($"    {Hidden}\n    public {template.Modifier}void __RenderWithChildren<__TChildren>({BufferWriter} __writer, {template.ContextType ?? "object"} __context, __TChildren __children){ChildrenConstraint(template, state)}\n    {{\n");
        }
        else
        {
            source.Append($"    /// <summary>Writes this template as UTF-8 to <paramref name=\"__writer\"/>, without flushing.</summary>\n    public {template.Modifier}void RenderTo({BufferWriter} __writer{context})\n    {{\n");
        }

        EmitContextAlias(source, template, "        ");
        EmitBody(source, template, state, template.Segments!.Value.ToList(), "        ");
        source.Append("    }\n\n");
        EmitBlocks(source, template, state);
    }

    /// <summary>
    /// The async form of the render method: the same segments, with a flush checkpoint after every loop iteration
    /// and every sync child, and nested templates that can wait too. If the client is gone, an
    /// OperationCanceledException unwinds all of them.
    /// </summary>
    private static void EmitAsyncCore(StringBuilder source, TemplateModel template)
    {
        var state = new CoreState(isAsync: true);
        var context = template.ContextType is null ? "" : $", {template.ContextType} __context";

        // The template's code. It's public because other templates call it, and hidden because nobody else should.
        // The parameters have these names so that the copied expressions can't see or clash with them.
        source.Append("    ").Append(Hidden).Append('\n');
        source.Append(template.AcceptsChildren
            ? $"    public {template.Modifier}async {ValueTask} __RenderWithChildrenAsync<__TChildren>({PipeWriter} __writer, {template.ContextType ?? "object"} __context, __TChildren __children, {Token} __ct){ChildrenConstraint(template, state)}\n    {{\n"
            : $"    public {template.Modifier}async {ValueTask} __RenderAsync({PipeWriter} __writer{context}, {Token} __ct)\n    {{\n");

        EmitCancellationAlias(source, template, "        ");
        EmitContextAlias(source, template, "        ");
        EmitBody(source, template, state, template.Segments!.Value.ToList(), "        ");
        source.Append("        ").Append(Checkpoint).Append("\n    }\n\n");
        EmitBlocks(source, template, state);
    }

    // An override inherits the constraint, and C# doesn't allow it to repeat it.
    private static string ChildrenConstraint(TemplateModel template, CoreState state) =>
        template.Modifier == "override " ? "" : $" where __TChildren : {state.ChildrenWriter}";

    /// <summary>Writes the code of these segments. Children given to a nested template are queued as a block, and written after.</summary>
    private static void EmitBody(StringBuilder source, TemplateModel template, CoreState state, List<Segment> segments, string indent)
    {
        var nextTemporary = 0;

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];

            switch (segment.Kind)
            {
                case SegmentKind.FlushBeforeAwait:
                    source.Append(indent).Append("await global::TemplateDsl.TemplateStreaming.FlushBufferedAsync(__writer, __ct).ConfigureAwait(false);\n");
                    break;

                case SegmentKind.LoopEnd when state.IsAsync:
                    source.Append(indent).Append(Checkpoint).Append('\n');
                    indent = EmitShared(source, segment, indent);
                    break;

                case SegmentKind.Template when segment.IsContent:
                {
                    // Sync content of an interface or type parameter type, which may need the context or not.
                    var temporary = $"__t{nextTemporary++}";
                    var context = template.ContextType is null ? "null!" : "__context";
                    var render = segment.IsSyncChild
                        ? $"{temporary}.__RenderContent(__writer, {context});"
                        : $"await {temporary}.__RenderContentAsync(__writer, {context}, __ct).ConfigureAwait(false);";

                    // A null test of a type parameter boxes a struct, unless the JIT optimizes it away, which it only does in
                    // optimized code. A struct is never null, so its test is skipped instead.
                    if (segment.TypeParameter is { } typeParameter)
                    {
                        source.Append(indent).Append("var ").Append(temporary).Append(" = ").Append(segment.Text).Append(";\n");
                        source.Append(indent).Append("if (typeof(").Append(typeParameter).Append(").IsValueType || (object?)").Append(temporary).Append(" is not null) ").Append(render).Append('\n');
                    }
                    else
                    {
                        source.Append(indent).Append("if (").Append(Matches(segment, temporary)).Append(") ").Append(render).Append('\n');
                    }

                    // Async content sends its output itself. This is where the output of sync content gets the chance to be sent.
                    if (state.IsAsync && segment.IsSyncChild)
                    {
                        source.Append(indent).Append(Checkpoint).Append('\n');
                    }

                    break;
                }

                case SegmentKind.Template:
                {
                    var temporary = $"__t{nextTemporary++}";

                    if (!state.IsAsync)
                    {
                        source.Append(indent).Append("if (").Append(Matches(segment, temporary)).Append(") ").Append(temporary)
                            .Append(".RenderTo(__writer").Append(ContextArgument(segment)).Append(");\n");
                    }
                    else if (segment.IsSyncChild)
                    {
                        // A sync template has no async code. This is where its output gets the chance to be sent.
                        source.Append(indent).Append("if (").Append(Matches(segment, temporary)).Append(")\n").Append(indent).Append("{\n");
                        source.Append(indent).Append("    ").Append(temporary).Append(".RenderTo(__writer").Append(ContextArgument(segment)).Append(");\n");
                        source.Append(indent).Append("    ").Append(Checkpoint).Append('\n');
                        source.Append(indent).Append("}\n");
                    }
                    else
                    {
                        source.Append(indent).Append("if (").Append(Matches(segment, temporary)).Append(") await ").Append(temporary)
                            .Append(".__RenderAsync(__writer").Append(ContextArgument(segment)).Append(", __ct).ConfigureAwait(false);\n");
                    }

                    break;
                }

                case SegmentKind.TemplateWithChildrenStart:
                {
                    var end = FindChildrenEnd(segments, i);
                    var block = new ChildrenBlock($"__{(state.IsAsync ? "Async" : "")}Children{state.NextBlock++}", segment, segments.GetRange(i + 1, end - i - 1));
                    var temporary = $"__t{nextTemporary++}";
                    var children = $"new {block.Type}({ChildrenArguments(template, block)})";

                    state.Blocks.Enqueue(block);
                    source.Append(indent).Append("if (").Append(Matches(segment, temporary)).Append(") ").Append(state.IsAsync
                        ? $"await {temporary}.__RenderWithChildrenAsync(__writer, {ChildrenContext(segment)}, {children}, __ct).ConfigureAwait(false);\n"
                        : $"{temporary}.__RenderWithChildren(__writer, {ChildrenContext(segment)}, {children});\n");
                    i = end;
                    break;
                }

                case SegmentKind.Children:
                    source.Append(indent).Append(state.IsAsync
                        ? "await __children.__WriteAsync(__writer, __ct).ConfigureAwait(false);\n"
                        : "__children.__Write(__writer);\n");
                    break;

                default:
                    indent = EmitShared(source, segment, indent);
                    break;
            }
        }
    }

    /// <summary>The end of the children that start at <paramref name="start"/>, which can contain children of their own.</summary>
    private static int FindChildrenEnd(List<Segment> segments, int start)
    {
        var depth = 0;

        for (var i = start; ; i++)
        {
            depth += segments[i].Kind switch
            {
                SegmentKind.TemplateWithChildrenStart => 1,
                SegmentKind.TemplateWithChildrenEnd => -1,
                _ => 0,
            };

            if (depth == 0)
            {
                return i;
            }
        }
    }

    /// <summary>What the struct of a block holds: this template, its context, the variables the children use, and its own children.</summary>
    private static string ChildrenArguments(TemplateModel template, ChildrenBlock block) =>
        string.Join(", ", Enumerable.Repeat("this", 1)
            .Concat(template.ContextType is null ? [] : ["__context"])
            .Concat(block.Start.Captures!.Value.Select(capture => capture.Name))
            .Concat(block.Start.ForwardsChildren ? ["__children"] : []));

    /// <summary>Writes the queued blocks, each a struct and a method, which can queue more for children inside children.</summary>
    private static void EmitBlocks(StringBuilder source, TemplateModel template, CoreState state)
    {
        while (state.Blocks.Count > 0)
        {
            var block = state.Blocks.Dequeue();
            var generic = block.Start.ForwardsChildren ? "<__TChildren>" : "";
            var constraint = block.Start.ForwardsChildren ? $" where __TChildren : {state.ChildrenWriter}" : "";
            var fields = new List<(string Type, string Name)> { (template.Name, "__self") };

            if (template.ContextType is not null)
            {
                fields.Add((template.ContextType, "__context"));
            }

            fields.AddRange(block.Start.Captures!.Value.Select(capture => (capture.Type, capture.Name)));

            if (block.Start.ForwardsChildren)
            {
                fields.Add(("__TChildren", "__children"));
            }

            source.Append($"    {Hidden}\n    private readonly struct {block.Name}{generic} : {state.ChildrenWriter}{constraint}\n    {{\n");

            foreach (var (type, name) in fields)
            {
                source.Append($"        internal readonly {type} {name};\n");
            }

            source.Append($"\n        public {block.Name}({string.Join(", ", fields.Select(field => $"{field.Type} {field.Name}"))})\n        {{\n");

            foreach (var (_, name) in fields)
            {
                source.Append($"            this.{name} = {name};\n");
            }

            source.Append("        }\n\n");
            source.Append(state.IsAsync
                ? $"        public {ValueTask} __WriteAsync({PipeWriter} __writer, {Token} __ct) => __self.{block.Method}(__writer, this, __ct);\n"
                : $"        public void __Write({BufferWriter} __writer) => __self.{block.Method}(__writer, this);\n");
            source.Append("    }\n\n");

            source.Append(state.IsAsync
                ? $"    private async {ValueTask} {block.Method}{generic}({PipeWriter} __writer, {block.Type} __state, {Token} __ct){constraint}\n    {{\n"
                : $"    private void {block.Method}{generic}({BufferWriter} __writer, {block.Type} __state){constraint}\n    {{\n");

            const string Indent = "        ";

            if (state.IsAsync)
            {
                EmitCancellationAlias(source, template, Indent);
            }

            foreach (var (_, name) in fields.Skip(1))
            {
                source.Append(Indent).Append("var ").Append(name).Append(" = __state.").Append(name).Append(";\n");
            }

            EmitContextAlias(source, template, Indent);
            EmitBody(source, template, state, block.Segments, Indent);
            source.Append("    }\n\n");
        }
    }

    /// <summary>The copied expressions use the token under the name BuildAsync gave it.</summary>
    private static void EmitCancellationAlias(StringBuilder source, TemplateModel template, string indent)
    {
        if (template.CancellationParameter is not null)
        {
            source.Append(indent).Append(Token).Append(' ').Append(template.CancellationParameter).Append(" = __ct;\n");
        }
    }

    /// <summary>
    /// Tests that a nested template isn't null and puts it in a variable, typed as what has its render methods. A
    /// pattern, because a struct can't be compared with null, and an interface or type parameter needs a cast.
    /// </summary>
    private static string Matches(Segment segment, string temporary) =>
        $"({segment.Text}) is {segment.Pattern ?? "{ }"} {temporary}";


    private static string ContextArgument(Segment segment) => segment.PassContext ? ", __context" : "";

    /// <summary>The children methods always take a context, which a template without one doesn't use.</summary>
    private static string ChildrenContext(Segment segment) => segment.PassContext ? "__context" : "null!";

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

        if (template.IsAsync)
        {
            // Without children, the template's code itself implements the interface.
            if (template.AcceptsChildren)
            {
                source.Append($$"""

                        /// <summary>Used by the render methods: renders without children.</summary>
                        {{Hidden}}
                        public {{template.Modifier}}global::System.Threading.Tasks.ValueTask __RenderAsync(global::System.IO.Pipelines.PipeWriter writer{{parameter}}, global::System.Threading.CancellationToken cancellationToken)
                        {
                            return __RenderWithChildrenAsync(writer, {{(template.ContextType is null ? "null!" : "context")}}, default(global::TemplateDsl.NoChildren), cancellationToken);
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
                public {{template.Modifier}}void RenderTo(global::System.Buffers.IBufferWriter<byte> writer{{parameter}})
                {
                    __RenderWithChildren(writer, {{(template.ContextType is null ? "null!" : "context")}}, default(global::TemplateDsl.NoChildren));
                }

            """);
    }

    /// <summary>The render methods of a template without Build, which the templates derived from it implement.</summary>
    private static void EmitAbstractMembers(StringBuilder source, TemplateModel template)
    {
        const string Writer = "global::System.Buffers.IBufferWriter<byte> writer";
        const string Pipe = "global::System.IO.Pipelines.PipeWriter writer";
        const string Token = "global::System.Threading.CancellationToken cancellationToken";
        const string Task = "global::System.Threading.Tasks.ValueTask";
        var context = template.ContextType is null ? "" : $", {template.ContextType} context";

        if (!template.IsAsync)
        {
            source.Append($"    /// <summary>Writes this template as UTF-8 to <paramref name=\"writer\"/>, without flushing.</summary>\n    public abstract void RenderTo({Writer}{context});\n\n");
        }
        else
        {
            source.Append($"    {Hidden}\n    public abstract {Task} __RenderAsync({Pipe}{context}, {Token});\n\n");
        }

        if (template.AcceptsChildren)
        {
            var childrenContext = $"{template.ContextType ?? "object"} context";
            source.Append($"    {Hidden}\n    public abstract {Task} __RenderWithChildrenAsync<TChildren>({Pipe}, {childrenContext}, TChildren children, {Token}) where TChildren : global::TemplateDsl.IAsyncChildrenWriter;\n\n");

            if (!template.IsAsync)
            {
                source.Append($"    {Hidden}\n    public abstract void __RenderWithChildren<TChildren>({Writer}, {childrenContext}, TChildren children) where TChildren : global::TemplateDsl.IChildrenWriter;\n\n");
            }
        }
    }

    /// <summary>
    /// Lets a sync template be a node in Build, where it is rendered in place, as at the top level, in a ternary or in
    /// Each. Like the conversion of strings, it never runs, and the usage analyzer reports it outside of Build. Content
    /// is passed around as <c>IHtmlContent</c> and the like, which brackets take too.
    /// </summary>
    private static void EmitConversion(StringBuilder source, TemplateModel template)
    {
        source.Append($"\n    /// <summary>Lets this template be a node in Build.</summary>\n    {Hidden}\n");

        if (template.Keyword.EndsWith("struct", StringComparison.Ordinal))
        {
            source.Append($"    public static implicit operator {template.FormatType}({template.Name} template) => throw global::TemplateDsl.TemplateBuild.NotExecutable();\n");
        }
        else
        {
            source.Append("    [return: global::System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(template))]\n")
                .Append($"    public static implicit operator {template.FormatType}?({template.Name}? template) => throw global::TemplateDsl.TemplateBuild.NotExecutable();\n");
        }
    }

    /// <summary>
    /// How a parent renders this template when it has it as content, of an interface or type parameter type: directly,
    /// so that a struct isn't boxed, with the parent's context, which this uses only if it needs one.
    /// </summary>
    private static void EmitContentRendering(StringBuilder source, TemplateModel template)
    {
        var context = template.ContextType ?? "object";
        var argument = template.ContextType is null ? "" : ", context";
        var asyncContent = $"{ValueTask} global::TemplateDsl.IAsyncTemplateContent<{template.FormatType}, {context}>.__RenderContentAsync({PipeWriter} writer, {context} context, {Token} cancellationToken)";

        if (template.IsAsync)
        {
            source.Append($"\n    {Hidden}\n    {asyncContent} => __RenderAsync(writer{argument}, cancellationToken);\n");
            return;
        }

        source.Append($"\n    {Hidden}\n    void global::TemplateDsl.ITemplateContent<{template.FormatType}, {context}>.__RenderContent({BufferWriter} writer, {context} context) => RenderTo(writer{argument});\n");

        // A sync template has no async code. This is where its output gets the chance to be sent, as for a sync child.
        source.Append($"\n    {Hidden}\n    {asyncContent}\n    {{\n        RenderTo(writer{argument});\n        return global::TemplateDsl.TemplateStreaming.FlushIfNeededAsync(writer, cancellationToken);\n    }}\n");
    }

    private static void EmitChildrenMembers(StringBuilder source, TemplateModel template)
    {
        source.Append($$"""

                /// <summary>The children given to this template. Place this in Build where they should go.</summary>
                private {{template.FormatType}} Children => throw global::TemplateDsl.TemplateBuild.NotExecutable();

                /// <summary>The children of this template, in the Build method of another one.</summary>
                public {{template.Name}} this[params global::TemplateDsl.TemplateChildren<{{template.FormatType}}> children] => throw global::TemplateDsl.TemplateBuild.NotExecutable();

            """);
    }

    private static string LiteralWrite(string text) =>
        $"global::System.Buffers.BuffersExtensions.Write(__writer, {SymbolDisplay.FormatLiteral(text, quote: true)}u8);";

    private static string ValueWrite(string expression, string? escapeMethod) =>
        escapeMethod is null
            ? $"global::System.Text.EncodingExtensions.GetBytes(global::System.Text.Encoding.UTF8, global::System.MemoryExtensions.AsSpan({expression}), __writer);"
            : $"{escapeMethod}({expression}, __writer);";
}
