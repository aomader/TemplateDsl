using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TemplateDsl.Generators;

/// <summary>
/// Translates the body of a template's Build method into the segments it writes: literal output, values
/// and code that are copied into the generated method. Anything it can't translate is reported instead of being guessed at.
/// </summary>
internal sealed class BuildTranslator(
    Compilation compilation,
    ITypeSymbol format,
    ITypeSymbol? contextType,
    bool isAsync,
    bool acceptsChildren,
    CancellationToken cancellationToken)
{
    private readonly EscapeRule[] _escapeRules = FormatReader.ReadEscapeRules(format);

    /// <summary>Looked up once, to compare symbols instead of names. Null if the target framework doesn't have it.</summary>
    private readonly INamedTypeSymbol? _asyncEnumerable = compilation.GetTypeByMetadataName("System.Collections.Generic.IAsyncEnumerable`1");

    // A format without a proper text node is reported where it's declared, so this only has to not get in the way.
    private readonly NodeInfo _stringNode = FormatReader.ReadTextNode(format) is { } textNode && FormatReader.ReadNode(textNode) is { IsText: true } node
        ? node
        : new NodeInfo("", "", "", IsText: true, TextEscape: null, TextPrefix: "", TextSuffix: "", Separator: null, new EquatableArray<AttributeInfo>([]));

    private readonly bool _hasInvariantText = FormatReader.HasInvariantText(format);
    private readonly StringBuilder _literal = new();

    // The separator of the node whose children are being written, if it has one.
    private (string Variable, string Text)? _separator;
    private int _nextVariable;

    private readonly List<Segment> _segments = [];
    private readonly List<DiagnosticInfo> _diagnostics = [];
    private SemanticModel _semanticModel = null!;

    public IReadOnlyList<DiagnosticInfo> Diagnostics => _diagnostics;

    /// <summary>Returns the segments of the method. Without any if it can't be translated, so that the class is still complete and the reason is the only error.</summary>
    public Segment[] Translate(MethodDeclarationSyntax method)
    {
        _semanticModel = compilation.GetSemanticModel(method.SyntaxTree);

        if (method.ExpressionBody is { } arrow)
        {
            WriteTopLevel(arrow.Expression);
        }
        else if (method.Body is { } body)
        {
            WriteStatements(body);
        }
        else
        {
            Report(method.Identifier.GetLocation(), "the method must have a body");
        }

        FlushLiteral();
        return _diagnostics.Count == 0 ? _segments.ToArray() : [];
    }

    /// <summary>
    /// The using directives that apply to the method, which the copied expressions may depend on.
    /// Global usings are left out because they apply to the generated file anyway.
    /// </summary>
    public static IEnumerable<string> GetUsings(MethodDeclarationSyntax method)
    {
        var fromNamespaces = method.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .SelectMany(declaration => declaration.Usings);

        var fromFile = method.SyntaxTree.GetRoot().DescendantNodes(node => node is CompilationUnitSyntax)
            .OfType<UsingDirectiveSyntax>();

        return fromFile
            .Concat(fromNamespaces)
            .Where(directive => !directive.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword))
            .Select(directive => directive.ToString());
    }

    /// <summary>Writes the body of a method or a branch, without adding a scope of its own.</summary>
    private void WriteStatements(StatementSyntax statement)
    {
        if (statement is BlockSyntax block)
        {
            foreach (var inner in block.Statements)
            {
                WriteStatement(inner);
            }
        }
        else
        {
            WriteStatement(statement);
        }
    }

    private void WriteStatement(StatementSyntax statement)
    {
        switch (statement)
        {
            case IfStatementSyntax ifStatement:
                WriteConditional(
                    ifStatement.Condition,
                    () => WriteStatements(ifStatement.Statement),
                    ifStatement.Else is { } elseClause ? () => WriteStatements(elseClause.Statement) : null);
                break;

            case LocalDeclarationStatementSyntax or ExpressionStatementSyntax or ThrowStatementSyntax:
                WriteCopiedStatement(statement);
                break;

            case BlockSyntax block:
                Add(SegmentKind.BlockStart);
                WriteStatements(block);
                Add(SegmentKind.BlockEnd);
                break;

            case SwitchStatementSyntax switchStatement:
                WriteSwitchStatement(switchStatement);
                break;

            case CommonForEachStatementSyntax { AwaitKeyword.RawKind: 0 } forEach:
                WriteLoop(forEach, forEach.CloseParenToken, forEach.Statement);
                break;

            case ForStatementSyntax forStatement:
                WriteLoop(forStatement, forStatement.CloseParenToken, forStatement.Statement);
                break;

            case WhileStatementSyntax whileStatement:
                WriteLoop(whileStatement, whileStatement.CloseParenToken, whileStatement.Statement);
                break;

            case BreakStatementSyntax:
                Add(SegmentKind.Statement, "break;");
                break;

            case ContinueStatementSyntax:
                Add(SegmentKind.Statement, "continue;");
                break;

            case ReturnStatementSyntax { Expression: { } returned }:
                WriteTopLevel(returned);
                Add(SegmentKind.Statement, "return;");
                break;

            // Only yield return has an expression.
            case YieldStatementSyntax { Expression: { } yielded }:
                WriteElement(yielded);
                break;

            case YieldStatementSyntax:
                Add(SegmentKind.Statement, "return;");
                break;

            default:
                Report(statement.GetLocation(),
                    $"'{statement}' is not supported; only local variables, expressions, blocks and if, switch, foreach, for, while, break, continue, return, throw and yield statements are");
                break;
        }
    }

    /// <summary>Code that doesn't write anything itself, which runs where it stands, between the output around it.</summary>
    private void WriteCopiedStatement(StatementSyntax statement)
    {
        if (statement.DescendantNodes().OfType<ExpressionSyntax>().FirstOrDefault(CreatesNode) is { } node)
        {
            Report(node.GetLocation(), $"'{node}' can only be part of the output, not of a variable or a statement; output it directly, or create a template for it");
            return;
        }

        FlushIfAwaits(statement);
        Add(SegmentKind.Statement, statement.ToString());
    }

    /// <summary>Nodes don't exist at runtime, so one in copied code would be thrown away or fail. Templates do exist.</summary>
    private bool CreatesNode(ExpressionSyntax expression)
    {
        if (expression.Parent is TypeOfExpressionSyntax)
        {
            return false;
        }

        if (acceptsChildren && IsChildrenReference(expression))
        {
            return true;
        }

        var type = _semanticModel.GetTypeInfo(expression, cancellationToken).Type;

        return type is not null
            && (SymbolEqualityComparer.Default.Equals(type, format) || DerivesFromFormat(type))
            && !FormatReader.Implements(type, FormatReader.Renderable);
    }

    private void WriteSwitchStatement(SwitchStatementSyntax statement)
    {
        FlushIfAwaits(statement.Expression);

        foreach (var label in statement.Sections.SelectMany(section => section.Labels))
        {
            FlushIfAwaits(label);
        }

        // Between two values, as in switch (a, b), the parentheses are part of the expression.
        Add(SegmentKind.BlockStart, statement.OpenParenToken.IsKind(SyntaxKind.None) ? $"switch {statement.Expression}" : $"switch ({statement.Expression})");

        foreach (var section in statement.Sections)
        {
            Add(SegmentKind.Statement, string.Join(" ", section.Labels.Select(label => label.ToString())));

            foreach (var inner in section.Statements)
            {
                WriteStatement(inner);
            }
        }

        Add(SegmentKind.BlockEnd);
    }

    /// <summary>
    /// A switch expression whose arms are output, as a switch statement. Like the expression, it
    /// throws if nothing matches, which a statement wouldn't do by itself.
    /// </summary>
    private void WriteSwitchExpression(SwitchExpressionSyntax expression, Action<ExpressionSyntax> writeArm)
    {
        FlushIfAwaits(expression.GoverningExpression);

        foreach (var arm in expression.Arms)
        {
            FlushIfAwaits(arm.Pattern);

            if (arm.WhenClause is { } when)
            {
                FlushIfAwaits(when);
            }
        }

        Add(SegmentKind.BlockStart, $"switch ({expression.GoverningExpression})");

        var hasDefault = false;

        foreach (var arm in expression.Arms)
        {
            var isDefault = arm.Pattern is DiscardPatternSyntax && arm.WhenClause is null;
            hasDefault |= isDefault;

            Add(SegmentKind.Statement, isDefault ? "default:" : $"case {arm.Pattern}{(arm.WhenClause is { } when ? " " + when : "")}:");

            if (Unwrap(arm.Expression) is ThrowExpressionSyntax thrown)
            {
                WriteThrow(thrown);
            }
            else
            {
                writeArm(arm.Expression);
                Add(SegmentKind.Statement, "break;");
            }
        }

        if (!hasDefault)
        {
            Add(SegmentKind.Statement, "default:");
            Add(SegmentKind.Statement, "throw new global::System.Runtime.CompilerServices.SwitchExpressionException();");
        }

        Add(SegmentKind.BlockEnd);
    }

    private void WriteThrow(ThrowExpressionSyntax thrown) => Add(SegmentKind.Statement, $"{thrown};");

    private void WriteLoop(StatementSyntax loop, SyntaxToken closeParen, StatementSyntax body)
    {
        var header = loop.ToString().Substring(0, closeParen.Span.End - loop.Span.Start);

        if (loop.ChildNodes().Where(node => node != body).Any(ContainsAwait))
        {
            Add(SegmentKind.FlushBeforeAwait);
        }

        Add(SegmentKind.BlockStart, header);
        WriteStatements(body);
        Add(SegmentKind.LoopEnd);
    }

    private bool IsEach(InvocationExpressionSyntax invocation) => GetEachMethod(invocation) is not null;

    private IMethodSymbol? GetEachMethod(InvocationExpressionSyntax invocation)
    {
        // A lambda returning a template only converts once the generated conversion exists.
        var info = _semanticModel.GetSymbolInfo(invocation, cancellationToken);

        return (info.Symbol ?? info.CandidateSymbols.FirstOrDefault()) is IMethodSymbol { Name: "Each" or "EachAsync" } method
            && FormatReader.IsTags(method.ContainingType)
                ? method
                : null;
    }

    /// <summary>
    /// Each is generated too, so it can't be resolved when its factory class is in this same project,
    /// and without it the types of the lambda's parameter are unknown.
    /// </summary>
    private static bool LooksLikeUnresolvedEach(ExpressionSyntax expression) =>
        expression is InvocationExpressionSyntax
        {
            Expression: IdentifierNameSyntax { Identifier.ValueText: "Each" or "EachAsync" }
                or MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Each" or "EachAsync" }
        };

    /// <summary>
    /// <c>await EachAsync(...)</c> and <c>await new Page()</c> only await so that
    /// C# checks their use. What they compile to is the same as without it, and waits where it needs to.
    /// </summary>
    private ExpressionSyntax UnwrapAwaitedTemplateConstruct(ExpressionSyntax expression)
    {
        if (expression is not AwaitExpressionSyntax { Expression: var awaited })
        {
            return expression;
        }

        var operand = Unwrap(awaited);

        var isConstruct = operand is InvocationExpressionSyntax invocation && IsEach(invocation)
            || IsTemplate(operand)
            || TryGetTemplateBrackets(operand, [], out _);

        return isConstruct ? operand : expression;
    }

    /// <summary>
    /// Async content in a sync template. C# reports it, as a missing conversion or an await outside
    /// of an async method, so this only leaves it out to avoid follow-up errors in the generated code.
    /// </summary>
    private bool IsAsyncInSync(ExpressionSyntax template) =>
        !isAsync
        && IsStreaming(template);

    /// <summary>A template in a variable can be null, which leaves no separator behind either.</summary>
    private void WriteTemplateChild(ExpressionSyntax template)
    {
        if (!TryGetPassContext(template, out var passContext))
        {
            return;
        }

        FlushIfAwaits(template);

        if (_separator is null || template is BaseObjectCreationExpressionSyntax)
        {
            WriteSeparator();
            Add(new Segment(SegmentKind.Template, template.ToString(), PassContext: passContext, IsSyncChild: !IsStreaming(template)));
            return;
        }

        var variable = $"__child{_nextVariable++}";

        Add(SegmentKind.Statement, $"var {variable} = {template};");
        Add(SegmentKind.BlockStart, $"if ({variable} is not null)");
        WriteSeparator();
        Add(new Segment(SegmentKind.Template, variable, PassContext: passContext, IsSyncChild: !IsStreaming(template)));
        Add(SegmentKind.BlockEnd);
    }

    /// <summary>Only called for templates, which <see cref="IsTemplate"/> has found to have a type.</summary>
    private bool IsStreaming(ExpressionSyntax template) =>
        FormatReader.Implements(_semanticModel.GetTypeInfo(template, cancellationToken).Type!, FormatReader.Streaming);

    private void WriteEach(InvocationExpressionSyntax invocation)
    {
        var isEachAsync = GetEachMethod(invocation)!.Name == "EachAsync";
        var arguments = invocation.ArgumentList.Arguments;

        // C# reports this, because a Task isn't a node and await needs an async method.
        if (isEachAsync && !isAsync)
        {
            return;
        }

        var lambda = arguments.Count == 2 && arguments.All(a => a.NameColon is null)
            ? arguments[1].Expression as LambdaExpressionSyntax
            : null;

        var parameter = lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Parameter,
            ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters: { Count: 1 } parameters } => parameters[0],
            _ => null,
        };

        // A parameter only comes from a lambda, so with one, there is a lambda.
        if (parameter is null || lambda!.ExpressionBody is null || (!isEachAsync && !lambda.AsyncKeyword.IsKind(SyntaxKind.None)))
        {
            Report(invocation.GetLocation(), "Each needs a collection and a lambda with one parameter and an expression body");
            return;
        }

        var items = arguments[0].Expression;
        var variable = $"{parameter.Type?.ToString() ?? "var"} {parameter.Identifier}";

        FlushIfAwaits(items);
        Add(
            SegmentKind.BlockStart,
            IsAsyncEnumerable(items)
                ? $"await foreach ({variable} in global::System.Threading.Tasks.TaskAsyncEnumerableExtensions.ConfigureAwait(global::TemplateDsl.TemplateStreaming.FlushWhileWaiting({items}, __writer, __ct), false))"
                : $"foreach ({variable} in {items})");
        WriteBranch(lambda.ExpressionBody);
        Add(SegmentKind.LoopEnd);
    }

    private bool IsAsyncEnumerable(ExpressionSyntax expression) =>
        _semanticModel.GetTypeInfo(expression, cancellationToken).Type is { } type
        && FormatReader.Interfaces(type).Any(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, _asyncEnumerable));

    /// <summary>An await in a lambda doesn't run here, so it doesn't count.</summary>
    private static bool ContainsAwait(SyntaxNode node) =>
        node.DescendantNodesAndSelf(child => child is not AnonymousFunctionExpressionSyntax)
            .Any(child => child is AwaitExpressionSyntax);

    private void FlushIfAwaits(SyntaxNode node)
    {
        if (ContainsAwait(node))
        {
            Add(SegmentKind.FlushBeforeAwait);
        }
    }

    private void WriteConditional(ExpressionSyntax condition, Action writeThen, Action? writeElse)
    {
        FlushIfAwaits(condition);
        Add(SegmentKind.BlockStart, $"if ({condition})");
        writeThen();

        if (writeElse is not null)
        {
            Add(SegmentKind.Else);
            writeElse();
        }

        Add(SegmentKind.BlockEnd);
    }

    private void WriteTernary(ConditionalExpressionSyntax conditional, Action<ExpressionSyntax> writeBranch) =>
        WriteConditional(conditional.Condition, () => writeBranch(conditional.WhenTrue), () => writeBranch(conditional.WhenFalse));

    private void Add(SegmentKind kind, string text = "") => Add(new Segment(kind, text));

    private void Add(Segment segment)
    {
        FlushLiteral();
        _segments.Add(segment);
    }

    private void WriteTopLevel(ExpressionSyntax syntax)
    {
        var expression = Unwrap(syntax);

        switch (expression)
        {
            case ConditionalExpressionSyntax conditional:
                WriteTernary(conditional, WriteTopLevel);
                break;

            case SwitchExpressionSyntax switchExpression:
                WriteSwitchExpression(switchExpression, WriteTopLevel);
                break;

            case CollectionExpressionSyntax collection:
                foreach (var element in collection.Elements)
                {
                    if (element is ExpressionElementSyntax { Expression: var item })
                    {
                        WriteElement(item);
                    }
                    else
                    {
                        Report(element.GetLocation(), $"'{element}' is not a template node");
                    }
                }

                break;

            case ArrayCreationExpressionSyntax { Initializer: { } initializer }:
                foreach (var item in initializer.Expressions)
                {
                    WriteElement(item);
                }

                break;

            default:
                Report(expression.GetLocation(), $"'{expression}' must be a collection expression or an array initializer");
                break;
        }
    }

    private void WriteElement(ExpressionSyntax syntax)
    {
        var outermost = UnwrapAwaitedTemplateConstruct(Unwrap(syntax));
        var expression = outermost;

        if (outermost is ConditionalExpressionSyntax conditional)
        {
            WriteTernary(conditional, WriteBranch);
            return;
        }

        if (IsString(outermost))
        {
            WriteStringChild(outermost);
            return;
        }

        if (IsFormattedValue(outermost, out var isNullableChild))
        {
            WriteFormatted(outermost, isNullableChild, _stringNode.TextPrefix, _stringNode.TextSuffix, _stringNode.TextEscape, _hasInvariantText, separate: true);
            return;
        }

        if (outermost is SwitchExpressionSyntax switchExpression)
        {
            WriteSwitchExpression(switchExpression, WriteBranch);
            return;
        }

        if (outermost is InvocationExpressionSyntax possibleEach && IsEach(possibleEach))
        {
            WriteEach(possibleEach);
            return;
        }

        if (acceptsChildren && IsChildrenReference(outermost))
        {
            if (_separator is not null)
            {
                Report(outermost.GetLocation(), "Children can't be placed in a node with a separator, because the separators between them are unknown");
                return;
            }

            Add(SegmentKind.Children);
            return;
        }

        var templateBrackets = new List<ElementAccessExpressionSyntax>();
        if (TryGetTemplateBrackets(outermost, templateBrackets, out var templateWithChildren))
        {
            WriteTemplateWithChildren(templateWithChildren!, templateBrackets);
            return;
        }

        if (IsTemplate(outermost))
        {
            if (IsAsyncInSync(outermost))
            {
                return;
            }

            WriteTemplateChild(outermost);
            return;
        }

        // The brackets end a chain, so there is at most one pair of them.
        BracketedArgumentListSyntax? children = null;
        if (expression is ElementAccessExpressionSyntax access && IsNodeBrackets(access))
        {
            children = access.ArgumentList;
            expression = Unwrap(access.Expression);
        }

        var chain = ResolveChain(expression);

        if (chain is null)
        {
            Report(outermost.GetLocation(), LooksLikeUnresolvedEach(outermost)
                ? $"'{outermost}' can only be used if the factory class of this format is in another project"
                : $"'{outermost}' is not a template node");
            return;
        }

        var node = chain.Node;

        WriteSeparator();

        if (node.IsText)
        {
            WriteTextNode(node, chain.Arguments[0].Expression);
            return;
        }

        if (node.IsVoid && children is not null)
        {
            Report(outermost.GetLocation(), $"'{expression}' can't have children");
            return;
        }

        _literal.Append(node.Start);

        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < chain.Arguments.Count; i++)
        {
            var argument = chain.Arguments[i];
            var parameter = argument.NameColon is { } name
                ? chain.Parameters.First(p => p.Name == name.Name.Identifier.ValueText)
                : chain.Parameters[i];

            WriteFactoryArgument(node, parameter.Name, argument, written);
        }

        foreach (var call in chain.Calls)
        {
            WriteChainCall(node, call, written);
        }

        _literal.Append(node.Body);

        var outerSeparator = _separator;
        _separator = node.Separator is null ? null : ($"__separator{_nextVariable++}", node.Separator);

        if (_separator is { } separator)
        {
            Add(SegmentKind.Statement, $"bool {separator.Variable} = false;");
        }

        if (children is not null)
        {
            WriteChildren(children);
        }

        _separator = outerSeparator;
        _literal.Append(node.End);
    }

    private void WriteFactoryArgument(NodeInfo node, string name, ArgumentSyntax argument, HashSet<string> written)
    {
        var attribute = FindAttribute(node, name);

        if (attribute is null)
        {
            Report(argument.GetLocation(), $"'{name}' is not an attribute of this node");
        }
        else if (!written.Add(attribute.Name))
        {
            Report(argument.GetLocation(), $"'{name}' appears more than once");
        }
        else
        {
            WriteAttribute(attribute, argument.Expression);
        }
    }

    /// <summary>Writes one call of a chain, such as <c>.Id("a")</c> or <c>.Attr("data-id", id)</c>, in source order.</summary>
    private void WriteChainCall(NodeInfo node, InvocationExpressionSyntax call, HashSet<string> written)
    {
        var name = ((MemberAccessExpressionSyntax)call.Expression).Name.Identifier.ValueText;
        var arguments = call.ArgumentList.Arguments;
        var attribute = FindAttribute(node, name);

        if (attribute is not null)
        {
            if (!written.Add(attribute.Name))
            {
                Report(call.GetLocation(), $"'{name}' appears more than once");
            }
            else if (arguments.Count == 1)
            {
                WriteAttribute(attribute, arguments[0].Expression);
            }
            else if (arguments.Count == 0 && attribute.Kind == AttributeKind.Presence)
            {
                _literal.Append(attribute.Prefix).Append(attribute.Suffix);
            }
            else
            {
                Report(call.GetLocation(), $"'{call}' must have one value");
            }
        }
        else if (name == "Attr" && node.AttributeList is { } list)
        {
            WriteAttr(node, list, call, written);
        }
        else
        {
            Report(call.GetLocation(), $"'{name}' is not an attribute of this node");
        }
    }

    private void WriteAttr(NodeInfo node, AttributeListInfo list, InvocationExpressionSyntax call, HashSet<string> written)
    {
        var arguments = call.ArgumentList.Arguments;

        if (arguments.Count != 2)
        {
            Report(call.GetLocation(), $"'{call}' must have a name and a value");
            return;
        }

        var nameFirst = arguments[0].NameColon?.Name.Identifier.ValueText != "value" && arguments[1].NameColon?.Name.Identifier.ValueText != "name";
        var nameArgument = arguments[nameFirst ? 0 : 1];
        var name = _semanticModel.GetConstantValue(nameArgument.Expression, cancellationToken);

        if (!name.HasValue || name.Value is not string attributeName)
        {
            Report(call.GetLocation(), $"'{nameArgument}' must be a constant, because attribute names aren't escaped");
        }
        else if (!FormatReader.IsValidAttributeName(attributeName))
        {
            Report(call.GetLocation(), $"'{attributeName}' is not a valid attribute name");
        }
        else if (FindAttribute(node, attributeName.Replace("-", "")) is { Kind: AttributeKind.Text or AttributeKind.Presence })
        {
            Report(call.GetLocation(), $"'{attributeName}' has its own method for this kind of value, so it can't be used with Attr");
        }
        else if (!written.Add(attributeName))
        {
            Report(call.GetLocation(), $"'{attributeName}' appears more than once");
        }
        else
        {
            // From here on it's an attribute like any other, whose value can be anything a string can be.
            WriteAttribute(
                new AttributeInfo(
                    attributeName,
                    list.Before + attributeName + list.BetweenNameAndValue,
                    list.After,
                    list.Escape,
                    AttributeKind.Text,
                    IsCurrentCulture: list.CurrentCultureNames.Contains(attributeName, StringComparer.OrdinalIgnoreCase)),
                arguments[nameFirst ? 1 : 0].Expression);
        }
    }

    /// <summary>Names are matched ignoring case, because factory parameters such as href set the attribute Href.</summary>
    private static AttributeInfo? FindAttribute(NodeInfo node, string name) =>
        node.Attributes.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

    private void WriteBoolAttribute(AttributeInfo attribute, ExpressionSyntax value)
    {
        var constant = _semanticModel.GetConstantValue(value, cancellationToken);

        if (!constant.HasValue)
        {
            // Null leaves the attribute out, like false.
            var isNullable = _semanticModel.GetTypeInfo(value, cancellationToken).Type?.SpecialType != SpecialType.System_Boolean;

            WriteConditional(
                isNullable ? SyntaxFactory.ParseExpression($"({value}) == true") : value,
                () => _literal.Append(attribute.Prefix).Append(attribute.Suffix),
                null);
        }
        else if (constant.Value is true)
        {
            _literal.Append(attribute.Prefix).Append(attribute.Suffix);
        }
    }

    /// <summary>Runs right before a child that actually renders, so null branches and empty loops don't count.</summary>
    private void WriteSeparator()
    {
        if (_separator is { } separator)
        {
            Add(SegmentKind.BlockStart, $"if ({separator.Variable})");
            _literal.Append(separator.Text);
            Add(SegmentKind.Else);
            Add(SegmentKind.Statement, $"{separator.Variable} = true;");
            Add(SegmentKind.BlockEnd);
        }
    }

    /// <summary>A plain string that is null renders nothing at all, so no separator or quotes either.</summary>
    private void WriteStringChild(ExpressionSyntax value)
    {
        var constant = _semanticModel.GetConstantValue(value, cancellationToken);

        if (constant.HasValue && constant.Value is null)
        {
            return;
        }

        var writesAround = _separator is not null || _stringNode.TextPrefix.Length > 0 || _stringNode.TextSuffix.Length > 0;

        if (constant.HasValue || value is InterpolatedStringExpressionSyntax || !writesAround)
        {
            WriteSeparator();
            WriteTextNode(_stringNode, value);
            return;
        }

        var variable = $"__text{_nextVariable++}";

        FlushIfAwaits(value);
        Add(SegmentKind.Statement, $"string? {variable} = {value};");
        Add(SegmentKind.BlockStart, $"if ({variable} is not null)");
        WriteSeparator();
        _literal.Append(_stringNode.TextPrefix);
        AddValue(variable, _stringNode.TextEscape);
        _literal.Append(_stringNode.TextSuffix);
        Add(SegmentKind.BlockEnd);
    }

    /// <summary>
    /// A number, date or similar as a child, attribute value or hole in a string, written without creating a string.
    /// A null value leaves it out with everything around it, like it does a string.
    /// </summary>
    private void WriteFormatted(ExpressionSyntax value, bool isNullable, string prefix, string suffix, string? escape, bool isInvariant, string? valueFormat = null, bool separate = false)
    {
        // An integer is the same on the machine that compiles and the one that renders, so it's only text from here on.
        if (isInvariant && valueFormat is null && _semanticModel.GetConstantValue(value, cancellationToken).Value is sbyte or byte or short or ushort or int or uint or long or ulong)
        {
            if (separate)
            {
                WriteSeparator();
            }

            var text = ((IFormattable)_semanticModel.GetConstantValue(value, cancellationToken).Value!).ToString(null, CultureInfo.InvariantCulture);
            _literal.Append(prefix).Append(Escape(escape, text)).Append(suffix);
            return;
        }

        valueFormat ??= isInvariant ? GetIsoFormat(value) : null;

        if (escape is not null && isInvariant && WritesOnly(value, valueFormat) is { } characters && !NeedsEscaping(escape, characters))
        {
            escape = null;
        }

        var expression = value.ToString();

        if (isNullable)
        {
            var variable = $"__text{_nextVariable++}";

            FlushIfAwaits(value);
            Add(SegmentKind.Statement, $"var {variable} = {value};");
            Add(SegmentKind.BlockStart, $"if ({variable}.HasValue)");
            expression = $"{variable}.GetValueOrDefault()";
        }

        if (separate)
        {
            WriteSeparator();
        }

        _literal.Append(prefix);

        if (!isNullable)
        {
            FlushIfAwaits(value);
        }

        Add(new Segment(SegmentKind.FormattedValue, expression, EscapeMethod: GetEscapeMethod(escape), ValueFormat: valueFormat, IsInvariant: isInvariant, IsUtf8: IsUtf8Formattable(value)));
        _literal.Append(suffix);

        if (isNullable)
        {
            Add(SegmentKind.BlockEnd);
        }
    }

    /// <summary>Only called for formatted values, which are value types.</summary>
    private bool IsUtf8Formattable(ExpressionSyntax value) =>
        FormatReader.Interfaces(GetValueType(value)!).Any(i => FormatReader.IsSystemType(i, "IUtf8SpanFormattable"));

    /// <summary>The type of a value, without the <see cref="Nullable{T}"/> around a nullable value type.</summary>
    private ITypeSymbol? GetValueType(ExpressionSyntax value) => GetValueType(value, out _);

    private ITypeSymbol? GetValueType(ExpressionSyntax value, out bool isNullable)
    {
        var type = _semanticModel.GetTypeInfo(value, cancellationToken).Type;

        isNullable = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };

        return isNullable ? ((INamedTypeSymbol)type!).TypeArguments[0] : type;
    }

    /// <summary>
    /// The characters that a value of a known type can be written with in the invariant culture, or null if that isn't
    /// known, as for a type of the app's own, which writes whatever it formats as, or a format that isn't a plain one.
    /// Numbers have digits, signs, a point and an exponent, or are NaN or Infinity, dates and times are digits and
    /// separators, and Guids hexadecimal digits and dashes.
    /// </summary>
    private string? WritesOnly(ExpressionSyntax value, string? valueFormat)
    {
        const string Numbers = "0123456789+-.ENaIfinty";
        const string DatesAndTimes = "0123456789+-.:T";

        var type = GetValueType(value)!;

        var isPlainDateFormat = valueFormat is not null
            && valueFormat.IndexOf("MMM", StringComparison.Ordinal) < 0
            && valueFormat.IndexOf("ddd", StringComparison.Ordinal) < 0
            && valueFormat.All(c => "yMdHmsfzT-:.".IndexOf(c) >= 0);

        return type.SpecialType switch
        {
            SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16
                or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64
                or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal when valueFormat is null => Numbers,
            SpecialType.System_DateTime when isPlainDateFormat => DatesAndTimes,
            _ => FormatReader.SystemTypeName(type) switch
            {
                "DateTimeOffset" or "DateOnly" or "TimeOnly" when isPlainDateFormat => DatesAndTimes,
                "Guid" when valueFormat is null => "0123456789abcdef-",
                _ => null,
            },
        };
    }

    /// <summary>Whether the escape rule replaces any of these characters. An unknown context is already an error, TDSL006.</summary>
    private bool NeedsEscaping(string context, string characters) =>
        characters.Any(c => Escape(context, c.ToString()) != c.ToString());

    /// <summary>What programs read a date as. The invariant culture's own format, 11/01/2026, is useless there.</summary>
    private string? GetIsoFormat(ExpressionSyntax value) =>
        FormatReader.SystemTypeName(GetValueType(value)!) switch
        {
            "DateTime" => "yyyy-MM-ddTHH:mm:ss",
            "DateTimeOffset" => "yyyy-MM-ddTHH:mm:sszzz",
            "DateOnly" => "yyyy-MM-dd",
            "TimeOnly" => "HH:mm:ss",
            _ => null,
        };

    /// <summary>Whether this is a value type that can write itself as text, such as an int or a nullable date.</summary>
    private bool IsFormattedValue(ExpressionSyntax expression, out bool isNullable)
    {
        var value = GetValueType(expression, out isNullable);

        return value is { IsValueType: true }
            && FormatReader.Interfaces(value).Any(i => FormatReader.IsSystemType(i, "ISpanFormattable"));
    }

    private void WriteTextNode(NodeInfo node, ExpressionSyntax value)
    {
        _literal.Append(node.TextPrefix);
        WriteText(value, node.TextEscape, _hasInvariantText);
        _literal.Append(node.TextSuffix);
    }

    private void WriteChildren(BracketedArgumentListSyntax bracketList)
    {
        foreach (var argument in bracketList.Arguments)
        {
            if (argument.NameColon is null)
            {
                WriteElement(argument.Expression);
            }
            else
            {
                Report(argument.GetLocation(), "children cannot be passed as a named argument");
            }
        }
    }

    private void WriteTemplateWithChildren(ExpressionSyntax template, List<ElementAccessExpressionSyntax> brackets)
    {
        var type = _semanticModel.GetTypeInfo(template, cancellationToken).Type!;

        if (!FormatReader.Implements(type, FormatReader.AcceptsChildren))
        {
            Report(brackets[0].ArgumentList.GetLocation(),
                $"template '{type.Name}' does not accept children; it would have to implement a template interface with children");
            return;
        }

        if (IsAsyncInSync(template))
        {
            return;
        }

        if (!TryGetPassContext(template, out var passContext))
        {
            return;
        }

        WriteSeparator();
        FlushIfAwaits(template);
        Add(new Segment(SegmentKind.TemplateWithChildrenStart, template.ToString(), PassContext: passContext));

        // These children are placed by the template, inside whatever it writes around them.
        var outerSeparator = _separator;
        _separator = null;

        foreach (var access in brackets)
        {
            WriteChildren(access.ArgumentList);
        }

        _separator = outerSeparator;
        Add(SegmentKind.TemplateWithChildrenEnd);
    }

    /// <summary>Recognizes <c>template[a][b]</c>. The indexer is generated, so it may not be resolvable yet.</summary>
    private bool TryGetTemplateBrackets(
        ExpressionSyntax expression,
        List<ElementAccessExpressionSyntax> brackets,
        out ExpressionSyntax? template)
    {
        template = null;

        if (expression is not ElementAccessExpressionSyntax access)
        {
            return false;
        }

        var receiver = Unwrap(access.Expression);

        if (TryGetTemplateBrackets(receiver, brackets, out template))
        {
            brackets.Add(access);
            return true;
        }

        if (IsTemplate(receiver))
        {
            template = receiver;
            brackets.Add(access);
            return true;
        }

        return false;
    }

    /// <summary>The property is generated, so it can only be recognized by name.</summary>
    private static bool IsChildrenReference(ExpressionSyntax expression) =>
        expression switch
        {
            IdentifierNameSyntax name => name.Identifier.ValueText == "Children",
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax, Name: var name } => name.Identifier.ValueText == "Children",
            _ => false,
        };

    /// <summary>A ternary branch that is null renders nothing: <c>c ? node : null</c>.</summary>
    private void WriteBranch(ExpressionSyntax branch)
    {
        var expression = Unwrap(branch);

        if (expression is ThrowExpressionSyntax thrown)
        {
            WriteThrow(thrown);
        }
        else if (!expression.IsKind(SyntaxKind.NullLiteralExpression))
        {
            WriteElement(branch);
        }
    }

    private void WriteAttribute(AttributeInfo attribute, ExpressionSyntax syntax)
    {
        var value = Unwrap(syntax);

        if (acceptsChildren && value.DescendantNodesAndSelf().OfType<ExpressionSyntax>().Any(IsChildrenReference))
        {
            Report(value.GetLocation(), "Children can only be placed as a child, because they are not available as a value");
            return;
        }

        if (attribute.Kind == AttributeKind.Presence)
        {
            WriteBoolAttribute(attribute, value);
            return;
        }

        if (attribute.Kind == AttributeKind.TrueFalse)
        {
            WriteTrueFalseAttribute(attribute, value);
            return;
        }

        if (attribute.Kind == AttributeKind.Enum && GetEnumType(value) is { } enumType)
        {
            WriteEnumAttribute(attribute, value, enumType);
            return;
        }

        if (IsFormattedValue(value, out var isNullable))
        {
            WriteFormatted(value, isNullable, attribute.Prefix, attribute.Suffix, attribute.Escape, !attribute.IsCurrentCulture, attribute.Format);
            return;
        }

        var constant = _semanticModel.GetConstantValue(value, cancellationToken);

        if (constant.HasValue)
        {
            // A constant null means that the attribute is absent.
            if (constant.Value is string text)
            {
                _literal.Append(attribute.Prefix).Append(Escape(attribute.Escape, text)).Append(attribute.Suffix);
            }
        }
        else if (value is ConditionalExpressionSyntax conditional)
        {
            WriteTernary(conditional, branch => WriteAttribute(attribute, branch));
        }
        else if (value is InterpolatedStringExpressionSyntax interpolated)
        {
            // An interpolated string is never null, so the attribute is always present.
            _literal.Append(attribute.Prefix);
            WriteInterpolated(interpolated, attribute.Escape, !attribute.IsCurrentCulture);
            _literal.Append(attribute.Suffix);
        }
        else
        {
            FlushIfAwaits(value);
            Add(new Segment(SegmentKind.OptionalAttribute, value.ToString(), attribute.Prefix, attribute.Suffix, GetEscapeMethod(attribute.Escape)));
        }
    }

    private INamedTypeSymbol? GetEnumType(ExpressionSyntax value) =>
        GetValueType(value) is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType ? enumType : null;

    /// <summary>
    /// Writes the member's name as the value, as text that is known while compiling. A constant is only that text, and
    /// anything else a switch over the members. Null and values that aren't members leave the attribute out.
    /// </summary>
    private void WriteEnumAttribute(AttributeInfo attribute, ExpressionSyntax value, INamedTypeSymbol enumType)
    {
        var members = enumType.GetMembers().OfType<IFieldSymbol>()
            .Where(field => field.HasConstantValue)
            .GroupBy(field => Convert.ToInt64(field.ConstantValue, CultureInfo.InvariantCulture))
            .Select(group => group.First())
            .ToArray();

        var constant = _semanticModel.GetConstantValue(value, cancellationToken);

        if (constant.HasValue)
        {
            var member = constant.Value is null
                ? null
                : members.FirstOrDefault(field => Convert.ToInt64(field.ConstantValue, CultureInfo.InvariantCulture) == Convert.ToInt64(constant.Value, CultureInfo.InvariantCulture));

            if (member is not null)
            {
                _literal.Append(attribute.Prefix).Append(Escape(attribute.Escape, FormatReader.GetEnumText(member.Name))).Append(attribute.Suffix);
            }

            return;
        }

        var typeName = enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var variable = $"__text{_nextVariable++}";

        FlushIfAwaits(value);
        Add(SegmentKind.Statement, $"{typeName}? {variable} = {value};");
        Add(SegmentKind.BlockStart, $"switch ({variable})");

        foreach (var member in members)
        {
            Add(SegmentKind.Statement, $"case {typeName}.{member.Name}:");
            _literal.Append(attribute.Prefix).Append(Escape(attribute.Escape, FormatReader.GetEnumText(member.Name))).Append(attribute.Suffix);
            Add(SegmentKind.Statement, "break;");
        }

        Add(SegmentKind.BlockEnd);
    }

    /// <summary>Writes the word true or false as the value. Null leaves the attribute out.</summary>
    private void WriteTrueFalseAttribute(AttributeInfo attribute, ExpressionSyntax value)
    {
        var constant = _semanticModel.GetConstantValue(value, cancellationToken);

        if (constant.HasValue)
        {
            if (constant.Value is bool word)
            {
                _literal.Append(attribute.Prefix).Append(word ? "true" : "false").Append(attribute.Suffix);
            }

            return;
        }

        var variable = $"__text{_nextVariable++}";

        FlushIfAwaits(value);
        Add(SegmentKind.Statement, $"bool? {variable} = {value};");
        Add(SegmentKind.BlockStart, $"if ({variable}.HasValue)");
        _literal.Append(attribute.Prefix);
        Add(SegmentKind.BlockStart, $"if ({variable}.GetValueOrDefault())");
        _literal.Append("true");
        Add(SegmentKind.Else);
        _literal.Append("false");
        Add(SegmentKind.BlockEnd);
        _literal.Append(attribute.Suffix);
        Add(SegmentKind.BlockEnd);
    }

    private string Escape(string? context, string value) => FormatReader.Escape(_escapeRules, context, value);

    private string? GetEscapeMethod(string? context) =>
        context is null
            ? null
            : $"{format.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.__WriteEscaped_{context}";

    private void WriteText(ExpressionSyntax syntax, string? escape, bool isInvariant)
    {
        var value = Unwrap(syntax);
        var constant = _semanticModel.GetConstantValue(value, cancellationToken);

        if (constant.HasValue)
        {
            // A constant null renders as nothing.
            if (constant.Value is string text)
            {
                _literal.Append(Escape(escape, text));
            }
        }
        else if (value is ConditionalExpressionSyntax conditional)
        {
            WriteTernary(conditional, branch => WriteText(branch, escape, isInvariant));
        }
        else if (value is InterpolatedStringExpressionSyntax interpolated)
        {
            WriteInterpolated(interpolated, escape, isInvariant);
        }
        else
        {
            FlushIfAwaits(value);
            AddValue(value.ToString(), escape);
        }
    }

    private void WriteInterpolated(InterpolatedStringExpressionSyntax interpolated, string? escape, bool isInvariant)
    {
        foreach (var content in interpolated.Contents)
        {
            if (content is InterpolatedStringTextSyntax text)
            {
                _literal.Append(Escape(escape, GetInterpolationText(interpolated, text)));
            }
            else if (content is InterpolationSyntax hole)
            {
                if (hole.AlignmentClause is null && IsFormattedValue(hole.Expression, out var isNullable))
                {
                    WriteFormatted(hole.Expression, isNullable, "", "", escape, isInvariant, hole.FormatClause?.FormatStringToken.ValueText);
                    continue;
                }

                FlushIfAwaits(hole.Expression);

                var isPlainString = hole.AlignmentClause is null && hole.FormatClause is null && IsString(hole.Expression);

                // Other holes keep their exact formatting by staying an interpolated string.
                var formatted = $"$\"{{{hole.Expression}{hole.AlignmentClause}{hole.FormatClause}}}\"";

                AddValue(
                    isPlainString ? hole.Expression.ToString()
                        : isInvariant ? $"string.Create(global::System.Globalization.CultureInfo.InvariantCulture, {formatted})"
                        : formatted,
                    escape);
            }
        }
    }

    /// <summary>
    /// The token's value still has doubled braces in it. Raw strings have no such escape: there,
    /// doubled braces are either text or, depending on the number of $ signs, not in a text token at all.
    /// </summary>
    private static string GetInterpolationText(InterpolatedStringExpressionSyntax interpolated, InterpolatedStringTextSyntax text)
    {
        var value = text.TextToken.ValueText;
        var isRaw = interpolated.StringStartToken.IsKind(SyntaxKind.InterpolatedSingleLineRawStringStartToken)
            || interpolated.StringStartToken.IsKind(SyntaxKind.InterpolatedMultiLineRawStringStartToken);

        return isRaw ? value : value.Replace("{{", "{").Replace("}}", "}");
    }

    private void AddValue(string expression, string? escape) =>
        Add(new Segment(SegmentKind.Value, expression, EscapeMethod: GetEscapeMethod(escape)));

    private void FlushLiteral()
    {
        if (_literal.Length > 0)
        {
            _segments.Add(new Segment(SegmentKind.Literal, _literal.ToString()));
            _literal.Clear();
        }
    }

    private bool IsString(ExpressionSyntax expression) =>
        _semanticModel.GetTypeInfo(expression, cancellationToken).Type?.SpecialType == SpecialType.System_String;

    /// <summary>
    /// Whether a nested template gets the context of this one. Without this check, the mistake would surface as an
    /// error in generated code. Children given in brackets belong to this template, so they use its context too.
    /// </summary>
    private bool TryGetPassContext(ExpressionSyntax template, out bool passContext)
    {
        // Only called for templates, which IsTemplate has found to have a type: a class, an interface, which can be the
        // context interface itself, or a type parameter constrained to a template.
        var type = _semanticModel.GetTypeInfo(template, cancellationToken).Type!;
        var needed = FormatReader.Interfaces(type)
            .FirstOrDefault(i => FormatReader.IsCoreType(i, FormatReader.ContextRenderable))
            ?.TypeArguments[0];

        passContext = needed is not null;

        if (needed is null || (contextType is not null && compilation.HasImplicitConversion(contextType, needed)))
        {
            return true;
        }

        _diagnostics.Add(DiagnosticInfo.Create(
            TemplateDsl.Generators.Diagnostics.ContextNotProvided,
            template.GetLocation(),
            contextType is null
                ? $"'{type.Name}' needs a context of type '{needed.Name}', but this template has no context; derive it from a template base class with a context"
                : $"'{type.Name}' needs a context of type '{needed.Name}', which the context '{contextType.Name}' of this template doesn't provide"));
        return false;
    }

    private bool IsTemplate(ExpressionSyntax expression) =>
        _semanticModel.GetTypeInfo(expression, cancellationToken).Type is { } type
        && FormatReader.Implements(type, FormatReader.Renderable);

    /// <summary>
    /// A node's brackets are generated, and so is a template's conversion to a node, so binding may fail
    /// or the indexer may not exist yet in the compilation being analyzed. So this looks at what the
    /// brackets are applied to, which in a chain is the start of that chain.
    /// </summary>
    private bool IsNodeBrackets(ElementAccessExpressionSyntax access) =>
        ResolveChain(access.Expression) is { Node.IsText: false };

    /// <summary>
    /// Reads <c>H.Div.Id("a").Class("b")</c>. Only the start is looked up: the methods after it can be generated
    /// in this same compilation, which this generator can't see. Build is also compiled, so wrong uses still fail.
    /// </summary>
    private NodeChain? ResolveChain(ExpressionSyntax syntax)
    {
        var expression = Unwrap(syntax);
        var symbol = _semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;

        if (symbol is IPropertySymbol property && FormatReader.IsTags(property.ContainingType))
        {
            return GetNode(property.Type) is { } node
                ? new NodeChain(node, ImmutableArray<IParameterSymbol>.Empty, default, new List<InvocationExpressionSyntax>())
                : null;
        }

        if (expression is not InvocationExpressionSyntax invocation)
        {
            return null;
        }

        if (symbol is IMethodSymbol method && FormatReader.IsTags(method.ContainingType))
        {
            return GetNode(method.ReturnType) is { } node
                ? new NodeChain(node, method.Parameters, invocation.ArgumentList.Arguments, new List<InvocationExpressionSyntax>())
                : null;
        }

        if (invocation.Expression is not MemberAccessExpressionSyntax { Name: IdentifierNameSyntax } member
            || ResolveChain(member.Expression) is not { } chain)
        {
            return null;
        }

        chain.Calls.Add(invocation);
        return chain;
    }

    private NodeInfo? GetNode(ITypeSymbol type) =>
        DerivesFromFormat(type) ? FormatReader.ReadNode(type) : null;

    private bool DerivesFromFormat(ITypeSymbol type) =>
        FormatReader.BaseTypes(type).Any(current => SymbolEqualityComparer.Default.Equals(current, format));

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }

    private void Report(Location location, string reason) =>
        _diagnostics.Add(DiagnosticInfo.Create(TemplateDsl.Generators.Diagnostics.UnsupportedBuildConstruct, location, reason));

    private sealed record NodeChain(
        NodeInfo Node,
        ImmutableArray<IParameterSymbol> Parameters,
        SeparatedSyntaxList<ArgumentSyntax> Arguments,
        List<InvocationExpressionSyntax> Calls);
}
