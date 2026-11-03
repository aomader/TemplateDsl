using BenchmarkDotNet.Running;
using Benchmarks;

// The comparison only means something if all sides produce the same bytes.
var benchmarks = new RenderBenchmarks();
foreach (var outputs in new[] { benchmarks.RenderButtonOutputs(), benchmarks.RenderPageOutputs() })
{
    if (outputs.Distinct().Count() != 1)
    {
        Console.Error.WriteLine($"Outputs differ:\nTemplateDsl: {outputs[0]}\nBlazor:      {outputs[1]}\nRazorSlices: {outputs[2]}{(outputs.Length > 3 ? $"\nAsync:       {outputs[3]}" : "")}");
        return 1;
    }
}

Console.WriteLine($"Outputs are identical ({benchmarks.RenderPageOutputs()[0].Length} characters for the page).");

if (await new LayoutBenchmarks().VerifyAsync() is { } difference)
{
    Console.Error.WriteLine(difference);
    return 1;
}

if (args is ["--verify-only"])
{
    return 0;
}

BenchmarkSwitcher.FromAssembly(typeof(RenderBenchmarks).Assembly).Run(args);
return 0;
