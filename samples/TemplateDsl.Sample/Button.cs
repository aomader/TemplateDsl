namespace SampleApp;

public enum ButtonKind
{
    Primary,
    Secondary,
}

public sealed partial class Button(string label) : IHtmlTemplate
{
    public ButtonKind Kind { get; init; } = ButtonKind.Primary;

    public IEnumerable<HtmlElement> Build() =>
    [
        Div.Class("btn", Kind == ButtonKind.Primary ? "btn-primary" : "btn-secondary") [
            label
        ]
    ];
}
