using Xunit;
using static TemplateDsl.Html.Elements;

namespace Tests;

/// <summary>
/// The elements imported with <c>using static</c>, as the samples do: no prefix, Each and Text included. A member of
/// the template wins over an element of the same name, which is then written with its class.
/// </summary>
public sealed partial class ImportedElementsPage(string title, IEnumerable<string> items) : IHtmlTemplate
{
    public string Title => title;

    public IEnumerable<HtmlElement> Build() =>
    [
        Html [
            Head [ Elements.Title [ Title ] ],
            Body.Class("page") [
                H1 [ Title ],
                Ul [ Each(items, item => Li [ item ]) ],
                Text("done"),
                Raw("<hr>")
            ]
        ]
    ];
}

public class ElementImportTests
{
    [Fact]
    public void Elements_can_be_imported_and_written_without_a_prefix() =>
        Assert.Equal(
            "<html><head><title>Items</title></head><body class=\"page\"><h1>Items</h1><ul><li>a</li><li>b</li></ul>done<hr></body></html>",
            new ImportedElementsPage("Items", ["a", "b"]).RenderToString());
}
