using System.Text.RegularExpressions;
using BenchmarkDotNet.Attributes;
using Benchmarks.Layouts;
using Benchmarks.Slices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Benchmarks;

/// <summary>
/// A whole request for a page with a layout, a head section, a condition and a partial per row, each written
/// the usual way. All are served by the same in-process app, so routing and the test client are shared.
/// </summary>
[MemoryDiagnoser]
public partial class LayoutBenchmarks
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    [GlobalSetup]
    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(LayoutBenchmarks).Assembly.GetName().Name,
        });

        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddRazorPages().AddApplicationPart(typeof(LayoutBenchmarks).Assembly);

        _app = builder.Build();
        _app.MapRazorPages();
        _app.MapGet("/dsl", (HttpContext context) =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            return new DslMainLayout(new DslDashboardPage(Dashboard.Sample)).RenderToAsync(context.Response.BodyWriter, context.RequestAborted);
        });

        _app.MapGet("/slices", () => SliceDashboard.Create(Dashboard.Sample));

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    [GlobalCleanup]
    public async Task StopAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Benchmark(Baseline = true)]
    public Task<byte[]> Layout_TemplateDsl() => _client.GetByteArrayAsync("/dsl");

    [Benchmark]
    public Task<byte[]> Layout_RazorPages() => _client.GetByteArrayAsync("/razor");

    [Benchmark]
    public Task<byte[]> Layout_RazorSlices() => _client.GetByteArrayAsync("/slices");

    /// <summary>
    /// Normally formatted Razor files also output the whitespace between their tags. That's part of what
    /// they do, so it stays in the benchmark, but it's the only difference this check allows.
    /// </summary>
    public async Task<string?> VerifyAsync()
    {
        await StartAsync();

        try
        {
            var dsl = await _client.GetStringAsync("/dsl");
            var razor = await _client.GetStringAsync("/razor");
            var slices = await _client.GetStringAsync("/slices");
            var same = Normalize(dsl) == Normalize(razor) && Normalize(dsl) == Normalize(slices);

            Console.WriteLine($"Layout page: {dsl.Length} characters with TemplateDsl, {razor.Length} with Razor Pages, {slices.Length} with RazorSlices.");

            return same ? null : $"Layout outputs differ:\nTemplateDsl: {Normalize(dsl)}\nRazor Pages: {Normalize(razor)}\nRazorSlices: {Normalize(slices)}";
        }
        finally
        {
            await StopAsync();
        }
    }

    private static string Normalize(string html) => BetweenTags().Replace(html, "><").Trim();

    [GeneratedRegex(@">\s+<")]
    private static partial Regex BetweenTags();
}
