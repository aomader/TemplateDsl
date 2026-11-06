using System.Buffers;
using Xunit;

namespace Tests;

public sealed partial class TextPage(string text) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [H.Div [ text ]];
}

public sealed partial class NestedRenderPage(string text) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        H.Div [ text ],
        H.P [ new TextPage(text).RenderToString() ],
        H.Div [ text ]
    ];
}

public sealed partial class ThrowingPage(string text) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        H.Div [ text ],
        H.Div [ Fail() ]
    ];

    private static string Fail() => throw new InvalidOperationException("render failed");
}

public class PooledBufferTests
{
    [Theory]
    [InlineData(10)]
    [InlineData(5_000)]
    [InlineData(100_000)]
    [InlineData(3_000_000)]
    public void Output_of_any_size_is_complete(int length)
    {
        var text = new string('x', length);

        Assert.Equal($"<div>{text}</div>", new TextPage(text).RenderToString());
    }

    [Theory]
    [InlineData("€")]
    [InlineData("😀")]
    public void Multibyte_characters_survive_every_buffer_boundary(string character)
    {
        // "<div>" comes first, so these lengths put the character on and around the end of a 4 KB buffer.
        for (var length = 4096 - 12; length <= 4096; length++)
        {
            var text = new string('x', length) + character + "end";

            Assert.Equal($"<div>{text}</div>", new TextPage(text).RenderToString());
        }
    }

    [Fact]
    public void A_render_inside_a_render_keeps_both_outputs_apart()
    {
        var text = new string('x', 6_000);

        Assert.Equal(
            $"<div>{text}</div><p>&lt;div&gt;{text}&lt;/div&gt;</p><div>{text}</div>",
            new NestedRenderPage(text).RenderToString());
    }

    [Fact]
    public void A_failed_render_throws_and_the_next_one_is_correct()
    {
        Assert.Throws<InvalidOperationException>(() => new ThrowingPage("first").RenderToString());

        Assert.Equal("<div>second</div>", new TextPage("second").RenderToString());
    }

    [Fact]
    public async Task Concurrent_renders_do_not_mix()
    {
        await Task.WhenAll(Enumerable.Range(0, 32).Select(worker => Task.Run(() =>
        {
            var text = new string((char)('a' + worker % 26), 5_000 + worker);

            for (var i = 0; i < 200; i++)
            {
                Assert.Equal($"<div>{text}</div>", new TextPage(text).RenderToString());
            }
        })));
    }

    [Fact]
    public void Rendering_to_a_string_allocates_little_more_than_the_string()
    {
        var text = new string('x', 50_000);
        var page = new TextPage(text);
        _ = page.RenderToString();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = page.RenderToString();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        var stringSize = result.Length * sizeof(char);
        Assert.InRange(allocated, stringSize, stringSize + 512);
    }

    [Fact]
    public void The_pool_is_not_the_shared_one() =>
        Assert.NotSame(ArrayPool<byte>.Shared, PooledBufferWriter.Pool);

    [Theory]
    [InlineData(10)]
    [InlineData(5_000)]
    [InlineData(3_000_000)]
    public async Task Async_output_of_any_size_is_complete(int length)
    {
        var text = new string('x', length);

        Assert.Equal(
            $"<ul><li>{text}</li><li>{text}</li></ul>",
            await new AsyncRowsPage(Rows([text, text])).RenderToStringAsync());
    }

    [Fact]
    public async Task Async_multibyte_characters_survive_a_buffer_boundary()
    {
        for (var length = 4096 - 16; length <= 4096; length++)
        {
            var text = new string('x', length) + "😀";

            Assert.Equal(
                $"<ul><li>{text}</li></ul>",
                await new AsyncRowsPage(Rows([text])).RenderToStringAsync());
        }
    }

    [Fact]
    public async Task Async_context_render_is_complete()
    {
        var text = new string('x', 6_000);

        Assert.Equal($"<p>{text}</p>", await new AsyncContextTextPage().RenderToStringAsync(text));
    }

    [Fact]
    public async Task A_token_fired_during_an_async_render_stops_it_and_the_next_one_is_correct()
    {
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new AsyncRowsPage(CancelAfterFirst(cancellation)).RenderToStringAsync(cancellation.Token));

        Assert.Equal("<ul><li>a</li></ul>", await new AsyncRowsPage(Rows(["a"])).RenderToStringAsync());
    }

    [Fact]
    public async Task An_already_fired_token_stops_an_async_render()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new AsyncRowsPage(Rows(["a"])).RenderToStringAsync(cancellation.Token));
    }

    [Fact]
    public async Task A_failing_async_render_throws_and_the_next_one_is_correct()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new AsyncRowsPage(FailAfterFirst()).RenderToStringAsync());

        Assert.Equal("<ul><li>a</li></ul>", await new AsyncRowsPage(Rows(["a"])).RenderToStringAsync());
    }

    [Fact]
    public async Task Concurrent_async_renders_do_not_mix()
    {
        await Task.WhenAll(Enumerable.Range(0, 32).Select(async worker =>
        {
            var text = new string((char)('a' + worker % 26), 5_000 + worker);

            for (var i = 0; i < 50; i++)
            {
                Assert.Equal(
                    $"<ul><li>{text}</li><li>{text}</li></ul>",
                    await new AsyncRowsPage(Rows([text, text])).RenderToStringAsync());
            }
        }));
    }

    [Fact]
    public async Task Flushing_honors_the_token()
    {
        using var writer = new PooledBufferWriter();

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await writer.FlushAsync(cancellation.Token));
    }

    [Fact]
    public void Memory_from_the_writer_is_written_like_a_span()
    {
        using var writer = new PooledBufferWriter();

        "ab"u8.CopyTo(writer.GetMemory(2).Span);
        writer.Advance(2);
        "c"u8.CopyTo(writer.GetMemory(10_000).Span);
        writer.Advance(1);

        Assert.Equal("abc", writer.GetWrittenString());
    }

    [Fact]
    public void Completing_or_cancelling_a_flush_keeps_the_output_readable()
    {
        using var writer = new PooledBufferWriter();
        "abc"u8.CopyTo(writer.GetSpan());
        writer.Advance(3);

        writer.CancelPendingFlush();
        writer.Complete();

        Assert.Equal("abc", writer.GetWrittenString());
    }

    [Fact]
    public async Task Rendering_to_a_string_asynchronously_allocates_little_more_than_the_string()
    {
        var text = new string('x', 50_000);
        _ = await new AsyncRowsPage(Rows([text])).RenderToStringAsync();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = await new AsyncRowsPage(Rows([text])).RenderToStringAsync();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        var stringSize = result.Length * sizeof(char);
        Assert.InRange(allocated, stringSize, stringSize + 4096);
    }

    private static async IAsyncEnumerable<string> Rows(string[] rows)
    {
        foreach (var row in rows)
        {
            yield return row;
        }

        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<string> CancelAfterFirst(CancellationTokenSource cancellation)
    {
        yield return "a";
        await cancellation.CancelAsync();
        await Task.Yield();
        yield return "b";
    }

    private static async IAsyncEnumerable<string> FailAfterFirst()
    {
        yield return "a";
        await Task.Yield();
        throw new InvalidOperationException("render failed");
    }
}

public sealed partial class AsyncRowsPage(IAsyncEnumerable<string> rows) : IAsyncHtmlTemplate
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) =>
        [H.Ul [ await H.EachAsync(rows, row => H.Li [ row ]) ]];
}

public sealed partial class AsyncContextTextPage : IAsyncHtmlTemplate<string>
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(string context, CancellationToken ct) =>
        [H.P [ await Task.FromResult(context) ]];
}
