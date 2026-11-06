using System.Buffers;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace TemplateDsl;

/// <summary>
/// The render methods of every template, so that they don't have to be generated for each one. They take the
/// template as its own type, so that a struct isn't boxed, and also work through an interface, such as a layout chosen
/// at runtime. The NuGet package imports this namespace automatically. The methods with a context only apply to
/// templates that need one, so a template without one doesn't show them.
/// </summary>
public static class TemplateRendering
{
    private static readonly StreamPipeWriterOptions StreamOptions = new(leaveOpen: true);

    /// <summary>
    /// Lets a template be a child of an async template in Build: <c>await new Page()</c>, or <c>await content</c> for
    /// content that may be sync or async. An async template can't become a node any other way, so C# itself rejects
    /// using one without await, or in a template that isn't async. A sync template can be awaited too, and renders in
    /// one go. Like everything in Build, this is compiled by the source generator and never runs.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never), ExcludeFromCodeCoverage]
    public static TaskAwaiter<TNode> GetAwaiter<TNode, TContext>(this IAsyncTemplateContent<TNode, TContext> template)
        where TNode : TemplateNode
        where TContext : notnull =>
        throw TemplateBuild.NotExecutable();

    /// <summary>Writes this template as UTF-8 to <paramref name="writer"/>, without flushing.</summary>
    public static void RenderTo<TTemplate>(this TTemplate template, IBufferWriter<byte> writer)
        where TTemplate : ITemplateContent<TemplateNode, object>
    {
        ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(writer);

        template.__RenderContent(writer, null!);
    }

    /// <summary>Renders this template to a string.</summary>
    public static string RenderToString<TTemplate>(this TTemplate template)
        where TTemplate : ITemplateContent<TemplateNode, object>
    {
        ThrowIfNull(template);

        using var writer = new PooledBufferWriter();
        template.__RenderContent(writer, null!);
        return writer.GetWrittenString();
    }

    /// <summary>
    /// Writes this template as UTF-8 to <paramref name="writer"/> and flushes. An async template streams: output is
    /// sent as it is rendered, a slow client slows down rendering, and if the client goes away or the token fires,
    /// rendering stops. A sync template renders in one go, so all of its output is held in memory until then. Either way,
    /// a client that went away or a fired token ends in an <see cref="OperationCanceledException"/>, so finishing means everything was sent.
    /// </summary>
    public static async ValueTask RenderToAsync<TTemplate>(this TTemplate template, PipeWriter writer, CancellationToken cancellationToken = default)
        where TTemplate : IAsyncTemplateContent<TemplateNode, object>
    {
        ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(writer);

        await template.__RenderContentAsync(writer, null!, cancellationToken).ConfigureAwait(false);
        await TemplateStreaming.FlushAsync(writer, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes this template as UTF-8 to <paramref name="stream"/> and flushes, like the method for a PipeWriter. The stream stays open.</summary>
    public static async ValueTask RenderToAsync<TTemplate>(this TTemplate template, Stream stream, CancellationToken cancellationToken = default)
        where TTemplate : IAsyncTemplateContent<TemplateNode, object>
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

    /// <summary>Renders this template to a string, once everything it awaits has finished.</summary>
    public static async Task<string> RenderToStringAsync<TTemplate>(this TTemplate template, CancellationToken cancellationToken = default)
        where TTemplate : IAsyncRenderable, IAsyncTemplateContent<TemplateNode, object>
    {
        using var writer = new PooledBufferWriter();
        await template.RenderToAsync(writer, cancellationToken).ConfigureAwait(false);
        return writer.GetWrittenString();
    }

    /// <summary>Writes this template as UTF-8 to <paramref name="writer"/> with the context it needs, without flushing.</summary>
    public static void RenderTo<TTemplate, TContext>(this TTemplate template, IBufferWriter<byte> writer, TContext context)
        where TTemplate : ISyncRenderable<TContext>, ITemplateContent<TemplateNode, TContext>
        where TContext : notnull
    {
        ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(context);

        template.__RenderContent(writer, context);
    }

    /// <summary>Renders this template to a string, with the context it needs.</summary>
    public static string RenderToString<TTemplate, TContext>(this TTemplate template, TContext context)
        where TTemplate : ISyncRenderable<TContext>, ITemplateContent<TemplateNode, TContext>
        where TContext : notnull
    {
        ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(context);

        using var writer = new PooledBufferWriter();
        template.__RenderContent(writer, context);
        return writer.GetWrittenString();
    }

    /// <summary>Like <see cref="RenderToAsync{TTemplate}(TTemplate, PipeWriter, CancellationToken)"/>, for a template that needs a context.</summary>
    public static async ValueTask RenderToAsync<TTemplate, TContext>(this TTemplate template, PipeWriter writer, TContext context, CancellationToken cancellationToken = default)
        where TTemplate : ITemplateRenderable<TContext>, IAsyncTemplateContent<TemplateNode, TContext>
        where TContext : notnull
    {
        ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(context);

        await template.__RenderContentAsync(writer, context, cancellationToken).ConfigureAwait(false);
        await TemplateStreaming.FlushAsync(writer, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Like <see cref="RenderToAsync{TTemplate}(TTemplate, Stream, CancellationToken)"/>, for a template that needs a context.</summary>
    public static async ValueTask RenderToAsync<TTemplate, TContext>(this TTemplate template, Stream stream, TContext context, CancellationToken cancellationToken = default)
        where TTemplate : ITemplateRenderable<TContext>, IAsyncTemplateContent<TemplateNode, TContext>
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

    /// <summary>Renders this template to a string with the context it needs, once everything it awaits has finished.</summary>
    public static async Task<string> RenderToStringAsync<TTemplate, TContext>(this TTemplate template, TContext context, CancellationToken cancellationToken = default)
        where TTemplate : IAsyncRenderable<TContext>, IAsyncTemplateContent<TemplateNode, TContext>
        where TContext : notnull
    {
        using var writer = new PooledBufferWriter();
        await template.RenderToAsync(writer, context, cancellationToken).ConfigureAwait(false);
        return writer.GetWrittenString();
    }

    // Without boxing a struct, which is never null: the test of a reference type doesn't box it.
    private static void ThrowIfNull<TTemplate>(TTemplate template, [CallerArgumentExpression(nameof(template))] string? name = null)
    {
        if (!typeof(TTemplate).IsValueType && template is null)
        {
            throw new ArgumentNullException(name);
        }
    }
}
