using TemplateDsl.Html;

Console.Write(new Page("you", ["one", "two"]).RenderToString());
Console.Write(await new AsyncPage().RenderToStringAsync());
Console.Write(new ContextParent().RenderToString("ctx"));

public sealed partial class Item(string label) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() => [H.Div[label]];
}

public sealed partial class Page(string name, string[] items) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() =>
    [
        H.Div.Class("card")
        [
            $"Hi <{name}>",
            H.Each(items, item => new Item(item))
        ]
    ];
}

public sealed partial class SlowPart : AsyncHtmlTemplate
{
    public override async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) => [H.P [ await Task.FromResult("slow") ]];
}

public sealed partial class AsyncPage : AsyncHtmlTemplate
{
    public override async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) => [H.Div [ await new SlowPart() ]];
}

public sealed partial class ContextChild : HtmlTemplate<string>
{
    public override IEnumerable<HtmlElement> Build(string context) => [H.I[context]];
}

public sealed partial class ContextParent : HtmlTemplate<string>
{
    public override IEnumerable<HtmlElement> Build(string context) => [new ContextChild()];
}
