using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Tests;

public sealed partial class UnicodeTemplate : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [H.Div.Class("äö€😀")];
}

public sealed partial class EmptyTemplate : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [];
}

public class StreamingTests
{
    private const string PageHtml = "<html><body><div></div><div><div></div></div></body></html>";

    [Fact]
    public void RenderTo_writes_utf8_to_a_buffer_writer()
    {
        var writer = new ArrayBufferWriter<byte>();

        new UnicodeTemplate().RenderTo(writer);

        Assert.Equal("<div class=\"äö€😀\"></div>"u8.ToArray(), writer.WrittenSpan.ToArray());
    }

    [Fact]
    public void RenderToString_matches_the_streamed_bytes()
    {
        Assert.Equal("<div class=\"äö€😀\"></div>", new UnicodeTemplate().RenderToString());
        Assert.Equal(PageHtml, new PageTemplate().RenderToString());
    }

    [Fact]
    public void Empty_template_renders_nothing()
    {
        var writer = new ArrayBufferWriter<byte>();

        new EmptyTemplate().RenderTo(writer);

        Assert.Equal(0, writer.WrittenCount);
        Assert.Equal("", new EmptyTemplate().RenderToString());
    }

    [Fact]
    public async Task RenderToAsync_writes_to_a_stream_and_leaves_it_open()
    {
        using var stream = new MemoryStream();

        await new PageTemplate().RenderToAsync(stream);

        Assert.True(stream.CanWrite);
        Assert.Equal(PageHtml, Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public async Task RenderToAsync_flushes_to_a_pipe_writer()
    {
        var pipe = new Pipe();

        await new PageTemplate().RenderToAsync(pipe.Writer);

        // Readable before the writer completes, so the data was flushed.
        var result = await pipe.Reader.ReadAsync();
        Assert.Equal(PageHtml, Encoding.UTF8.GetString(result.Buffer.ToArray()));
    }

    [Fact]
    public async Task RenderToAsync_honors_cancellation_on_a_stream()
    {
        using var stream = new MemoryStream();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await new PageTemplate().RenderToAsync(stream, new CancellationToken(canceled: true)));
    }

    [Theory]
    [InlineData("/body-writer")]
    [InlineData("/body-stream")]
    public async Task Kestrel_client_receives_the_html_before_the_response_ends(string path)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var releaseResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        await using var app = builder.Build();

        app.MapGet("/body-writer", async context =>
        {
            await new PageTemplate().RenderToAsync(context.Response.BodyWriter, context.RequestAborted);
            await releaseResponse.Task;
        });

        app.MapGet("/body-stream", async context =>
        {
            await new PageTemplate().RenderToAsync(context.Response.Body, context.RequestAborted);
            await releaseResponse.Task;
        });

        await app.StartAsync(timeout.Token);

        using var client = new HttpClient();
        using var response = await client.GetAsync(
            app.Urls.First() + path,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);

        // The handler is still blocked on releaseResponse, so these bytes can only have
        // arrived because rendering flushed them to the connection.
        await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
        var received = new byte[Encoding.UTF8.GetByteCount(PageHtml)];
        await body.ReadExactlyAsync(received, timeout.Token);

        Assert.Equal(PageHtml, Encoding.UTF8.GetString(received));

        releaseResponse.SetResult();
        Assert.Equal(-1, body.ReadByte());

        await app.StopAsync(timeout.Token);
    }

    [Fact]
    public async Task Stream_receives_many_writes_for_a_large_page()
    {
        using var stream = new CountingStream();
        var items = Enumerable.Range(0, 5000).Select(i => $"item{i}");

        await new EachList(items).RenderToAsync(stream);

        Assert.True(stream.WriteCount > 5, $"Only {stream.WriteCount} writes");
    }

    [Fact]
    public async Task Small_page_is_written_in_one_go()
    {
        using var stream = new CountingStream();

        await new EachList(["a", "b"]).RenderToAsync(stream);

        Assert.Equal(1, stream.WriteCount);
    }

    [Fact]
    public async Task Rendering_waits_for_a_slow_reader()
    {
        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 16 * 1024, resumeWriterThreshold: 8 * 1024));
        var enumerated = 0;
        var items = Enumerable.Range(0, 100_000).Select(i => { enumerated++; return $"item{i}"; });

        var render = new AsyncEachList(items).RenderToAsync(pipe.Writer).AsTask();

        // Nothing is reading, so rendering has to stop at the pipe's limit instead of buffering it all.
        await WaitUntilAsync(() => enumerated > 0 && Stable(ref enumerated));
        Assert.False(render.IsCompleted);
        Assert.InRange(enumerated, 1, 5000);

        // Completing the writer from here, not from the read loop: checking whether rendering is done
        // between two reads can miss the moment it finishes, and the next read would then wait forever.
        var writerCompleted = render.ContinueWith(_ => pipe.Writer.Complete(), TaskScheduler.Default);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var total = 0L;
        while (true)
        {
            var result = await pipe.Reader.ReadAsync(timeout.Token);
            total += result.Buffer.Length;
            pipe.Reader.AdvanceTo(result.Buffer.End);

            if (result.IsCompleted)
            {
                break;
            }
        }

        await writerCompleted;
        await render;
        Assert.Equal(100_000, enumerated);
        Assert.Equal(Encoding.UTF8.GetByteCount(new EachList(items).RenderToString()), total);
    }

    [Fact]
    public async Task A_sync_template_renders_in_one_go_before_anything_is_sent()
    {
        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 16 * 1024, resumeWriterThreshold: 8 * 1024));
        var enumerated = 0;
        var items = Enumerable.Range(0, 10_000).Select(i => { enumerated++; return $"item{i}"; });

        var render = new EachList(items).RenderToAsync(pipe.Writer);

        // Nothing is reading, and all of it is rendered anyway. Only the flush at the end waits for the reader.
        Assert.Equal(10_000, enumerated);
        Assert.False(render.IsCompleted);

        var writerCompleted = render.AsTask().ContinueWith(_ => pipe.Writer.Complete(), TaskScheduler.Default);

        while (true)
        {
            var result = await pipe.Reader.ReadAsync();
            pipe.Reader.AdvanceTo(result.Buffer.End);

            if (result.IsCompleted)
            {
                break;
            }
        }

        await writerCompleted;
    }

    [Fact]
    public async Task A_sync_template_stops_at_the_end_if_the_token_fired_while_rendering()
    {
        using var cancellation = new CancellationTokenSource();
        var enumerated = 0;
        var items = Enumerable.Range(0, 100).Select(i =>
        {
            if (++enumerated == 10)
            {
                cancellation.Cancel();
            }

            return "x";
        });

        using var stream = new MemoryStream();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await new EachList(items).RenderToAsync(stream, cancellation.Token));
        Assert.Equal(100, enumerated);
    }

    [Fact]
    public async Task Rendering_throws_when_the_client_goes_away()
    {
        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 16 * 1024, resumeWriterThreshold: 8 * 1024));
        var enumerated = 0;
        var items = Enumerable.Range(0, 1_000_000).Select(i => { enumerated++; return $"item{i}"; });

        var render = new AsyncNestedLoops(["g"], items).RenderToAsync(pipe.Writer).AsTask();
        await pipe.Reader.ReadAsync();
        pipe.Reader.Complete();

        await Assert.ThrowsAsync<OperationCanceledException>(() => render);

        Assert.InRange(enumerated, 1, 100_000);
    }

    [Fact]
    public async Task Cancellation_stops_rendering_in_a_loop()
    {
        using var cancellation = new CancellationTokenSource();
        var enumerated = 0;
        var items = Enumerable.Range(0, 1_000_000).Select(i =>
        {
            if (++enumerated == 10)
            {
                cancellation.Cancel();
            }

            return "x";
        });

        using var stream = new MemoryStream();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await new AsyncEachList(items).RenderToAsync(stream, cancellation.Token));
        Assert.Equal(10, enumerated);
    }

    [Theory]
    [InlineData("/body-writer")]
    [InlineData("/body-stream")]
    public async Task Kestrel_client_receives_output_while_the_loop_is_still_running(string path)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var clientHasFirstPart = new ManualResetEventSlim();
        var first = new string('a', 2 * TemplateStreaming.FlushThreshold);

        IEnumerable<string> Items()
        {
            yield return first;
            Assert.True(clientHasFirstPart.Wait(TimeSpan.FromSeconds(30)), "The client never received the first part.");
            yield return "last";
        }

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        await using var app = builder.Build();
        app.MapGet("/body-writer", context => new AsyncEachList(Items()).RenderToAsync(context.Response.BodyWriter, context.RequestAborted).AsTask());
        app.MapGet("/body-stream", context => new AsyncEachList(Items()).RenderToAsync(context.Response.Body, context.RequestAborted).AsTask());
        await app.StartAsync(timeout.Token);

        using var client = new HttpClient();
        using var response = await client.GetAsync(app.Urls.First() + path, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);

        var expectedStart = Encoding.UTF8.GetBytes($"<div class=\"list\">start <div class=\"{first}\">");
        var received = new byte[expectedStart.Length];
        await body.ReadExactlyAsync(received, timeout.Token);
        Assert.Equal(expectedStart, received);

        clientHasFirstPart.Set();

        using var rest = new StreamReader(body);
        Assert.EndsWith("<div class=\"last\">last</div> end</div>", await rest.ReadToEndAsync(timeout.Token));

        await app.StopAsync(timeout.Token);
    }

    private static int s_lastSeen;

    private static bool Stable(ref int value)
    {
        var current = Volatile.Read(ref value);
        var stable = current == s_lastSeen;
        s_lastSeen = current;
        return stable;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++)
        {
            await Task.Delay(50);
        }
    }

    private sealed class CountingStream : MemoryStream
    {
        public int WriteCount { get; private set; }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            WriteCount++;
            return base.WriteAsync(buffer, cancellationToken);
        }
    }
}
