using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace TemplateDsl;

/// <summary>
/// The render methods of every template, so that they don't have to be generated for each one. They
/// also work through interfaces, such as a layout chosen at runtime. The NuGet package imports this
/// namespace automatically. The methods with and without a context stay separate, because
/// sharing their bodies would take a closure on every render.
/// </summary>
public static class TemplateRendering
{
    private static readonly StreamPipeWriterOptions StreamOptions = new(leaveOpen: true);

    /// <summary>
    /// Lets an async template be a child of another async template in Build: <c>await new Page()</c>. An async
    /// template can't become a node any other way, so C# itself rejects using one without await, or in a template
    /// that isn't async. Like everything in Build, this is compiled by the source generator and never runs.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never), ExcludeFromCodeCoverage]
    public static TaskAwaiter<TNode> GetAwaiter<TNode>(this IAsyncTemplate<TNode> template)
        where TNode : TemplateNode =>
        throw TemplateBuild.NotExecutable();

    /// <inheritdoc cref="GetAwaiter{TNode}"/>
    [EditorBrowsable(EditorBrowsableState.Never), ExcludeFromCodeCoverage]
    public static TaskAwaiter<TNode> GetAwaiter<TNode, TContext>(this IAsyncTemplate<TNode, TContext> template)
        where TNode : TemplateNode
        where TContext : notnull =>
        throw TemplateBuild.NotExecutable();

    /// <summary>
    /// Writes this template as UTF-8 to <paramref name="writer"/> and flushes. An async template streams: output is
    /// sent as it is rendered, a slow client slows down rendering, and if the client goes away or the token fires,
    /// rendering stops. A sync template renders in one go, so all of its output is held in memory until then. Either way,
    /// a client that went away or a fired token ends in an <see cref="OperationCanceledException"/>, so finishing means everything was sent.
    /// </summary>
    public static async ValueTask RenderToAsync(this ITemplateRenderable template, PipeWriter writer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(writer);

        if (template is ISyncRenderable sync)
        {
            sync.RenderTo(writer);
        }
        else
        {
            await ((IAsyncRenderable)template).__RenderAsync(writer, cancellationToken).ConfigureAwait(false);
        }

        await TemplateStreaming.FlushAsync(writer, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes this template as UTF-8 to <paramref name="stream"/> and flushes, like the method for a PipeWriter. The stream stays open.</summary>
    public static async ValueTask RenderToAsync(this ITemplateRenderable template, Stream stream, CancellationToken cancellationToken = default)
    {
        var writer = PipeWriter.Create(stream, StreamOptions);

        try
        {
            await template.RenderToAsync(writer, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await writer.CompleteAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Renders this template to a string.</summary>
    public static string RenderToString(this ISyncRenderable template)
    {
        ArgumentNullException.ThrowIfNull(template);

        using var writer = new PooledBufferWriter();
        template.RenderTo(writer);
        return writer.GetWrittenString();
    }

    /// <summary>Renders this template to a string, once everything it awaits has finished.</summary>
    public static async Task<string> RenderToStringAsync(this IAsyncRenderable template, CancellationToken cancellationToken = default)
    {
        using var writer = new PooledBufferWriter();
        await template.RenderToAsync(writer, cancellationToken).ConfigureAwait(false);
        return writer.GetWrittenString();
    }

    /// <summary>Like <see cref="RenderToAsync(ITemplateRenderable, PipeWriter, CancellationToken)"/>, for a template that needs a context.</summary>
    public static async ValueTask RenderToAsync<TContext>(this ITemplateRenderable<TContext> template, PipeWriter writer, TContext context, CancellationToken cancellationToken = default)
        where TContext : notnull
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(context);

        if (template is ISyncRenderable<TContext> sync)
        {
            sync.RenderTo(writer, context);
        }
        else
        {
            await ((IAsyncRenderable<TContext>)template).__RenderAsync(writer, context, cancellationToken).ConfigureAwait(false);
        }

        await TemplateStreaming.FlushAsync(writer, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Like <see cref="RenderToAsync(ITemplateRenderable, Stream, CancellationToken)"/>, for a template that needs a context.</summary>
    public static async ValueTask RenderToAsync<TContext>(this ITemplateRenderable<TContext> template, Stream stream, TContext context, CancellationToken cancellationToken = default)
        where TContext : notnull
    {
        var writer = PipeWriter.Create(stream, StreamOptions);

        try
        {
            await template.RenderToAsync(writer, context, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await writer.CompleteAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Renders this template to a string, with the context it needs.</summary>
    public static string RenderToString<TContext>(this ISyncRenderable<TContext> template, TContext context)
        where TContext : notnull
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(context);

        using var writer = new PooledBufferWriter();
        template.RenderTo(writer, context);
        return writer.GetWrittenString();
    }

    /// <summary>Renders this template to a string with the context it needs, once everything it awaits has finished.</summary>
    public static async Task<string> RenderToStringAsync<TContext>(this IAsyncRenderable<TContext> template, TContext context, CancellationToken cancellationToken = default)
        where TContext : notnull
    {
        using var writer = new PooledBufferWriter();
        await template.RenderToAsync(writer, context, cancellationToken).ConfigureAwait(false);
        return writer.GetWrittenString();
    }
}
