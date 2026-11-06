using Xunit;

namespace Tests;

public enum Tone { Calm, Loud, Silent }

public sealed partial class SwitchExpressionChildren(int number, object? value, Tone tone) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        H.Div [
            number switch
            {
                1 => H.P [ "one" ],
                2 or 3 => H.B [ "few" ],
                > 3 and < 10 => "some",
                var other when other < 0 => H.I [ other ],
                10 => new Kid(),
                11 => number,
                12 => null,
                _ => H.U
            }
        ],
        value switch { string text => H.P [ text ], int count => H.B [ count ], null => "none", _ => throw new InvalidOperationException("odd") },
        H.Span.Id(tone switch { Tone.Calm => "calm", Tone.Loud => "loud", _ => null }) [
            (number, tone) switch { (1, Tone.Calm) => H.P, (_, Tone.Loud) => H.B, _ => null }
        ]
    ];
}

public sealed partial class Kid : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [H.Em];
}

public sealed partial class SwitchWholeOutput(int number) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
        number switch
        {
            1 => [H.P, H.B],
            2 => [],
            _ => [H.I],
        };
}

public sealed partial class NotExhaustive(Tone tone) : IHtmlTemplate
{
#pragma warning disable CS8524
    public IEnumerable<HtmlElement> Build() => [tone switch { Tone.Calm => H.P, Tone.Loud => H.B, Tone.Silent => H.I }];
#pragma warning restore CS8524
}

public sealed partial class SwitchStatementPage(int number, int[] numbers) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build()
    {
        yield return "start";

        switch (number)
        {
            case 1:
                yield return H.P [ "one" ];
                break;
            case 2:
            case 3:
            {
                var doubled = number * 2;
                yield return H.B [ doubled ];
                break;
            }
            case > 100:
                throw new InvalidOperationException("too large");
            case 50:
                yield break;
            default:
                yield return H.I;
                break;
        }

        foreach (var item in numbers)
        {
            switch (item)
            {
                case 0:
                    continue;
                case 9:
                    yield break;
                default:
                    yield return H.Li [ item ];
                    break;
            }

            yield return ",";
        }

        yield return "end";
    }
}

public sealed partial class SwitchReturns(int number) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build()
    {
        switch (number)
        {
            case 1:
                return [H.P];
            default:
                return [H.B];
        }
    }
}

public sealed partial class AsyncSwitch(Task<int> number) : IAsyncHtmlTemplate
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct)
    {
        switch (await number)
        {
            case 1:
                return [H.P [ await number ]];
            default:
                return [await number switch { 2 => await new AsyncKid(), _ => H.B }];
        }
    }
}

public sealed partial class AsyncKid : IAsyncHtmlTemplate
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) => [H.Em [ await Task.FromResult("x") ]];
}

public sealed partial class WithStatements(List<string> log, int count) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build()
    {
        string label;
        var i = 0;
        var total = 0;
        label = count > 1 ? "many" : "one";
        log.Add("before");
        yield return H.P [ label ];

        while (i < count)
        {
            total += i;
            yield return H.Li [ i ];
            i++;
        }

        log.Add("after");

        {
            var shadow = total * 2;
            yield return H.B [ shadow ];
        }

        if (count < 0)
        {
            throw new InvalidOperationException("The count can't be negative.");
        }
    }
}

public sealed partial class AsyncStatements2(List<string> log, Task delay) : IAsyncHtmlTemplate
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct)
    {
        log.Add("before");
        await delay;
        log.Add("after");
        return [H.P];
    }
}

public class StatementTests
{
    [Theory]
    [InlineData(1, "<p>one</p>")]
    [InlineData(3, "<b>few</b>")]
    [InlineData(5, "some")]
    [InlineData(-4, "<i>-4</i>")]
    [InlineData(10, "<em></em>")]
    [InlineData(11, "11")]
    [InlineData(12, "")]
    [InlineData(20, "<u></u>")]
    public void A_switch_expression_chooses_a_child(int number, string expected) =>
        Assert.StartsWith($"<div>{expected}</div>", new SwitchExpressionChildren(number, null, Tone.Silent).RenderToString());

    [Fact]
    public void A_switch_expression_works_with_types_tuples_and_attributes()
    {
        Assert.Equal("<div><p>one</p></div><p>a</p><span id=\"calm\"><p></p></span>", new SwitchExpressionChildren(1, "a", Tone.Calm).RenderToString());
        Assert.Equal("<div><p>one</p></div><b>2</b><span id=\"loud\"><b></b></span>", new SwitchExpressionChildren(1, 2, Tone.Loud).RenderToString());
        Assert.Equal("<div><p>one</p></div>none<span></span>", new SwitchExpressionChildren(1, null, Tone.Silent).RenderToString());
    }

    [Fact]
    public void A_throwing_arm_throws() =>
        Assert.Throws<InvalidOperationException>(() => new SwitchExpressionChildren(1, 1.5, Tone.Calm).RenderToString());

    [Fact]
    public void A_switch_expression_without_a_match_throws_like_in_csharp()
    {
        Assert.Equal("<i></i>", new NotExhaustive(Tone.Silent).RenderToString());
        Assert.Throws<System.Runtime.CompilerServices.SwitchExpressionException>(() => new NotExhaustive((Tone)99).RenderToString());
    }

    [Theory]
    [InlineData(1, "<p></p><b></b>")]
    [InlineData(2, "")]
    [InlineData(3, "<i></i>")]
    public void A_switch_expression_can_choose_the_whole_output(int number, string expected) =>
        Assert.Equal(expected, new SwitchWholeOutput(number).RenderToString());

    [Theory]
    [InlineData(1, "start<p>one</p>end")]
    [InlineData(3, "start<b>6</b>end")]
    [InlineData(50, "start")]
    [InlineData(7, "start<i></i>end")]
    public void A_switch_statement_chooses_output(int number, string expected) =>
        Assert.Equal(expected, new SwitchStatementPage(number, []).RenderToString());

    [Fact]
    public void A_switch_statement_can_throw() =>
        Assert.Throws<InvalidOperationException>(() => new SwitchStatementPage(101, []).RenderToString());

    [Fact]
    public void In_a_loop_break_ends_the_switch_and_continue_the_loop() =>
        Assert.Equal("start<p>one</p><li>1</li>,<li>2</li>,", new SwitchStatementPage(1, [1, 0, 2, 9, 3]).RenderToString());

    [Theory]
    [InlineData(1, "<p></p>")]
    [InlineData(2, "<b></b>")]
    public void A_switch_statement_can_return(int number, string expected) =>
        Assert.Equal(expected, new SwitchReturns(number).RenderToString());

    [Theory]
    [InlineData(1, "<p>1</p>")]
    [InlineData(2, "<em>x</em>")]
    [InlineData(3, "<b></b>")]
    public async Task Switches_can_await(int number, string expected) =>
        Assert.Equal(expected, await new AsyncSwitch(Task.FromResult(number)).RenderToStringAsync());

    [Fact]
    public void Statements_run_in_order_between_the_output()
    {
        var log = new List<string>();

        Assert.Equal("<p>many</p><li>0</li><li>1</li><li>2</li><b>6</b>", new WithStatements(log, 3).RenderToString());
        Assert.Equal(["before", "after"], log);
    }

    [Fact]
    public void A_throw_statement_throws() =>
        Assert.Throws<InvalidOperationException>(() => new WithStatements([], -1).RenderToString());

    [Fact]
    public async Task An_awaited_statement_waits()
    {
        var log = new List<string>();
        var delay = new TaskCompletionSource();
        var render = new AsyncStatements2(log, delay.Task).RenderToStringAsync();

        Assert.Equal(["before"], log);
        delay.SetResult();

        Assert.Equal("<p></p>", await render);
        Assert.Equal(["before", "after"], log);
    }
}
