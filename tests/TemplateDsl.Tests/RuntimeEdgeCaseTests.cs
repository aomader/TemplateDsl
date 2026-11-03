using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Tests;

public sealed partial class TupleSwitch(int first, int second) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build()
    {
        switch (first, second)
        {
            case (1, 2):
                yield return "one, two";
                break;
            default:
                yield return "other";
                break;
        }
    }
}

public sealed partial class GuardedArm(int number, bool isLate) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() =>
    [
        number switch
        {
            1 when isLate => "late",
            1 => "one",
            _ when isLate => "late other",
            _ => "other",
        }
    ];
}

public sealed partial class NamedAttrArguments(string value) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() => [H.Div.Attr(value: value, name: "data-value").Attr(name: "data-name", value: value)];
}

public sealed partial class NumberForPeople(double value) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() => [H.Div.Attr("aria-label", value).Attr("data-value", value)];
}

public sealed partial class NullConstants : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() => [H.Div.Hidden(null), H.Input.Type(null), H.Input.Type((InputType?)null)];
}

public sealed partial class DateAndGuidFormats(DateTime date, DateOnly day, TimeOnly time, DateTimeOffset moment, Guid id) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() =>
    [
        H.Div.Attr("data-month", $"{date:MMM}").Attr("data-day", $"{day:ddd}").Attr("data-id", $"{id:N}"),
        H.Div.Attr("data-time", time).Attr("data-moment", moment),
        H.P [ $"{day:yyyy'<'MM}" ]
    ];
}

public sealed partial class LongValues(double number, DateTime date) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() =>
    [
        H.P [ $"{number:F70}" ],
        H.Div.Attr("data-date", $"{date:yyyyMMddHHmmssyyyyMMddHHmmssyyyyMMddHHmmssyyyyMMddHHmmssyyyyMMddHHmmss}")
    ];
}

public sealed partial class FormattedBracketed(Bracketed value) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() => [H.P.Attr("data-value", value) [ $"{value:wide}" ]];
}

public sealed partial class ParenthesizedEach(int[] numbers) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() => [H.Ul [ H.Each(numbers, (number) => H.Li [ number ]) ]];
}

/// <summary>An async sequence of its own, not one from an async iterator.</summary>
public sealed class Countdown(int from) : IAsyncEnumerable<int>
{
    public async IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        for (var number = from; number > 0; number--)
        {
            await Task.Yield();
            yield return number;
        }
    }
}

public sealed partial class CountdownList(Countdown countdown) : AsyncHtmlTemplate
{
    public override async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken cancellationToken) =>
        [H.Ul [ await H.EachAsync(countdown, number => H.Li [ number ]) ]];
}

public sealed partial class FailingItem(IAsyncEnumerable<int> numbers) : AsyncHtmlTemplate
{
    public override async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken cancellationToken) =>
        [H.Ul [ await H.EachAsync(numbers, number => number == 2 ? throw new InvalidOperationException("two") : H.Li [ number ]) ]];
}

public sealed partial class FailingPage(bool fail) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() => [H.P [ fail ? throw new InvalidOperationException("failed") : "fine" ]];
}

public sealed partial class FailingPageWithContext : HtmlTemplate<TestContext>
{
    public override IEnumerable<HtmlElement> Build(TestContext context) =>
        [H.P [ context.User == "fail" ? throw new InvalidOperationException("failed") : context.User ]];
}

/// <summary>Renders by hand, without being a template, so it has no format.</summary>
public sealed class HandWritten : ISyncRenderable
{
    public void RenderTo(IBufferWriter<byte> writer) => writer.Write("by hand"u8);
}

public sealed partial class AsyncGreetingFromContext : AsyncHtmlTemplate<TestContext>
{
    public override async Task<IEnumerable<HtmlElement>> BuildAsync(TestContext context, CancellationToken cancellationToken) =>
        [H.P [ await Task.FromResult(context.Greeting) ]];
}

/// <summary>Hands out exactly the space asked for, as a PipeWriter does at the end of a segment.</summary>
public sealed class TightBufferWriter : IBufferWriter<byte>
{
    private readonly ArrayBufferWriter<byte> _inner = new();
    private byte[] _span = [];

    public ReadOnlyMemory<byte> Written => _inner.WrittenMemory;

    public void Advance(int count) => _inner.Write(_span.AsSpan(0, count));

    public Memory<byte> GetMemory(int sizeHint = 0) => _span = new byte[Math.Max(sizeHint, 1)];

    public Span<byte> GetSpan(int sizeHint = 0) => _span = new byte[Math.Max(sizeHint, 1)];
}

/// <summary>A stream whose writes and flushes complete later, as a network stream's do.</summary>
public sealed class SlowStream : MemoryStream
{
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        await base.WriteAsync(buffer, cancellationToken);
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        await base.FlushAsync(cancellationToken);
    }
}

public class RuntimeEdgeCaseTests
{
    [Theory]
    [InlineData(1, 2, "one, two")]
    [InlineData(2, 1, "other")]
    public void A_switch_statement_can_switch_on_a_tuple(int first, int second, string expected)
    {
        Assert.Equal(expected, new TupleSwitch(first, second).RenderToString());
    }

    [Theory]
    [InlineData(1, true, "late")]
    [InlineData(1, false, "one")]
    [InlineData(2, true, "late other")]
    [InlineData(2, false, "other")]
    public void A_switch_expression_arm_can_have_a_guard(int number, bool isLate, string expected)
    {
        Assert.Equal(expected, new GuardedArm(number, isLate).RenderToString());
    }

    [Fact]
    public void Attr_arguments_can_be_named_in_any_order()
    {
        Assert.Equal("<div data-value=\"&lt;v&gt;\" data-name=\"&lt;v&gt;\"></div>", new NamedAttrArguments("<v>").RenderToString());
    }

    [Fact]
    public void An_attribute_list_value_for_people_uses_the_current_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

        try
        {
            Assert.Equal("<div aria-label=\"1,5\" data-value=\"1.5\"></div>", new NumberForPeople(1.5).RenderToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Null_constants_leave_attributes_out()
    {
        Assert.Equal("<div></div><input><input>", new NullConstants().RenderToString());
    }

    [Fact]
    public void Dates_and_guids_are_written_in_their_format_or_as_iso()
    {
        var html = new DateAndGuidFormats(
            new DateTime(2026, 11, 2, 10, 30, 0),
            new DateOnly(2026, 11, 2),
            new TimeOnly(10, 30, 5),
            new DateTimeOffset(2026, 11, 2, 10, 30, 0, TimeSpan.FromHours(1)),
            Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e")).RenderToString();

        Assert.Equal(
            "<div data-month=\"Nov\" data-day=\"Mon\" data-id=\"0f8fad5bd9cb469fa16570867728950e\"></div>"
            + "<div data-time=\"10:30:05\" data-moment=\"2026-11-02T10:30:00+01:00\"></div>"
            + "<p>2026&lt;11</p>",
            html);
    }

    [Fact]
    public void Values_longer_than_the_formatting_buffer_are_written_whole()
    {
        var html = new LongValues(1.5, new DateTime(2026, 11, 2, 10, 30, 0)).RenderToString();

        Assert.Equal(
            $"<p>{1.5.ToString("F70", CultureInfo.CurrentCulture)}</p><div data-date=\"{string.Concat(Enumerable.Repeat("20261102103000", 5))}\"></div>",
            html);
    }

    [Fact]
    public void Values_are_written_whole_into_a_writer_that_hands_out_small_spans()
    {
        var writer = new TightBufferWriter();

        new LongValues(1.5, new DateTime(2026, 11, 2, 10, 30, 0)).RenderTo(writer);

        Assert.Equal(new LongValues(1.5, new DateTime(2026, 11, 2, 10, 30, 0)).RenderToString(), Encoding.UTF8.GetString(writer.Written.ToArray()));
    }

    [Fact]
    public void A_value_that_only_formats_as_characters_gets_its_format()
    {
        Assert.Equal("<p data-value=\"&lt;3&amp;&gt;\">&lt;3&amp;&gt;</p>", new FormattedBracketed(new Bracketed(3)).RenderToString());
    }

    [Fact]
    public void Each_takes_a_lambda_with_parentheses()
    {
        Assert.Equal("<ul><li>1</li><li>2</li></ul>", new ParenthesizedEach([1, 2]).RenderToString());
    }

    [Fact]
    public async Task EachAsync_takes_any_async_sequence()
    {
        Assert.Equal("<ul><li>3</li><li>2</li><li>1</li></ul>", await new CountdownList(new Countdown(3)).RenderToStringAsync());
    }

    [Fact]
    public async Task An_item_that_fails_stops_the_sequence()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new FailingItem(Numbers()).RenderToStringAsync());

        Assert.Equal("two", error.Message);
    }

    [Fact]
    public async Task Rendering_to_a_slow_stream_sends_everything()
    {
        using var stream = new SlowStream();
        using var withContext = new SlowStream();

        await new FailingPage(fail: false).RenderToAsync(stream);
        await new FailingPageWithContext().RenderToAsync(withContext, new TestContext("Hi", "you"));

        Assert.Equal("<p>fine</p>", Encoding.UTF8.GetString(stream.ToArray()));
        Assert.Equal("<p>you</p>", Encoding.UTF8.GetString(withContext.ToArray()));
    }

    [Fact]
    public async Task A_template_that_fails_while_rendering_to_a_stream_still_completes_the_writer()
    {
        using var stream = new SlowStream();

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await new FailingPage(fail: true).RenderToAsync(stream));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await new FailingPageWithContext().RenderToAsync(stream, new TestContext("Hi", "fail")));

        Assert.True(stream.CanWrite);
    }

    [Fact]
    public async Task Render_methods_reject_null()
    {
        var page = new FailingPage(fail: false);
        var withContext = new FailingPageWithContext();
        var context = new TestContext("Hi", "you");
        var writer = System.IO.Pipelines.PipeWriter.Create(Stream.Null);

        Assert.Throws<ArgumentNullException>(() => TemplateRendering.RenderToString(null!));
        Assert.Throws<ArgumentNullException>(() => TemplateRendering.RenderToString<TestContext>(null!, context));
        Assert.Throws<ArgumentNullException>(() => withContext.RenderToString(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await TemplateRendering.RenderToAsync(null!, writer));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await page.RenderToAsync((System.IO.Pipelines.PipeWriter)null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await TemplateRendering.RenderToAsync<TestContext>(null!, writer, context));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await withContext.RenderToAsync((System.IO.Pipelines.PipeWriter)null!, context));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await withContext.RenderToAsync(writer, null!));
    }

    [Fact]
    public async Task Results_reject_a_missing_http_context()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => new FailingPage(fail: false).ToResult().ExecuteAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => new FailingPageWithContext().ToResult(new TestContext("Hi", "you")).ExecuteAsync(null!));
    }

    [Theory]
    [MemberData(nameof(Results))]
    public async Task Results_get_the_content_type_of_their_format(Func<IResult> result, string contentType, string body)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await result().ExecuteAsync(context);

        Assert.Equal(contentType, context.Response.ContentType);
        Assert.Equal(body, Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
    }

    public static TheoryData<Func<IResult>, string, string> Results => new()
    {
        { () => new EuroPrice(2m).ToResult(), "text/plain; charset=utf-8", "<p>2.00 EUR</p><p>2</p>" },
        { () => new HandWritten().ToResult(), "text/plain; charset=utf-8", "by hand" },
        { () => new AsyncGreetingFromContext().ToResult(new TestContext("Hello", "given")), "text/html; charset=utf-8", "<p>Hello</p>" },
    };

    private static async IAsyncEnumerable<int> Numbers([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var number = 1; number <= 3; number++)
        {
            await Task.Delay(0, cancellationToken);
            yield return number;
        }
    }
}
