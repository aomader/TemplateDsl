namespace SampleApp;

public enum ButtonKind
{
    Primary,
    Secondary,
}

public sealed partial class Button(string label) : HtmlTemplate
{
    public ButtonKind Kind { get; init; } = ButtonKind.Primary;

    public override IEnumerable<HtmlElement> Build() =>
    [
        H.Div.Class(Kind == ButtonKind.Primary ? "btn btn-primary" : "btn btn-secondary") [
            label
        ]
    ];
}
