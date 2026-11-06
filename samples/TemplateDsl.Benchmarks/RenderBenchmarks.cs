using System.IO.Pipelines;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Benchmarks.Blazor;
using Benchmarks.Dsl;
using Benchmarks.Slices;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RazorSlices;
using System.Text;

namespace Benchmarks;

[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class RenderBenchmarks
{
    private static readonly IServiceProvider Services = new ServiceCollection().BuildServiceProvider();

    private static readonly Dictionary<string, object?> ButtonParameters = new()
    {
        [nameof(BlazorButton.Label)] = "Hello",
    };

    private static readonly int[] LargeRows = Enumerable.Range(0, 5000).ToArray();

    private static readonly Dictionary<string, object?> LargePageParameters = new()
    {
        [nameof(BlazorLargePage.Rows)] = LargeRows,
    };

    private static readonly Dictionary<string, object?> PageParameters = new()
    {
        [nameof(BlazorPage.UserName)] = "Ann",
        [nameof(BlazorPage.IsAdmin)] = true,
        [nameof(BlazorPage.Unread)] = 5,
    };

    private static readonly SlicePageModel SliceModel = new("Ann", IsAdmin: true, Unread: 5);

    private readonly DslButton _dslButton = new("Hello");
    private readonly DslPage _dslPage = new("Ann", isAdmin: true, unread: 5);
    private readonly DslAsyncPage _dslAsyncPage = new("Ann", isAdmin: true, unread: 5);
    private readonly DslLargePage _dslLargePage = new(LargeRows);
    private readonly DslLargeAsyncPage _dslLargeAsyncPage = new(LargeRows);
    private readonly DslStructButton _dslStructButton = new("Hello");
    private readonly DslStructPage _dslStructPage = new("Ann", IsAdmin: true, Unread: 5);
    private readonly DslStructLargePage _dslStructLargePage = new(LargeRows);

    // Stands in for Response.BodyWriter, which the server provides per request: written to, flushed, never completed.
    // Blazor only writes to a TextWriter, so it gets one over the same pipe, as ASP.NET Core does for Razor views.
    private PipeWriter _pipe = null!;
    private Stream _pipeStream = null!;

    public RenderBenchmarks() => WriteTo(Stream.Null);

    /// <summary>Points the pipe at <paramref name="output"/>, which the output check uses to see what is written.</summary>
    private void WriteTo(Stream output)
    {
        _pipe = PipeWriter.Create(output, new StreamPipeWriterOptions(leaveOpen: true));
        _pipeStream = _pipe.AsStream(leaveOpen: true);
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Button to string")]
    public string Button_TemplateDsl() => _dslButton.RenderToString();

    [Benchmark, BenchmarkCategory("Button to string")]
    public string Button_TemplateDslStruct() => _dslStructButton.RenderToString();

    [Benchmark, BenchmarkCategory("Button to string")]
    public Task<string> Button_Blazor() => RenderBlazorToString<BlazorButton>(ButtonParameters);

    [Benchmark, BenchmarkCategory("Button to string")]
    public ValueTask<string> Button_RazorSlices() => SliceButton.Create(new SliceButtonModel("Hello")).RenderAsync();

    [Benchmark(Baseline = true), BenchmarkCategory("Page to string")]
    public string Page_TemplateDsl() => _dslPage.RenderToString();

    [Benchmark, BenchmarkCategory("Page to string")]
    public string Page_TemplateDslStruct() => _dslStructPage.RenderToString();

    [Benchmark, BenchmarkCategory("Page to string")]
    public Task<string> PageAsync_TemplateDsl() => _dslAsyncPage.RenderToStringAsync();

    [Benchmark, BenchmarkCategory("Page to string")]
    public Task<string> Page_Blazor() => RenderBlazorToString<BlazorPage>(PageParameters);

    [Benchmark, BenchmarkCategory("Page to string")]
    public ValueTask<string> Page_RazorSlices() => SlicePage.Create(SliceModel).RenderAsync();

    [Benchmark(Baseline = true), BenchmarkCategory("Page to pipe")]
    public ValueTask PageToPipe_TemplateDsl() => _dslPage.RenderToAsync(_pipe);

    [Benchmark, BenchmarkCategory("Page to pipe")]
    public ValueTask PageToPipe_TemplateDslStruct() => _dslStructPage.RenderToAsync(_pipe);

    [Benchmark, BenchmarkCategory("Page to pipe")]
    public Task PageToPipe_Blazor() => RenderBlazorToStream<BlazorPage>(PageParameters, _pipeStream);

    [Benchmark, BenchmarkCategory("Page to pipe")]
    public ValueTask PageToPipe_RazorSlices() => SlicePage.Create(SliceModel).RenderAsync(_pipe);

    [Benchmark(Baseline = true), BenchmarkCategory("Large page to pipe")]
    public ValueTask LargePageToPipe_TemplateDsl() => _dslLargePage.RenderToAsync(_pipe);

    [Benchmark, BenchmarkCategory("Large page to pipe")]
    public ValueTask LargePageToPipe_AsyncTemplateDsl() => _dslLargeAsyncPage.RenderToAsync(_pipe);

    [Benchmark, BenchmarkCategory("Large page to pipe")]
    public ValueTask LargePageToPipe_TemplateDslStruct() => _dslStructLargePage.RenderToAsync(_pipe);

    [Benchmark, BenchmarkCategory("Large page to pipe")]
    public Task LargePageToPipe_Blazor() => RenderBlazorToStream<BlazorLargePage>(LargePageParameters, _pipeStream);

    [Benchmark, BenchmarkCategory("Large page to pipe")]
    public ValueTask LargePageToPipe_RazorSlices() => SliceLargePage.Create(LargeRows).RenderAsync(_pipe);

    public string[] RenderButtonOutputs() =>
        [Button_TemplateDsl(), Button_Blazor().GetAwaiter().GetResult(), Button_RazorSlices().AsTask().GetAwaiter().GetResult(), Button_TemplateDslStruct()];

    public string[] RenderPageOutputs() =>
        [Page_TemplateDsl(), Page_Blazor().GetAwaiter().GetResult(), Page_RazorSlices().AsTask().GetAwaiter().GetResult(), PageAsync_TemplateDsl().GetAwaiter().GetResult(), Page_TemplateDslStruct()];

    public string[] RenderLargePageOutputs() => [_dslLargePage.RenderToString(), _dslStructLargePage.RenderToString()];

    /// <summary>What each benchmark writes to the pipe, to check that they all write the same.</summary>
    public async Task<string[]> RenderPipeOutputsAsync()
    {
        async Task<string> Capture(Func<Task> render)
        {
            using var output = new MemoryStream();
            WriteTo(output);
            await render();
            await _pipe.FlushAsync();
            WriteTo(Stream.Null);
            return Encoding.UTF8.GetString(output.ToArray());
        }

        string[] pages =
        [
            await Capture(() => PageToPipe_TemplateDsl().AsTask()),
            await Capture(() => PageToPipe_TemplateDslStruct().AsTask()),
            await Capture(PageToPipe_Blazor),
            await Capture(() => PageToPipe_RazorSlices().AsTask()),
        ];

        string[] largePages =
        [
            await Capture(() => LargePageToPipe_TemplateDsl().AsTask()),
            await Capture(() => LargePageToPipe_AsyncTemplateDsl().AsTask()),
            await Capture(() => LargePageToPipe_TemplateDslStruct().AsTask()),
            await Capture(LargePageToPipe_Blazor),
            await Capture(() => LargePageToPipe_RazorSlices().AsTask()),
        ];

        return pages.Distinct().Count() == 1 && largePages.Distinct().Count() == 1 ? [] : [.. pages, .. largePages];
    }

    // A renderer per render, as in a request: it holds on to every root component it renders.
    private static async Task<string> RenderBlazorToString<TComponent>(Dictionary<string, object?> parameters)
        where TComponent : IComponent
    {
        await using var renderer = new HtmlRenderer(Services, NullLoggerFactory.Instance);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            HtmlRootComponent output = await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters));
            return output.ToHtmlString();
        });
    }

    private static async Task RenderBlazorToStream<TComponent>(Dictionary<string, object?> parameters, Stream stream)
        where TComponent : IComponent
    {
        await using var renderer = new HtmlRenderer(Services, NullLoggerFactory.Instance);

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            HtmlRootComponent output = await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters));

            await using var writer = new HttpResponseStreamWriter(stream, Encoding.UTF8);
            output.WriteHtmlTo(writer);
        });
    }
}
