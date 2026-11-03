using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace TemplateDsl;

/// <summary>
/// Decides when generated async rendering hands buffered output to the client. Used by generated code.
/// All of these throw <see cref="OperationCanceledException"/> if the client is gone or the token fires.
/// </summary>
[SuppressMessage("Design", "CA1062", Justification = "Only generated code calls these, with its own writer, on every checkpoint of a render.")]
public static class TemplateStreaming
{
    /// <summary>The number of unflushed bytes from which a render checkpoint flushes.</summary>
    public const int FlushThreshold = 4096;

    /// <summary>A render checkpoint: flushes once enough output is buffered, which also waits for a slow client.</summary>
    public static ValueTask FlushIfNeededAsync(PipeWriter writer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // A writer that can't report its buffered size flushes at every checkpoint.
        return writer.CanGetUnflushedBytes && writer.UnflushedBytes < FlushThreshold
            ? default
            : FlushAsync(writer, cancellationToken);
    }

    /// <summary>Sends everything buffered so far, because the template is about to wait for something.</summary>
    public static ValueTask FlushBufferedAsync(PipeWriter writer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return writer.CanGetUnflushedBytes && writer.UnflushedBytes == 0
            ? default
            : FlushAsync(writer, cancellationToken);
    }

    /// <summary>
    /// Flushes unconditionally. A pipe whose reader is gone drops writes, so asking the
    /// writer like this is the only way to notice that when nothing seems to be buffered.
    /// </summary>
    public static ValueTask FlushAsync(PipeWriter writer, CancellationToken cancellationToken)
    {
        var flush = writer.FlushAsync(cancellationToken);

        if (flush.IsCompletedSuccessfully)
        {
            ThrowIfStopped(flush.Result, cancellationToken);
            return default;
        }

        return AwaitFlushAsync(flush, cancellationToken);
    }

    /// <summary>
    /// Passes on <paramref name="items"/>, flushing only when the next item isn't ready. A fast source
    /// therefore stays batched by <see cref="FlushThreshold"/>, while a slow one is sent as it arrives.
    /// </summary>
    public static async IAsyncEnumerable<TItem> FlushWhileWaiting<TItem>(
        IAsyncEnumerable<TItem> items,
        PipeWriter writer,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var enumerator = items.GetAsyncEnumerator(cancellationToken);
        await using var disposal = enumerator.ConfigureAwait(false);
        var isFirst = true;

        while (true)
        {
            var next = enumerator.MoveNextAsync();
            ExceptionDispatchInfo? flushFailure = null;

            if (!next.IsCompleted)
            {
                try
                {
                    // Before the first item, an empty flush could send the response headers before anything has been rendered.
                    if (isFirst)
                    {
                        await FlushBufferedAsync(writer, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await FlushAsync(writer, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException exception)
                {
                    flushFailure = ExceptionDispatchInfo.Capture(exception);
                }
            }

            isFirst = false;

            // Always awaited, because the enumerator can't be disposed while it's still moving.
            var hasNext = await next.ConfigureAwait(false);

            flushFailure?.Throw();

            if (!hasNext)
            {
                yield break;
            }

            yield return enumerator.Current;
        }
    }

    private static async ValueTask AwaitFlushAsync(ValueTask<FlushResult> flush, CancellationToken cancellationToken) =>
        ThrowIfStopped(await flush.ConfigureAwait(false), cancellationToken);

    private static void ThrowIfStopped(FlushResult result, CancellationToken cancellationToken)
    {
        if (result.IsCompleted || result.IsCanceled)
        {
            throw new OperationCanceledException("Rendering stopped because the client is gone.", cancellationToken);
        }
    }
}
