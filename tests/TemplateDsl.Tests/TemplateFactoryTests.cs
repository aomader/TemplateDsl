using Xunit;
using static Tests.Templates;
using T = Tests.Templates;

namespace Tests;

// Templates that create each other through the factories of this project, without new. The generator writes the
// factories, so it can't see them in Build, and recognizes each call by the name of the template it creates.

public readonly partial record struct FactoryRow(string Name) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [H.Li [ Name ]];
}

public sealed partial class FactoryCard(string title, string? note = null, int level = 2) : IHtmlTemplate, IAcceptsChildren
{
    public IEnumerable<HtmlElement> Build() => [H.Section.Attr("data-level", level) [ H.H2 [ title ], note, Children ]];
}

public sealed partial class FactoryList<TItem>(IEnumerable<TItem> items, Func<TItem, string> text) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [H.Ul [ H.Each(items, item => FactoryRow(text(item))) ]];
}

public sealed partial class FactoryGreeting : IHtmlTemplate<string>
{
    public IEnumerable<HtmlElement> Build(string context) => [H.P [ "Hello ", context ]];
}

public sealed partial class FactoryPage(string[] names) : IHtmlTemplate<string>
{
    public IEnumerable<HtmlElement> Build(string context) =>
    [
        FactoryCard("Names", level: 3) [ FactoryList(names, name => name.ToUpperInvariant()) ],
        FactoryList<int>([1, 2], number => $"#{number}"),
        T.FactoryGreeting(),
        Tests.Templates.FactoryRow("qualified"),
        H.Div [ names.Length > 1 ? FactoryCard("Many") : null ]
    ];
}

public sealed partial class FactoryPart : IAsyncHtmlTemplate
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) => [H.I [ await Task.FromResult("slow") ]];
}

public sealed partial class FactoryAsyncPage : IAsyncHtmlTemplate
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) => [H.Div [ await FactoryPart(), FactoryRow("sync") ]];
}

public class TemplateFactoryTests
{
    [Fact]
    public void Templates_create_templates_without_new()
    {
        Assert.Equal(
            "<section data-level=\"3\"><h2>Names</h2><ul><li>ANN</li><li>BOB</li></ul></section>"
            + "<ul><li>#1</li><li>#2</li></ul>"
            + "<p>Hello Ann</p>"
            + "<li>qualified</li>"
            + "<div><section data-level=\"2\"><h2>Many</h2></section></div>",
            FactoryPage(["Ann", "Bob"]).RenderToString("Ann"));
    }

    [Fact]
    public async Task An_async_template_awaits_a_template_created_without_new() =>
        Assert.Equal("<div><i>slow</i><li>sync</li></div>", await FactoryAsyncPage().RenderToStringAsync());

    [Fact]
    public void A_factory_takes_named_and_optional_arguments_like_the_constructor() =>
        Assert.Equal("<section data-level=\"2\"><h2>T</h2>n</section>", FactoryCard("T", note: "n").RenderToString());
}
