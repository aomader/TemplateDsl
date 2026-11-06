using System.Globalization;
using Xunit;

namespace Tests;

public sealed partial class NumberChildren(int number, int? missing, int? present, double real, char character, DateOnly date, Guid id, byte small, decimal money) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        H.P [ number ],
        H.P [ missing ],
        H.P [ present ],
        H.P [ real ],
        H.P [ character ],
        H.P [ date ],
        H.P [ id ],
        H.P [ small ],
        H.P [ money ],
        H.P [ 5 ],
        H.P [ number > 0 ? number : null ],
        H.P [ "a", number, "b" ],
        number
    ];
}

public sealed partial class NumberAttributes(int number, int? missing, int? present, bool? flag, char character) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        H.Td.ColSpan(number).RowSpan(missing),
        H.Td.ColSpan(present).RowSpan(5),
        H.Td.ColSpan(number > 0 ? number : 0),
        H.Div.Attr("data-a", number).Attr("data-b", missing).Attr("data-c", present),
        H.Div.Attr("data-a", character),
        H.Input.Disabled(flag),
        H.Input.Disabled(flag ?? true)
    ];
}

public sealed partial class EachNumbers(int[] numbers) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [H.Ul [ H.Each(numbers, n => H.Li [ n ]) ], H.Each(numbers, n => n)];
}

public sealed partial class AsyncNumbers(Task<int> number, Task<int?> missing) : IAsyncHtmlTemplate
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) =>
    [
        H.P [ await number ],
        H.P [ await missing ],
        H.P.Attr("data-a", await number) [ await number ]
    ];
}

public sealed partial class CultureValues(double real, DateOnly date) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [H.P [ real ], H.P [ $"{real}" ], H.P [ date ], H.P [ $"{date}" ]];
}

public sealed partial class LongFormatted(LongValue value) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [H.Div.Attr("data-a", value)];
}

/// <summary>Longer than any stack buffer, and full of characters that must be escaped.</summary>
public readonly struct LongValue : ISpanFormattable
{
    public string ToString(string? format, IFormatProvider? formatProvider) => new string('<', 100);

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        charsWritten = 0;
        return false;
    }
}

public class FormattedValueTests
{
    [Fact]
    public void Values_become_text_and_null_leaves_nothing()
    {
        using var _ = new InvariantCulture();
        var id = Guid.Parse("00000000-0000-0000-0000-000000000001");

        Assert.Equal(
            "<p>7</p><p></p><p>8</p><p>1.5</p><p>x</p><p>01/02/2026</p><p>00000000-0000-0000-0000-000000000001</p><p>3</p><p>9.50</p><p>5</p><p>7</p><p>a7b</p>7",
            new NumberChildren(7, null, 8, 1.5, 'x', new DateOnly(2026, 1, 2), id, 3, 9.50m).RenderToString());
    }

    [Fact]
    public void A_char_is_escaped_as_text_and_as_an_attribute()
    {
        using var _ = new InvariantCulture();
        var id = Guid.Empty;

        Assert.Contains("<p>&lt;</p>", new NumberChildren(1, null, 2, 1, '<', default, id, 1, 1).RenderToString());
        Assert.Contains("data-a=\"&quot;\"", new NumberAttributes(1, null, 2, null, '"').RenderToString());
    }

    [Fact]
    public void Attributes_take_values_and_null_leaves_them_out()
    {
        Assert.Equal(
            "<td colspan=\"7\"></td><td colspan=\"8\" rowspan=\"5\"></td><td colspan=\"7\"></td>"
                + "<div data-a=\"7\" data-c=\"8\"></div><div data-a=\"x\"></div><input><input disabled>",
            new NumberAttributes(7, null, 8, null, 'x').RenderToString());
    }

    [Theory]
    [InlineData(true, "<input disabled>")]
    [InlineData(false, "<input>")]
    public void A_nullable_bool_attribute_follows_its_value(bool flag, string expected) =>
        Assert.StartsWith(expected, new NumberAttributes(1, null, 1, flag, 'x').RenderToString().Substring(new NumberAttributes(1, null, 1, null, 'x').RenderToString().IndexOf("<input", StringComparison.Ordinal)));

    [Fact]
    public void Each_takes_values() =>
        Assert.Equal("<ul><li>1</li><li>2</li></ul>12", new EachNumbers([1, 2]).RenderToString());

    [Fact]
    public async Task Awaited_values_work() =>
        Assert.Equal(
            "<p>4</p><p></p><p data-a=\"4\">4</p>",
            await new AsyncNumbers(Task.FromResult(4), Task.FromResult<int?>(null)).RenderToStringAsync());

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    [InlineData("ar-SA")]
    public void A_value_is_written_like_in_an_interpolated_string(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);

        try
        {
            var real = 1234.5;
            var date = new DateOnly(2026, 11, 1);

            Assert.Equal(
                $"<p>{real}</p><p>{real}</p><p>{date}</p><p>{date}</p>".Replace("&", "&amp;"),
                new CultureValues(real, date).RenderToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void A_value_too_long_for_the_stack_is_complete_and_escaped()
    {
        var expected = string.Concat(Enumerable.Repeat("&lt;", 100));

        Assert.Equal($"<div data-a=\"{expected}\"></div>", new LongFormatted(default).RenderToString());
    }

    private sealed class InvariantCulture : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

        public InvariantCulture() => CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }
}

public sealed partial class Holes(int number, double real, int? missing, int? present, DateOnly date, char character, string? text) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        H.P [ $"a{number}b{real:F2}c{missing}d{present}e{date:yyyy}f{character}g{text}h{number,4}i{number:<0>}" ],
        H.P.Id($"a{number}b{real:F2}c{missing}d{present:D3}e{character}f{number:<0>}"),
        H.Raw($"{number:<0>}")
    ];
}

public sealed partial class AllocationFreeHoles(int number, double real, int? present) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [H.P.Id($"a{number}") [ $"{number} {real:F2} {present}" ]];
}

public sealed partial class AsyncHoles(Task<int> number, Task<int?> missing) : IAsyncHtmlTemplate
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) =>
        [H.P.Id($"a{await number:D2}") [ $"{await number}-{await missing}-{await number:D3}" ]];
}

public class InterpolationHoleTests
{
    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    [InlineData("ar-SA")]
    public void Holes_are_written_like_in_an_interpolated_string(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);

        try
        {
            var number = 5;
            var real = 1234.5;
            int? missing = null;
            int? present = 8;
            var date = new DateOnly(2026, 11, 1);
            var character = '"';
            var text = "&";

            var expectedText = Escape($"a{number}b{real:F2}c{missing}d{present}e{date:yyyy}f{character}g{text}h{number,4}i{number:<0>}");
            var expectedAttribute = Escape(string.Create(CultureInfo.InvariantCulture, $"a{number}b{real:F2}c{missing}d{present:D3}e{character}f{number:<0>}")).Replace("\"", "&quot;");

            Assert.Equal(
                $"<p>{expectedText}</p><p id=\"{expectedAttribute}\"></p>{number:<0>}",
                new Holes(number, real, missing, present, date, character, text).RenderToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    [Fact]
    public void Holes_do_not_create_strings()
    {
        var page = new AllocationFreeHoles(12345, 1.5, 7);
        var writer = new System.Buffers.ArrayBufferWriter<byte>(1024);
        page.RenderTo(writer);
        writer.Clear();

        var before = GC.GetAllocatedBytesForCurrentThread();
        page.RenderTo(writer);

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public async Task Awaited_holes_work() =>
        Assert.Equal(
            "<p id=\"a04\">4--004</p>",
            await new AsyncHoles(Task.FromResult(4), Task.FromResult<int?>(null)).RenderToStringAsync());
}

public sealed partial class FeedHoles(int number) : FormatSamples.IRssTemplate
{
    public IEnumerable<FormatSamples.RssNode> Build() => [FormatSamples.Rss.Title [ $"{number:<0>}" ]];
}

public class FormatsWithoutValuesTests
{
    [Fact]
    public void Holes_work_in_a_format_that_does_not_opt_in() =>
        Assert.Equal("<title>&lt;5&gt;</title>", new FeedHoles(5).RenderToString());
}
