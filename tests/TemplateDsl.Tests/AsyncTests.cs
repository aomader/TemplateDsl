using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Tests;

public sealed record AsyncRow(string Name, int Size);

public sealed partial class AwaitingTemplate(Task<string> value, bool flag = true) : AsyncHtmlTemplate
{
    public override async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken cancellationToken) =>
    [
        "before ",
        H.Div.Id(await value).Class(flag ? await value : null)
        [
            await value,
            H.Text(await value),
            $"hole {await value} {(await value).Length:D3} ",
            await value == "skip" ? "skipped" : await value
        ],
        " after"
    ];
}

public sealed partial class AsyncStatements(Task<bool> flag, Task<string> name) : AsyncHtmlTemplate
{
    public override async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct)
    {
        var text = await name;
        const string Suffix = "!";

        if (await flag)
        {
            var upper = text.ToUpperInvariant();
            return [H.Div.Id(text)[upper + Suffix]];
        }

        return [H.Div[text + Suffix]];
    }
}

public sealed partial class SyncLocals(int count) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build()
    {
        var label = $"count-{count}";
        var chip = new Chip(label);

        if (count > 0)
        {
            var doubled = count * 2;
            return [H.Div.Id(label)[$"{doubled}"], chip];
        }

        return [chip];
    }
}

public sealed partial class AsyncChip(string label) : AsyncHtmlTemplate
{
    public override async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) =>
        [H.Div[await Task.Run(() => label, ct)]];
}

public sealed partial class AsyncPanel(Task<string> title) : AsyncHtmlTemplateWithChildren
{
    public override async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) =>
        [H.Div.Class(await title)[Children]];
}

public sealed partial class AsyncComposition(IAsyncEnumerable<AsyncRow> rows, string[] names) : AsyncHtmlTemplate
{
    public override async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) =>
    [
        new Chip("sync child"),
        await new AsyncChip("async child"),
        new Panel("sync panel")[await new AsyncChip("in sync panel"), await Task.FromResult("awaited")],
        await new AsyncPanel(Task.FromResult("async-panel"))["text", new Chip("in async panel")],
        H.Div.Class("rows")[await H.EachAsync(rows, row => new Chip(row.Name))],
        await H.EachAsync(rows, row => row.Size > 1 ? H.Div.Id(row.Name) : row.Name),
        await H.EachAsync(rows, async row => H.Div[await Task.FromResult(row.Name)]),
        await H.EachAsync(names, async name => new Chip(await Task.FromResult(name))),
        H.Each(await Task.FromResult(names), name => H.Div[name])
    ];
}

public sealed partial class TokenReader(TaskCompletionSource<CancellationToken> seen) : AsyncHtmlTemplate
{
    public override async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken myToken) =>
        [await Capture(myToken)];

    private Task<string> Capture(CancellationToken token)
    {
        seen.SetResult(token);
        return Task.FromResult("x");
    }
}

public sealed partial class FailingAfterOutput : AsyncHtmlTemplate
{
    public override async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) =>
        ["sent", await Fail()];

    private static async Task<string> Fail()
    {
        await Task.Yield();
        throw new InvalidTimeZoneException("boom");
    }
}

public sealed partial class StreamedRows(IAsyncEnumerable<string> rows) : AsyncHtmlTemplate
{
    public override async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) =>
        ["start ", await H.EachAsync(rows, row => H.Div[row]), " end"];
}

public class AsyncTests
{
    private static async IAsyncEnumerable<AsyncRow> Rows()
    {
        await Task.Yield();
        yield return new AsyncRow("a", 1);
        yield return new AsyncRow("b", 2);
    }

    private static async Task<string> ReadSomeAsync(PipeReader reader)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await reader.ReadAsync(timeout.Token);
        var text = Encoding.UTF8.GetString(result.Buffer.ToArray());
        reader.AdvanceTo(result.Buffer.End);
        return text;
    }

    [Fact]
    public async Task Await_works_in_every_value_position()
    {
        Assert.Equal(
            "before <div id=\"v\" class=\"v\">vvhole v 001 v</div> after",
            await new AwaitingTemplate(Task.FromResult("v")).RenderToStringAsync());
    }

    [Theory]
    [InlineData(true, "<div id=\"ann\">ANN!</div>")]
    [InlineData(false, "<div>ann!</div>")]
    public async Task Locals_and_if_statements_can_await(bool flag, string expected)
    {
        Assert.Equal(expected, await new AsyncStatements(Task.FromResult(flag), Task.FromResult("ann")).RenderToStringAsync());
    }

    [Theory]
    [InlineData(2, "<div id=\"count-2\">4</div><div>count-2</div>")]
    [InlineData(0, "<div>count-0</div>")]
    public void Sync_templates_can_use_locals(int count, string expected)
    {
        Assert.Equal(expected, new SyncLocals(count).RenderToString());
    }

    [Fact]
    public async Task Sync_and_async_templates_and_loops_compose()
    {
        Assert.Equal(
            "<div>sync child</div>"
            + "<div>async child</div>"
            + "<div class=\"panel\"><div class=\"title\">sync panel</div><div class=\"body\"><div>in sync panel</div>awaited</div></div>"
            + "<div class=\"async-panel\">text<div>in async panel</div></div>"
            + "<div class=\"rows\"><div>a</div><div>b</div></div>"
            + "a<div id=\"b\"></div>"
            + "<div>a</div><div>b</div>"
            + "<div>n</div>"
            + "<div>n</div>",
            await new AsyncComposition(Rows(), ["n"]).RenderToStringAsync());
    }

    [Fact]
    public async Task Output_before_an_await_reaches_the_client_before_it_finishes()
    {
        var value = new TaskCompletionSource<string>();
        var pipe = new Pipe();

        var render = new AwaitingTemplate(value.Task, flag: false).RenderToAsync(pipe.Writer).AsTask();

        Assert.Equal("before <div", await ReadSomeAsync(pipe.Reader));
        Assert.False(render.IsCompleted);

        value.SetResult("v");
        await render;
        Assert.Equal(" id=\"v\">vvhole v 001 v</div> after", await ReadSomeAsync(pipe.Reader));
    }

    [Fact]
    public async Task Slow_rows_are_sent_as_they_arrive()
    {
        var rows = Channel.CreateUnbounded<string>();
        var pipe = new Pipe();

        var render = new StreamedRows(rows.Reader.ReadAllAsync()).RenderToAsync(pipe.Writer).AsTask();
        Assert.Equal("start ", await ReadSomeAsync(pipe.Reader));

        rows.Writer.TryWrite("one");
        Assert.Equal("<div>one</div>", await ReadSomeAsync(pipe.Reader));

        rows.Writer.TryWrite("two");
        Assert.Equal("<div>two</div>", await ReadSomeAsync(pipe.Reader));
        Assert.False(render.IsCompleted);

        rows.Writer.Complete();
        await render;
        Assert.Equal(" end", await ReadSomeAsync(pipe.Reader));
    }

    [Fact]
    public async Task Rows_that_are_already_available_stay_batched()
    {
        static async IAsyncEnumerable<string> Ready()
        {
            for (var i = 0; i < 100; i++)
            {
                yield return "row";
            }

            await Task.CompletedTask;
        }

        using var stream = new WriteCountingStream();

        await new StreamedRows(Ready()).RenderToAsync(stream);

        Assert.Equal(1, stream.WriteCount);
    }

    [Fact]
    public async Task Rendering_throws_at_a_slow_stream_when_the_client_goes_away()
    {
        var pipe = new Pipe();
        var enumerated = 0;

        async IAsyncEnumerable<string> Forever()
        {
            while (true)
            {
                await Task.Delay(1);
                enumerated++;
                yield return "row";
            }
        }

        var render = new StreamedRows(Forever()).RenderToAsync(pipe.Writer).AsTask();
        await ReadSomeAsync(pipe.Reader);
        pipe.Reader.Complete();

        await Assert.ThrowsAsync<OperationCanceledException>(() => render.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(enumerated < 1000);
    }

    [Fact]
    public async Task Build_method_receives_the_callers_cancellation_token()
    {
        using var cancellation = new CancellationTokenSource();
        var seen = new TaskCompletionSource<CancellationToken>();

        await new TokenReader(seen).RenderToStringAsync(cancellation.Token);

        Assert.Equal(cancellation.Token, await seen.Task);
    }

    [Fact]
    public async Task Cancellation_stops_a_waiting_stream()
    {
        using var cancellation = new CancellationTokenSource();
        var rows = Channel.CreateUnbounded<string>();
        using var stream = new MemoryStream();

        var render = new StreamedRows(rows.Reader.ReadAllAsync()).RenderToAsync(stream, cancellation.Token).AsTask();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => render);
    }

    [Fact]
    public async Task Exceptions_reach_the_caller_after_output_was_sent()
    {
        var pipe = new Pipe();

        var render = new FailingAfterOutput().RenderToAsync(pipe.Writer).AsTask();

        await Assert.ThrowsAsync<InvalidTimeZoneException>(() => render);
        Assert.Equal("sent", await ReadSomeAsync(pipe.Reader));
    }

    [Theory]
    [InlineData("/body-writer")]
    [InlineData("/body-stream")]
    public async Task Kestrel_client_receives_output_before_an_await_finishes(string path)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var value = new TaskCompletionSource<string>();

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        await using var app = builder.Build();
        app.MapGet("/body-writer", context => new AwaitingTemplate(value.Task, false).RenderToAsync(context.Response.BodyWriter, context.RequestAborted).AsTask());
        app.MapGet("/body-stream", context => new AwaitingTemplate(value.Task, false).RenderToAsync(context.Response.Body, context.RequestAborted).AsTask());
        await app.StartAsync(timeout.Token);

        using var client = new HttpClient();
        using var response = await client.GetAsync(app.Urls.First() + path, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);

        var start = new byte["before <div".Length];
        await body.ReadExactlyAsync(start, timeout.Token);
        Assert.Equal("before <div", Encoding.UTF8.GetString(start));

        value.SetResult("v");

        using var rest = new StreamReader(body);
        Assert.EndsWith("</div> after", await rest.ReadToEndAsync(timeout.Token));

        await app.StopAsync(timeout.Token);
    }

    private sealed class WriteCountingStream : MemoryStream
    {
        public int WriteCount { get; private set; }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            WriteCount++;
            return base.WriteAsync(buffer, cancellationToken);
        }
    }
}
