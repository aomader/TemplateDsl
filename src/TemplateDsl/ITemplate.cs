using System.Buffers;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;

namespace TemplateDsl;

// The interfaces a template implements say what it is, and an IDE has nothing to suggest implementing but Build: the
// methods they have are hidden and have a default. The source generator implements them for every template, and the
// render methods in TemplateRendering call them. A context of type object means none: a template without a context
// is content for any context.

/// <summary>Any template, with or without a context.</summary>
public interface IRenderable;

/// <summary>A template that needs no context, sync or async, which <c>RenderToAsync</c> can render.</summary>
public interface ITemplateRenderable : IRenderable;

/// <summary>
/// A template that needs a <typeparamref name="TContext"/> to render. A parent passes its own context on, so a
/// child can ask for less than its parent has, such as one interface of it, but never for more.
/// </summary>
public interface ITemplateRenderable<in TContext> : IRenderable
    where TContext : notnull;

/// <summary>A template that renders in one go, which <c>RenderToString</c> can render.</summary>
public interface ISyncRenderable : ITemplateRenderable;

/// <summary>A template that streams its output as it is rendered, so it has only async render methods.</summary>
public interface IAsyncRenderable : ITemplateRenderable;

/// <summary>A template with a context that renders in one go.</summary>
public interface ISyncRenderable<in TContext> : ITemplateRenderable<TContext>
    where TContext : notnull;

/// <summary>A template with a context that streams its output as it is rendered, so it has only async render methods.</summary>
public interface IAsyncRenderable<in TContext> : ITemplateRenderable<TContext>
    where TContext : notnull;

/// <summary>A template that can be given children in brackets, which gets a generated <c>Children</c> property to place them.</summary>
public interface IAcceptsChildren;

/// <summary>A sync template of the format <typeparamref name="TNode"/>, which can be placed in brackets whatever its type, also an interface or a type parameter.</summary>
public interface ITemplateContent<out TNode> : IRenderable
    where TNode : TemplateNode;

/// <summary>
/// Content of the format <typeparamref name="TNode"/> for an async template, which places it with <c>await</c>: any
/// template, sync or async, that renders with a <typeparamref name="TContext"/>, because it needs it, less of it, or none,
/// which counts as needing an <see cref="object"/>. As a parameter, such as of a layout, it takes all of them, and the
/// compiler rejects a template that needs a context it can't get.
/// </summary>
public interface IAsyncTemplateContent<out TNode, in TContext> : IRenderable
    where TNode : TemplateNode
    where TContext : notnull
{
    /// <summary>
    /// Used by generated code: renders this template as part of an async render, with the context of the render, which
    /// only a template that needs one uses. A sync template renders in one go, and then lets its output be sent. The
    /// generator implements it for every template. This default only runs for a type it didn't see.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [SuppressMessage("Naming", "CA1707", Justification = "The underscores keep generated members apart from a template's own.")]
    ValueTask __RenderContentAsync(PipeWriter writer, TContext context, CancellationToken cancellationToken) =>
        throw TemplateBuild.NotGenerated(this);

    /// <summary>
    /// Used by generated code: the HTTP Content-Type its format declares, such as <c>text/html; charset=utf-8</c>, or
    /// null if it declares none. The generator writes it into every template, so that it needs no reflection.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [SuppressMessage("Naming", "CA1707", Justification = "The underscores keep generated members apart from a template's own.")]
    string? __ContentType => null;
}

/// <summary>
/// Content of the format <typeparamref name="TNode"/> that renders with a <typeparamref name="TContext"/>: a sync
/// template that needs it, or less of it, or no context at all, which counts as needing an <see cref="object"/>. As a
/// parameter, such as of a layout, it takes all of them, and the compiler rejects a template that needs a context it
/// can't get. It is content for an async template too.
/// </summary>
public interface ITemplateContent<out TNode, in TContext> : ITemplateContent<TNode>, IAsyncTemplateContent<TNode, TContext>
    where TNode : TemplateNode
    where TContext : notnull
{
    /// <summary>
    /// Used by generated code: writes this template as UTF-8 to <paramref name="writer"/>, without flushing, with the
    /// context of the render, which only a template that needs one uses. The generator implements it for every sync
    /// template. This default only runs for a type it didn't see.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [SuppressMessage("Naming", "CA1707", Justification = "The underscores keep generated members apart from a template's own.")]
    void __RenderContent(IBufferWriter<byte> writer, TContext context) => throw TemplateBuild.NotGenerated(this);
}

/// <summary>
/// A template that outputs nodes of the format <typeparamref name="TNode"/>. Its data lives on the implementing type,
/// a class, struct or record, so a template is created like any other object. The source generator adds the render
/// methods, and a conversion to <typeparamref name="TNode"/>, which places it in Build.
/// </summary>
public interface ITemplate<out TNode> : ISyncRenderable, ITemplateContent<TNode, object>
    where TNode : TemplateNode
{
    /// <summary>
    /// Describes the output of this template. This method never runs: the source generator translates its body
    /// into the generated render methods, so it has to stay within what the generator supports:
    /// nodes, strings, numbers and other values, nested templates, ternaries, switch expressions, <c>Each</c>, local variables,
    /// expression statements, blocks, and if, switch, foreach, for, while, break, continue, return, throw and yield statements.
    /// </summary>
    IEnumerable<TNode> Build();
}

/// <summary>
/// A template that streams: output is sent as it is rendered, also before an <c>await</c>, and a slow client
/// slows down rendering. It doesn't have to await anything. It only gets async render methods, and only
/// other async templates can contain it.
/// </summary>
public interface IAsyncTemplate<TNode> : IAsyncRenderable, IAsyncTemplateContent<TNode, object>
    where TNode : TemplateNode
{
    /// <summary>
    /// Describes the output of this template, like <see cref="ITemplate{TNode}.Build"/>. In addition,
    /// <c>await</c> can be used in any expression, and <c>EachAsync</c> loops over data as it arrives.
    /// </summary>
    Task<IEnumerable<TNode>> BuildAsync(CancellationToken cancellationToken);
}

/// <summary>A template like <see cref="ITemplate{TNode}"/> whose Build method receives a context that every render call has to supply.</summary>
public interface ITemplate<out TNode, in TContext> : ISyncRenderable<TContext>, ITemplateContent<TNode, TContext>
    where TNode : TemplateNode
    where TContext : notnull
{
    /// <summary>Describes the output of this template, see <see cref="ITemplate{TNode}.Build"/>. This method never runs.</summary>
    IEnumerable<TNode> Build(TContext context);
}

/// <summary>A template like <see cref="IAsyncTemplate{TNode}"/> whose BuildAsync method receives a context that every render call has to supply.</summary>
public interface IAsyncTemplate<TNode, in TContext> : IAsyncRenderable<TContext>, IAsyncTemplateContent<TNode, TContext>
    where TNode : TemplateNode
    where TContext : notnull
{
    /// <summary>Describes the output of this template, see <see cref="IAsyncTemplate{TNode}.BuildAsync"/>. This method never runs.</summary>
    Task<IEnumerable<TNode>> BuildAsync(TContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Used by generated code: the children a template was given in brackets, which it writes where it places them. Each
/// parent has a struct like this for every place it gives children, holding what they need, so that writing them
/// allocates nothing.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[SuppressMessage("Naming", "CA1707", Justification = "The underscores keep generated members apart from a template's own.")]
public interface IChildrenWriter
{
    /// <summary>Writes the children in one go.</summary>
    void __Write(IBufferWriter<byte> writer);
}

/// <summary>Used by generated code: like <see cref="IChildrenWriter"/>, for children given in an async template, which can wait.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[SuppressMessage("Naming", "CA1707", Justification = "The underscores keep generated members apart from a template's own.")]
public interface IAsyncChildrenWriter
{
    /// <summary>Writes the children, waiting for what they await.</summary>
    ValueTask __WriteAsync(PipeWriter writer, CancellationToken cancellationToken);
}

/// <summary>Used by generated code: no children, for a template that takes them but is rendered without.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[SuppressMessage("Naming", "CA1707", Justification = "The underscores keep generated members apart from a template's own.")]
[SuppressMessage("Performance", "CA1815", Justification = "It has no state, and is never compared.")]
public readonly struct NoChildren : IChildrenWriter, IAsyncChildrenWriter
{
    /// <inheritdoc/>
    public void __Write(IBufferWriter<byte> writer)
    {
    }

    /// <inheritdoc/>
    public ValueTask __WriteAsync(PipeWriter writer, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

/// <summary>
/// Used by generated code: renders a template with the children its parent gave it, as part of an async render, with
/// the context of the render, which only a template that needs one uses: one without has this for an object context.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[SuppressMessage("Naming", "CA1707", Justification = "The underscores keep generated members apart from a template's own.")]
public interface IChildrenRenderer<in TContext>
    where TContext : notnull
{
    /// <summary>Renders as part of an async parent, with <paramref name="children"/> written where the template places them.</summary>
    ValueTask __RenderWithChildrenAsync<TChildren>(PipeWriter writer, TContext context, TChildren children, CancellationToken cancellationToken)
        where TChildren : IAsyncChildrenWriter;
}

/// <summary>Used by generated code: like <see cref="IChildrenRenderer{TContext}"/>, for a sync template, which also renders in one go.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[SuppressMessage("Naming", "CA1707", Justification = "The underscores keep generated members apart from a template's own.")]
public interface ISyncChildrenRenderer<in TContext> : IChildrenRenderer<TContext>
    where TContext : notnull
{
    /// <summary>Renders in one go, with <paramref name="children"/> written where the template places them.</summary>
    void __RenderWithChildren<TChildren>(IBufferWriter<byte> writer, TContext context, TChildren children)
        where TChildren : IChildrenWriter;
}
