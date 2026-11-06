using System.Collections;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace TemplateDsl.Generators;

// Everything in this file has value equality and holds no symbols or syntax nodes,
// so that the incremental pipeline can cache its results between compilations.

/// <param name="Namespace">The namespace of the template, or null for the global one.</param>
/// <param name="Name">The name of the template, with its type parameters.</param>
/// <param name="MetadataName">The name of the template in metadata, which names the generated file.</param>
/// <param name="Keyword">How the type is declared: class, struct, record or record struct.</param>
/// <param name="Usings">The using directives of the file with Build, which the copied expressions may need.</param>
/// <param name="Segments">What the render methods write, or null to generate nothing, such as for a type that isn't partial.</param>
/// <param name="AcceptsChildren">Whether the template can be given children in brackets.</param>
/// <param name="IsAsync">Whether the template streams.</param>
/// <param name="CancellationParameter">The name BuildAsync gives its cancellation token, if it is async.</param>
/// <param name="ContextType">The type of the context the template needs, if it needs one.</param>
/// <param name="ContextParameter">The name Build gives its context, if it needs one.</param>
/// <param name="FormatType">The format's node type.</param>
/// <param name="Modifier">Of the render methods: empty, virtual, override, or abstract for a template without Build.</param>
/// <param name="Interfaces">The interface to add for children, for a template that takes them and whose base class isn't one already, or null.</param>
/// <param name="AbstractBuild">The Build method to declare for an abstract template that doesn't declare it, or null.</param>
/// <param name="HasConversion">Whether it gets a conversion to the format's node type, which only sync templates have.</param>
/// <param name="Diagnostics">What is wrong with the template, if anything.</param>
internal sealed record TemplateModel(
    string? Namespace,
    string Name,
    string MetadataName,
    string Keyword,
    EquatableArray<string> Usings,
    EquatableArray<Segment>? Segments,
    bool AcceptsChildren,
    bool IsAsync,
    string? CancellationParameter,
    string? ContextType,
    string? ContextParameter,
    string FormatType,
    string Modifier,
    string? Interfaces,
    string? AbstractBuild,
    bool HasConversion,
    EquatableArray<DiagnosticInfo> Diagnostics);

internal enum SegmentKind
{
    /// <summary>Text is output to write as it is.</summary>
    Literal,

    /// <summary>Text is a string expression whose value is written.</summary>
    Value,

    /// <summary>Text is an expression of a formattable value type, which is written without creating a string.</summary>
    FormattedValue,

    /// <summary>Text is a string expression; the attribute is left out if its value is null.</summary>
    OptionalAttribute,

    /// <summary>Text is an expression of a template type, which renders itself to the same writer.</summary>
    Template,

    /// <summary>Text is a template expression. The segments up to the matching end are its children.</summary>
    TemplateWithChildrenStart,

    TemplateWithChildrenEnd,

    /// <summary>The place where this template's own children go.</summary>
    Children,


    /// <summary>Text is code that writes nothing itself, such as a statement or a case label, copied as it is.</summary>
    Statement,

    /// <summary>What follows awaits something, so the output so far should be sent first.</summary>
    FlushBeforeAwait,

    /// <summary>Text is what comes before the braces, such as an if or a loop header, if anything. The segments up to the matching end are inside them.</summary>
    BlockStart,

    /// <summary>Ends the block of an if and starts the block of its else.</summary>
    Else,

    BlockEnd,

    /// <summary>A block end after which async rendering has a flush checkpoint, one in every iteration.</summary>
    LoopEnd,
}

/// <param name="Kind">What this segment is.</param>
/// <param name="Text">The literal text, or the expression of a value or a template.</param>
/// <param name="AttributePrefix">For optional attributes: what is written before the value.</param>
/// <param name="AttributeSuffix">For optional attributes: what is written after the value.</param>
/// <param name="EscapeMethod">For values: the generated method that writes them escaped, or null to write them as they are.</param>
/// <param name="PassContext">For nested templates: whether this one needs the context of its parent.</param>
/// <param name="ValueFormat">For formatted values: the format string, such as F2, if there is one.</param>
/// <param name="IsInvariant">For formatted values: whether they use the invariant culture instead of the current one.</param>
/// <param name="IsUtf8">For formatted values: whether they can format themselves as UTF-8.</param>
/// <param name="IsSyncChild">For nested templates: whether this one renders in one go.</param>
/// <param name="Pattern">For templates given children: the interface to give them through, if their type doesn't have the methods itself.</param>
/// <param name="Captures">For children given to a template: the variables of Build they use, which their method gets.</param>
/// <param name="TypeParameter">For content of a type parameter type: its name, to test for null without boxing a struct.</param>
/// <param name="IsContent">For nested templates: whether this one is content of an interface or type parameter type, which may need a context or not.</param>
/// <param name="ForwardsChildren">For children given to a template: whether they include this template's own children.</param>
internal sealed record Segment(
    SegmentKind Kind,
    string Text,
    string? AttributePrefix = null,
    string? AttributeSuffix = null,
    string? EscapeMethod = null,
    bool PassContext = false,
    string? ValueFormat = null,
    bool IsInvariant = false,
    bool IsUtf8 = false,
    bool IsSyncChild = false,
    string? Pattern = null,
    EquatableArray<Capture>? Captures = null,
    bool ForwardsChildren = false,
    bool IsContent = false,
    string? TypeParameter = null);

/// <summary>A variable of Build that children given to a template use, by its name in code and its type.</summary>
internal sealed record Capture(string Name, string Type);

internal sealed record DiagnosticInfo(
    DiagnosticDescriptor Descriptor,
    string Path,
    TextSpan Span,
    LinePositionSpan LineSpan,
    string Argument)
{
    public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, Location location, string argument)
    {
        var lineSpan = location.GetLineSpan();
        return new DiagnosticInfo(descriptor, lineSpan.Path, location.SourceSpan, lineSpan.Span, argument);
    }

    public Diagnostic ToDiagnostic() =>
        Diagnostic.Create(Descriptor, Location.Create(Path, Span, LineSpan), Argument);
}

internal readonly struct EquatableArray<TItem>(TItem[] items) : IEquatable<EquatableArray<TItem>>, IEnumerable<TItem>
{
    private readonly TItem[] _items = items;

    public int Length => _items.Length;

    public bool Equals(EquatableArray<TItem> other) => _items.SequenceEqual(other._items);

    public override bool Equals(object? obj) => obj is EquatableArray<TItem> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = 17;
        foreach (var item in _items)
        {
            hash = hash * 31 + (item?.GetHashCode() ?? 0);
        }

        return hash;
    }

    public IEnumerator<TItem> GetEnumerator() => ((IEnumerable<TItem>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}
