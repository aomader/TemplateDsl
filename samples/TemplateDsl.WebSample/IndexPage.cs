using System.Runtime.CompilerServices;
using TemplateDsl;
using TemplateDsl.Html;

namespace WebSample;

public static class Entries
{
    /// <summary>Stands in for a slow data source, such as a database query.</summary>
    public static async IAsyncEnumerable<string> StreamAsync(int count, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var number = 1; number <= count; number++)
        {
            await Task.Delay(500, cancellationToken);
            yield return $"Entry {number} of {count}, rendered at {DateTime.Now:HH:mm:ss.fff}";
        }
    }

    /// <summary>Stands in for another slow query.</summary>
    public static async Task<string> LoadServerStatusAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(1000, cancellationToken);
        return $"All systems running, checked at {DateTime.Now:HH:mm:ss.fff}";
    }
}

/// <summary>
/// An async component: it loads its own data, so the page using it doesn't have to know how.
/// Using it takes an await, like any other async call: <c>await new ServerStatus()</c>.
/// </summary>
public sealed partial class ServerStatus : IAsyncHtmlTemplate
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) =>
    [
        H.Div.Class("status") [ await Entries.LoadServerStatusAsync(ct) ]
    ];
}

/// <summary>Only the content: the list is sent as it arrives, and the page doesn't know its layout.</summary>
public sealed partial class IndexPage(IAsyncEnumerable<string> items) : Page
{
    public override string Title => "Streaming with TemplateDsl";

    public override IHtmlContent? Head => new IndexHead();

    public override async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) =>
    [
        H.P [ "This text arrived first. The status below takes a second, and then the entries appear one by one." ],
        await new ServerStatus(),
        H.Ul [
            await H.EachAsync(items, item => H.Li [ item ])
        ],
        H.P [ "All entries have been sent." ],
        H.P [ "dont do that" ]
    ];
}

public sealed partial class IndexHead : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [H.Meta.Name("description").Content("A streaming sample")];
}
