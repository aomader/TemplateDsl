using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Benchmarks.Blazor;
using Benchmarks.Dsl;
using Benchmarks.Slices;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RazorSlices;

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
    private readonly DslLargePage _dslLargePage = new(Enumerable.Range(0, 5000).ToArray());
    private readonly DslLargeAsyncPage _dslLargeAsyncPage = new(Enumerable.Range(0, 5000).ToArray());

    [Benchmark(Baseline = true), BenchmarkCategory("Button to string")]
    public string Button_TemplateDsl() => _dslButton.RenderToString();

    [Benchmark, BenchmarkCategory("Button to string")]
    public Task<string> Button_Blazor() => RenderBlazorToString<BlazorButton>(ButtonParameters);

    [Benchmark, BenchmarkCategory("Button to string")]
    public ValueTask<string> Button_RazorSlices() => SliceButton.Create(new SliceButtonModel("Hello")).RenderAsync();

    [Benchmark(Baseline = true), BenchmarkCategory("Page to string")]
    public string Page_TemplateDsl() => _dslPage.RenderToString();

    [Benchmark, BenchmarkCategory("Page to string")]
    public Task<string> PageAsync_TemplateDsl() => _dslAsyncPage.RenderToStringAsync();

    [Benchmark, BenchmarkCategory("Page to string")]
    public Task<string> Page_Blazor() => RenderBlazorToString<BlazorPage>(PageParameters);

    [Benchmark, BenchmarkCategory("Page to string")]
    public ValueTask<string> Page_RazorSlices() => SlicePage.Create(SliceModel).RenderAsync();

    [Benchmark(Baseline = true), BenchmarkCategory("Page to stream")]
    public ValueTask PageToStream_TemplateDsl() => _dslPage.RenderToAsync(Stream.Null);

    [Benchmark, BenchmarkCategory("Page to stream")]
    public Task PageToStream_Blazor() => RenderBlazorToStream<BlazorPage>(PageParameters, Stream.Null);

    [Benchmark, BenchmarkCategory("Page to stream")]
    public ValueTask PageToStream_RazorSlices() => SlicePage.Create(SliceModel).RenderAsync(Stream.Null);

    [Benchmark(Baseline = true), BenchmarkCategory("Large page to stream")]
    public ValueTask LargePageToStream_TemplateDsl() => _dslLargePage.RenderToAsync(Stream.Null);

    [Benchmark, BenchmarkCategory("Large page to stream")]
    public ValueTask LargePageToStream_AsyncTemplateDsl() => _dslLargeAsyncPage.RenderToAsync(Stream.Null);

    public string[] RenderButtonOutputs() =>
        [Button_TemplateDsl(), Button_Blazor().GetAwaiter().GetResult(), Button_RazorSlices().AsTask().GetAwaiter().GetResult()];

    public string[] RenderPageOutputs() =>
        [Page_TemplateDsl(), Page_Blazor().GetAwaiter().GetResult(), Page_RazorSlices().AsTask().GetAwaiter().GetResult(), PageAsync_TemplateDsl().GetAwaiter().GetResult()];

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

            await using var writer = new StreamWriter(stream, leaveOpen: true);
            output.WriteHtmlTo(writer);
        });
    }
}
