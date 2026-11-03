using System.Globalization;
using System.Runtime.CompilerServices;
using Xunit;

namespace Tests;

public sealed record Order(int Id, string Product);

public sealed partial class OrderRow(Order order) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() => [H.Tr [ H.Td [ order.Id ], H.Td [ order.Product ] ]];
}

/// <summary>Writes each item with the row template it's given, since an item of any type isn't a node itself.</summary>
public sealed partial class Table<TItem>(IEnumerable<TItem> items, Func<TItem, HtmlTemplate> row) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() => [H.Table [ H.Tbody [ H.Each(items, item => row(item)) ] ]];
}

public sealed partial class Choice<TValue>(IEnumerable<TValue> values, Func<TValue, string> label) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() =>
        [H.Select [ H.Each(values, value => H.Option.Value(label(value)) [ $"{value}" ]) ]];
}

/// <summary>Takes two type parameters, and nests another generic template with one of them.</summary>
public sealed partial class Grouped<TKey, TItem>(TKey key, IEnumerable<TItem> items, Func<TItem, HtmlTemplate> row) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() => [H.Section [ H.H2 [ $"{key}" ], new Table<TItem>(items, row) ]];
}

/// <summary>A type parameter constrained to a template can be nested like any template.</summary>
public sealed partial class Slot<TPart>(TPart part) : HtmlTemplate
    where TPart : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() => [H.Div.Class("slot") [ part ]];
}

/// <summary>Puts children in brackets into a generic template that takes children.</summary>
public sealed partial class PageWithBox<TItem>(TItem item) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() => [new Box<TItem>(item) [ "inner" ]];
}

public sealed partial class Box<TItem>(TItem item) : HtmlTemplateWithChildren
{
    public override IEnumerable<HtmlElement> Build() => [H.Div.Title($"{item}") [ Children ]];
}

/// <summary>A part that needs the context gets it from this template, also through a type parameter.</summary>
public sealed partial class ContextSlot<TPart>(TPart part) : HtmlTemplate<TestContext>
    where TPart : HtmlTemplate<TestContext>
{
    public override IEnumerable<HtmlElement> Build(TestContext context) => [H.Div [ context.Greeting, part ]];
}

public sealed partial class UserName : HtmlTemplate<TestContext>
{
    public override IEnumerable<HtmlElement> Build(TestContext context) => [H.B [ context.User ]];
}

public sealed partial class Formatted<TValue>(TValue value) : HtmlTemplate
    where TValue : struct, ISpanFormattable
{
    public override IEnumerable<HtmlElement> Build() => [H.P.Attr("data-value", value) [ $"{value}" ]];
}

public sealed partial class AsyncList<TItem>(IAsyncEnumerable<TItem> items, Func<TItem, string> text) : AsyncHtmlTemplate
{
    public override async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken cancellationToken) =>
        [H.Ul [ await H.EachAsync(items, item => H.Li [ text(item) ]) ]];
}

public class GenericTemplateTests
{
    private static readonly Order[] Orders = [new(1, "Keyboard <wireless>"), new(2, "Mouse")];

    [Fact]
    public void A_generic_template_writes_its_items_with_the_template_it_is_given()
    {
        Assert.Equal(
            "<table><tbody><tr><td>1</td><td>Keyboard &lt;wireless&gt;</td></tr><tr><td>2</td><td>Mouse</td></tr></tbody></table>",
            new Table<Order>(Orders, order => new OrderRow(order)).RenderToString());
    }

    [Fact]
    public void The_same_template_works_with_other_type_arguments()
    {
        Assert.Equal(
            "<select><option value=\"one\">1</option><option value=\"two\">2</option></select>",
            new Choice<int>([1, 2], number => number == 1 ? "one" : "two").RenderToString());

        Assert.Equal(
            "<select><option value=\"A\">a&amp;b</option></select>",
            new Choice<string>(["a&b"], text => text[..1].ToUpperInvariant()).RenderToString());
    }

    [Fact]
    public void A_generic_template_can_nest_another_one()
    {
        Assert.Equal(
            "<section><h2>2026</h2><table><tbody><tr><td>2</td><td>Mouse</td></tr></tbody></table></section>",
            new Grouped<int, Order>(2026, Orders[1..], order => new OrderRow(order)).RenderToString());
    }

    [Fact]
    public void A_type_parameter_constrained_to_a_template_is_nested_like_one()
    {
        Assert.Equal(
            "<div class=\"slot\"><tr><td>1</td><td>Keyboard &lt;wireless&gt;</td></tr></div>",
            new Slot<OrderRow>(new OrderRow(Orders[0])).RenderToString());
    }

    [Fact]
    public void A_generic_template_takes_children()
    {
        Assert.Equal("<div title=\"7\">inner</div>", new PageWithBox<int>(7).RenderToString());
    }

    [Fact]
    public void A_part_that_needs_the_context_gets_it_through_a_type_parameter()
    {
        Assert.Equal(
            "<div>Hi<b>Ann</b></div>",
            new ContextSlot<UserName>(new UserName()).RenderToString(new TestContext("Hi", "Ann")));
    }

    [Fact]
    public void Values_of_a_type_parameter_are_formatted_and_escaped()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

        try
        {
            Assert.Equal("<p data-value=\"1.5\">1,5</p>", new Formatted<double>(1.5).RenderToString());
            Assert.Equal("<p data-value=\"&lt;7&amp;&gt;\">&lt;7&amp;&gt;</p>", new Formatted<Bracketed>(new Bracketed(7)).RenderToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task A_generic_template_can_stream()
    {
        Assert.Equal("<ul><li>1</li><li>2</li></ul>", await new AsyncList<int>(Numbers(), number => $"{number}").RenderToStringAsync());
    }

    private static async IAsyncEnumerable<int> Numbers([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var number = 1; number <= 2; number++)
        {
            await Task.Delay(0, cancellationToken);
            yield return number;
        }
    }
}
