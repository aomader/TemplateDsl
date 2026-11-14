using TemplateDsl.Html;
using static TemplateDsl.Html.Elements;

Console.Write(new Page("you", ["one", "two"]).RenderToString());
Console.Write(await new AsyncPage().RenderToStringAsync());
Console.Write(new ContextParent().RenderToString("ctx"));

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
            Each(items, item => new Item(item))
        ]
    ];
}

public sealed partial class SlowPart : IAsyncHtmlTemplate
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) => [P [ await Task.FromResult("slow") ]];
}

public sealed partial class AsyncPage : IAsyncHtmlTemplate
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) => [Div [ await new SlowPart() ]];
}

public sealed partial class ContextChild : IHtmlTemplate<string>
{
    public IEnumerable<HtmlElement> Build(string context) => [I[context]];
}

public sealed partial class ContextParent : IHtmlTemplate<string>
{
    public IEnumerable<HtmlElement> Build(string context) => [new ContextChild()];
}
