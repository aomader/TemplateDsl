using System.Buffers;
using System.IO.Pipelines;
using System.Text;

namespace TemplateDsl;

/// <summary>
/// The buffer of a render to a string. It rents from a pool of its own, so that rendered output, which can hold
/// personal data, stays away from other code using the shared pool. Arrays aren't cleared when they are returned,
/// so a later render to a string reuses them with old content, which it overwrites and never exposes.
/// A string has no client to send anything to, so flushing does nothing here.
/// </summary>
internal sealed class PooledBufferWriter : PipeWriter, IDisposable
{
    private const int MinimumRentSize = 4096;

    internal static readonly ArrayPool<byte> Pool = ArrayPool<byte>.Create();

    private byte[] _buffer = Pool.Rent(MinimumRentSize);
    private int _written;

    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);

    public override bool CanGetUnflushedBytes => true;

    /// <summary>Always zero, so that render checkpoints don't call <see cref="FlushAsync"/> for nothing.</summary>
    public override long UnflushedBytes => 0;

    public string GetWrittenString() => Encoding.UTF8.GetString(WrittenSpan);

    public override void Advance(int bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bytes, _buffer.Length - _written);
        _written += bytes;
    }

    public override Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureFree(sizeHint);
        return _buffer.AsMemory(_written);
    }

    public override Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureFree(sizeHint);
        return _buffer.AsSpan(_written);
    }

    public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default) =>
        cancellationToken.IsCancellationRequested
            ? ValueTask.FromCanceled<FlushResult>(cancellationToken)
            : new ValueTask<FlushResult>(default(FlushResult));

    /// <summary>Does nothing, because a flush here never waits, so there's nothing to cancel.</summary>
    public override void CancelPendingFlush()
    {
    }

    /// <summary>Does nothing, because the output is still read after rendering. <see cref="Dispose"/> ends the buffer.</summary>
    public override void Complete(Exception? exception = null)
    {
    }

    public void Dispose() => Pool.Return(_buffer);

    private void EnsureFree(int sizeHint)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
        var needed = Math.Max(sizeHint, 1);

        if (needed <= _buffer.Length - _written)
        {
            return;
        }

        var required = checked(_written + needed);
        var doubled = (int)Math.Min(_buffer.Length * 2L, Array.MaxLength);
        var larger = Pool.Rent(Math.Max(required, doubled));
        WrittenSpan.CopyTo(larger);
        Pool.Return(_buffer);
        _buffer = larger;
    }
}
