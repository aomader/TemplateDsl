using System.IO.Pipelines;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Tests;

public sealed partial class EndlessRows(IAsyncEnumerable<string> rows) : IAsyncHtmlTemplate
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) =>
        [H.Ul [ await H.EachAsync(rows, row => H.Li [ row ]) ]];
}

public class DisconnectTests
{
    private sealed class CapturingLogs : ILoggerProvider, ILogger
    {
        public List<string> Problems { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
            {
                lock (Problems)
                {
                    Problems.Add($"{logLevel}: {formatter(state, exception)}");
                }
            }
        }

        public void Dispose()
        {
        }
    }

    private static async IAsyncEnumerable<string> Forever()
    {
        while (true)
        {
            await Task.Delay(5);
            yield return "row";
        }
    }

    [Theory]
    [InlineData("/with-token")]
    [InlineData("/without-token")]
    public async Task Disconnect_ends_the_render_with_a_cancellation_and_logs_no_problem(string path)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var logs = new CapturingLogs();
        var outcome = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        await using var app = builder.Build();

        // Records what the endpoint ended with, and lets it continue to the framework as usual.
        app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
                outcome.TrySetResult(null);
            }
            catch (Exception exception)
            {
                outcome.TrySetResult(exception);
                throw;
            }
        });

        app.MapGet("/with-token", context => new EndlessRows(Forever()).RenderToAsync(context.Response.BodyWriter, context.RequestAborted).AsTask());
        app.MapGet("/without-token", context => new EndlessRows(Forever()).RenderToAsync(context.Response.BodyWriter).AsTask());

        await app.StartAsync(timeout.Token);

        using (var client = new HttpClient())
        using (var response = await client.GetAsync(app.Urls.First() + path, HttpCompletionOption.ResponseHeadersRead, timeout.Token))
        await using (var body = await response.Content.ReadAsStreamAsync(timeout.Token))
        {
            var first = new byte[16];
            await body.ReadExactlyAsync(first, timeout.Token);
            Assert.StartsWith("<ul>", Encoding.UTF8.GetString(first));
        }

        var exception = await outcome.Task.WaitAsync(timeout.Token);
        Assert.IsAssignableFrom<OperationCanceledException>(exception);

        await app.StopAsync(timeout.Token);
        lock (logs.Problems)
        {
            Assert.Empty(logs.Problems);
        }
    }

    [Fact]
    public async Task Finishing_means_everything_was_sent()
    {
        var pipe = new Pipe();
        pipe.Reader.Complete();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await new PageTemplate().RenderToAsync(pipe.Writer));
    }
}
