using System.ComponentModel;

namespace TemplateDsl;

/// <summary>
/// Base class of the nodes of every format. A format derives its own base class from this. Nodes only
/// describe a template in its Build method, which the source generator compiles: they aren't rendered.
/// </summary>
public abstract class TemplateNode;

/// <summary>Used by generated members that only exist so that Build type-checks.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class TemplateBuild
{
    /// <summary>The error that Build-only members throw if they run anyway, which they only can by calling Build by hand.</summary>
    public static InvalidOperationException NotExecutable() =>
        new("Build is compiled by the source generator and can't be executed. Use the generated render methods.");

    /// <summary>The error of a template without generated render methods, which only one that isn't partial lacks, and that is a compiler error.</summary>
    public static InvalidOperationException NotGenerated(object template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return new($"'{template.GetType().Name}' has no render methods, which the source generator adds to templates that are declared partial.");
    }
}

/// <summary>
/// The children in brackets, such as <c>H.Div [ "text", H.Span, new Row(), page ]</c>: nodes, text and values of the
/// format, and its sync templates of any type, also an interface such as a layout's content, or a type parameter.
/// Like everything in Build, this is compiled by the source generator and never created.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1010", Justification = "Its element type is object, so that each child goes to the Add method that takes it.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1710", Justification = "It is what brackets take, not a collection to use.")]
public sealed class TemplateChildren<TNode> : System.Collections.IEnumerable
    where TNode : TemplateNode
{
    /// <summary>A node, or text or a value that converts to one.</summary>
    public void Add(TNode? node) => throw TemplateBuild.NotExecutable();

    /// <summary>A template of this format, of any type: a class, a struct, an interface or a type parameter.</summary>
    public void Add<TTemplate>(TTemplate? template)
        where TTemplate : ITemplateContent<TNode> =>
        throw TemplateBuild.NotExecutable();

    /// <inheritdoc/>
    public System.Collections.IEnumerator GetEnumerator() => throw TemplateBuild.NotExecutable();
}
