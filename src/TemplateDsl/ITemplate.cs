using System.Buffers;
using System.ComponentModel;
using System.IO.Pipelines;

namespace TemplateDsl;

/// <summary>Any template, with or without a context.</summary>
public interface IRenderable;

/// <summary>Any async template, with or without a context. Those stream their output as it is rendered.</summary>
public interface IStreamingRenderable : IRenderable;

/// <summary>A template that needs no context, sync or async, which <c>RenderToAsync</c> can render.</summary>
public interface ITemplateRenderable : IRenderable;

/// <summary>
/// A template that needs a <typeparamref name="TContext"/> to render. A parent passes its own context on, so a
/// child can ask for less than its parent has, such as one interface of it, but never for more.
/// </summary>
public interface ITemplateRenderable<in TContext> : IRenderable
    where TContext : notnull;

/// <summary>A template with a context that renders in one go.</summary>
public interface ISyncRenderable<in TContext> : ITemplateRenderable<TContext>
    where TContext : notnull
{
    /// <summary>Writes this template as UTF-8 to <paramref name="writer"/>, without flushing. Generated.</summary>
    void RenderTo(IBufferWriter<byte> writer, TContext context);
}

/// <summary>A template with a context that streams its output as it is rendered, so it has only async render methods.</summary>
public interface IAsyncRenderable<in TContext> : ITemplateRenderable<TContext>, IStreamingRenderable
    where TContext : notnull
{
    /// <summary>Used by generated code: renders as part of a parent.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707", Justification = "The underscores keep generated members apart from a template's own.")]
    ValueTask __RenderAsync(PipeWriter writer, TContext context, CancellationToken cancellationToken);
}

/// <summary>A template that renders in one go.</summary>
public interface ISyncRenderable : ITemplateRenderable
{
    /// <summary>Writes this template as UTF-8 to <paramref name="writer"/>, without flushing. Generated.</summary>
    void RenderTo(IBufferWriter<byte> writer);
}

/// <summary>A template that streams its output as it is rendered, so it has only async render methods.</summary>
public interface IAsyncRenderable : ITemplateRenderable, IStreamingRenderable
{
    /// <summary>Used by generated code: renders as part of a parent.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707", Justification = "The underscores keep generated members apart from a template's own.")]
    ValueTask __RenderAsync(PipeWriter writer, CancellationToken cancellationToken);
}

/// <summary>A template that can be given children in brackets, which gets a generated <c>Children</c> property to place them.</summary>
public interface IAcceptsChildren;

/// <summary>
/// A template that outputs nodes of the format <typeparamref name="TNode"/>. Its data lives on the
/// implementing type, so a template is created like any other object and rendered with the generated methods.
/// </summary>
public interface ITemplate<out TNode> : ISyncRenderable
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
public interface IAsyncTemplate<TNode> : IAsyncRenderable
    where TNode : TemplateNode
{
    /// <summary>
    /// Describes the output of this template, like <see cref="ITemplate{TNode}.Build"/>. In addition,
    /// <c>await</c> can be used in any expression, and <c>EachAsync</c> loops over data as it arrives.
    /// </summary>
    Task<IEnumerable<TNode>> BuildAsync(CancellationToken cancellationToken);
}

/// <summary>A template like <see cref="ITemplate{TNode}"/> whose Build method receives a context that every render call has to supply.</summary>
public interface ITemplate<out TNode, in TContext> : ISyncRenderable<TContext>
    where TNode : TemplateNode
    where TContext : notnull
{
    /// <summary>Describes the output of this template, see <see cref="ITemplate{TNode}.Build"/>. This method never runs.</summary>
    IEnumerable<TNode> Build(TContext context);
}

/// <summary>A template like <see cref="IAsyncTemplate{TNode}"/> whose BuildAsync method receives a context that every render call has to supply.</summary>
public interface IAsyncTemplate<TNode, in TContext> : IAsyncRenderable<TContext>
    where TNode : TemplateNode
    where TContext : notnull
{
    /// <summary>Describes the output of this template, see <see cref="IAsyncTemplate{TNode}.BuildAsync"/>. This method never runs.</summary>
    Task<IEnumerable<TNode>> BuildAsync(TContext context, CancellationToken cancellationToken);
}
