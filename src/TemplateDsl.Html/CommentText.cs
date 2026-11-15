using System.Buffers;
using System.Text;

namespace TemplateDsl.Html;

/// <summary>
/// Writes the text of an HTML comment, so that it can't end the comment or break it, without changing how it reads.
/// A browser doesn't decode character references in a comment, so escaping would show in the source. Instead, a
/// zero-width space goes where the text would otherwise be dangerous, and nowhere else:
/// <list type="bullet">
/// <item>between two dashes, so that the text has no <c>--</c>, which <c>--&gt;</c>, <c>--!&gt;</c> and <c>&lt;!--</c> need, and which XML forbids;</item>
/// <item>before a <c>&gt;</c> at the start, or after a <c>-</c> at the start that is followed by one, which would close the comment at once;</item>
/// <item>after a <c>-</c> at the end, which would otherwise touch the <c>--&gt;</c> that ends the comment.</item>
/// </list>
/// Other text, such as <c>2026-11-14</c> or <c>a -&gt; b</c>, is written as it is.
/// </summary>
public sealed class CommentText : ITextWriter
{
    /// <inheritdoc/>
    public static void Write(ReadOnlySpan<char> text, IBufferWriter<byte> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var breaks = (i > 0 && text[i] == '-' && text[i - 1] == '-')
                || (i == 0 && text[0] == '>')
                || (i == 1 && text[0] == '-' && text[1] == '>');

            if (breaks)
            {
                Encoding.UTF8.GetBytes(text[start..i], writer);
                writer.Write("​"u8);
                start = i;
            }
        }

        Encoding.UTF8.GetBytes(text[start..], writer);

        if (text.Length > 0 && text[^1] == '-')
        {
            writer.Write("​"u8);
        }
    }
}
