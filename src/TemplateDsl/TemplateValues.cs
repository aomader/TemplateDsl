using System.Buffers;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace TemplateDsl;

/// <summary>Writes characters as UTF-8, escaped or not.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate void CharacterWriter(ReadOnlySpan<char> characters, IBufferWriter<byte> writer);

/// <summary>Writes values that aren't strings, without creating a string for them. Used by generated code.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[SuppressMessage("Design", "CA1062", Justification = "Only generated code calls these, for every value it writes.")]
public static class TemplateValues
{
    private static readonly CharacterWriter Plain = static (characters, writer) => Encoding.UTF8.GetBytes(characters, writer);

    /// <summary>Writes <paramref name="value"/> as UTF-8. Without a <paramref name="provider"/>, it uses the current culture.</summary>
    public static void Write<TValue>(TValue value, IBufferWriter<byte> writer, string? format = null, IFormatProvider? provider = null)
        where TValue : ISpanFormattable =>
        Write(value, writer, format, provider, Plain);

    /// <summary>
    /// Formats <paramref name="value"/> straight into the output as UTF-8, which is all that most values need. Only if
    /// that doesn't fit, or the result contains one of the <paramref name="special"/> bytes, does it go the long way.
    /// An <paramref name="escape"/> without <paramref name="special"/> bytes, because it replaces characters that aren't
    /// ASCII, always goes the long way, since its characters can't be found as single bytes.
    /// </summary>
    public static void WriteUtf8<TValue>(TValue value, IBufferWriter<byte> writer, string? format, IFormatProvider? provider, SearchValues<byte>? special, CharacterWriter? escape)
        where TValue : IUtf8SpanFormattable, ISpanFormattable
    {
        if (escape is not null && special is null)
        {
            Write(value, writer, format, provider, escape);
            return;
        }

        var span = writer.GetSpan(64);

        if (value.TryFormat(span, out var written, format, provider) && (special is null || span.Slice(0, written).IndexOfAny(special) < 0))
        {
            writer.Advance(written);
            return;
        }

        Write(value, writer, format, provider, escape ?? Plain);
    }

    /// <summary>Formats <paramref name="value"/> and hands the result to <paramref name="write"/>.</summary>
    public static void Write<TValue>(TValue value, IBufferWriter<byte> writer, string? format, IFormatProvider? provider, CharacterWriter write)
        where TValue : ISpanFormattable
    {
        Span<char> buffer = stackalloc char[64];

        if (value.TryFormat(buffer, out var written, format, provider))
        {
            write(buffer.Slice(0, written), writer);
        }
        else
        {
            write(value.ToString(format, provider), writer);
        }
    }
}
