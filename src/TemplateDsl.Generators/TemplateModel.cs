using System.Collections;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace TemplateDsl.Generators;

// Everything in this file has value equality and holds no symbols or syntax nodes,
// so that the incremental pipeline can cache its results between compilations.

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
    bool IsSyncChild = false);

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
