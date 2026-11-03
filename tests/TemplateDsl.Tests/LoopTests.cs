using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using Xunit;

namespace Tests;

public sealed partial class EachList(IEnumerable<string> items) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() =>
    [
        H.Div.Class("list")
        [
            "start ",
            H.Each(items, item => item == "skip" ? null : H.Div.Class(item)[item]),
            " end"
        ]
    ];
}

public sealed partial class StatementLoops(int count) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build()
    {
        yield return "[";

        foreach (var number in Enumerable.Range(0, count))
        {
            if (number == 1)
            {
                continue;
            }

            yield return H.Div.Id($"f{number}");
        }

        for (var i = 0; i < count; i++)
        {
            if (i == 2)
                break;

            yield return $"{i},";
        }

    }
}

public sealed partial class ForAndWhile(int count) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build()
    {
        for (var i = 0; i < count; i++)
        {
            yield return $"{i},";
        }

        while (count > 100)
        {
            yield return "never";
        }

        yield return "done";
    }
}

public sealed partial class ReturnInLoop(int[] numbers) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build()
    {
        foreach (var number in numbers)
        {
            if (number < 0)
            {
                return [H.Div["negative"]];
            }
        }

        return [H.Div["all positive"]];
    }
}

public sealed partial class NestedLoops(string[] groups, IEnumerable<string> items) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() =>
    [
        H.Each(groups, group => new Panel(group)
        [
            H.Each(items, item => new Chip($"{group}-{item}"))
        ])
    ];
}

public sealed partial class AsyncEachList(IEnumerable<string> items) : AsyncHtmlTemplate
{
    public override async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) =>
    [
        H.Div.Class("list")
        [
            "start ",
            H.Each(items, item => item == "skip" ? null : H.Div.Class(item)[item]),
            " end"
        ]
    ];
}

public sealed partial class AsyncNestedLoops(string[] groups, IEnumerable<string> items) : AsyncHtmlTemplate
{
    public override async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) =>
    [
        H.Each(groups, group => new Panel(group)
        [
            H.Each(items, item => new Chip($"{group}-{item}"))
        ])
    ];
}

public class LoopTests
{
    private static async Task<string> RenderThroughPipeAsync(Func<PipeWriter, ValueTask> render)
    {
        using var stream = new MemoryStream();
        var writer = PipeWriter.Create(stream, new StreamPipeWriterOptions(leaveOpen: true));
        await render(writer);
        await writer.CompleteAsync();
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    [Fact]
    public void Each_repeats_for_every_item_and_skips_null()
    {
        Assert.Equal(
            "<div class=\"list\">start <div class=\"a\">a</div><div class=\"b\">b</div> end</div>",
            new EachList(["a", "skip", "b"]).RenderToString());
    }

    [Fact]
    public void Each_with_no_items_renders_only_the_surroundings()
    {
        Assert.Equal("<div class=\"list\">start  end</div>", new EachList([]).RenderToString());
    }

    [Fact]
    public void Foreach_for_continue_and_break_work()
    {
        Assert.Equal("[<div id=\"f0\"></div><div id=\"f2\"></div>0,1,", new StatementLoops(3).RenderToString());
    }

    [Fact]
    public void For_while_and_yield_work_together()
    {
        Assert.Equal("0,1,done", new ForAndWhile(2).RenderToString());
    }

    [Theory]
    [InlineData(new[] { 1, -1 }, "<div>negative</div>")]
    [InlineData(new[] { 1, 2 }, "<div>all positive</div>")]
    public void Return_inside_a_loop_stops_rendering(int[] numbers, string expected)
    {
        Assert.Equal(expected, new ReturnInLoop(numbers).RenderToString());
    }

    [Fact]
    public async Task Return_inside_a_loop_stops_async_rendering_too()
    {
        Assert.Equal(
            "<div>negative</div>",
            await RenderThroughPipeAsync(w => new ReturnInLoop([-1]).RenderToAsync(w)));
    }

    [Fact]
    public async Task Sync_and_async_rendering_produce_the_same_output()
    {
        var items = Enumerable.Range(0, 3000).Select(i => $"item{i}").ToArray();
        var template = new NestedLoops(["g1", "g2", "g3"], items);

        var sync = template.RenderToString();
        var viaPipe = await RenderThroughPipeAsync(w => template.RenderToAsync(w));

        Assert.True(sync.Length > 3 * TemplateStreaming.FlushThreshold);
        Assert.Equal(sync, viaPipe);
        Assert.Equal(new PanelUser("x", true).RenderToString(), await RenderThroughPipeAsync(w => new PanelUser("x", true).RenderToAsync(w)));
    }
}
