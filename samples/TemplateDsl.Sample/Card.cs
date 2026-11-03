namespace SampleApp;

/// <summary>Children are only given inside another template's Build method.</summary>
public sealed partial class SettingsPage : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() =>
    [
        new Card("Settings") [
            "Some content & more",
            new Button("Save")
        ]
    ];
}

/// <summary>Implementing HtmlTemplateWithChildren is what allows <c>new Card("Title") [ ... ]</c>.</summary>
public sealed partial class Card(string title) : HtmlTemplateWithChildren
{
    public override IEnumerable<HtmlElement> Build() =>
    [
        H.Div.Class("card") [
            H.Div.Class("card-title") [ title ],
            H.Div.Class("card-body") [ Children ]
        ]
    ];
}
