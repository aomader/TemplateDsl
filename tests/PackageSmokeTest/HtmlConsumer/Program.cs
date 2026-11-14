using TemplateDsl.Html;
using static TemplateDsl.Html.Elements;
using static HtmlConsumer.Templates;

Console.Write(Page("you", ["one", "two"]).RenderToString());
Console.Write(await AsyncPage().RenderToStringAsync());
Console.Write(ContextParent().RenderToString("ctx"));

public sealed partial class Item(string label) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [Div[label]];
}

public sealed partial class Page(string name, string[] items) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        Div.Class("card")
        [
            $"Hi <{name}>",
            Each(items, item => Item(item))
        ]
    ];
}

public sealed partial class SlowPart : IAsyncHtmlTemplate
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) => [P [ await Task.FromResult("slow") ]];
}

public sealed partial class AsyncPage : IAsyncHtmlTemplate
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) => [Div [ await SlowPart() ]];
}

public sealed partial class ContextChild : IHtmlTemplate<string>
{
    public IEnumerable<HtmlElement> Build(string context) => [I[context]];
}

public sealed partial class ContextParent : IHtmlTemplate<string>
{
    public IEnumerable<HtmlElement> Build(string context) => [ContextChild()];
}
