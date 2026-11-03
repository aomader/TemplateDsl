using System.Runtime.CompilerServices;

namespace SampleApp;

public sealed record Order(int Id, string Product);

/// <summary>Stands in for a database, with delays to make the streaming visible.</summary>
public sealed class OrderRepository
{
    /// <summary>How long the pretend database takes to answer.</summary>
    private readonly TimeSpan _latency = TimeSpan.FromMilliseconds(500);

    public async Task<string> GetUserNameAsync(int userId, CancellationToken cancellationToken)
    {
        await Task.Delay(_latency, cancellationToken);
        return $"User {userId}";
    }

    public async IAsyncEnumerable<Order> StreamOrders(int userId, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var product in new[] { "Keyboard", "Mouse", "Monitor" })
        {
            await Task.Delay(_latency, cancellationToken);
            yield return new Order(product.Length + userId, product);
        }
    }
}

public sealed partial class OrderRow(Order order) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() => [ H.Div.Id($"order-{order.Id}").Class("order") [ order.Product ] ];
}

public sealed partial class OrdersPage(OrderRepository repository, int userId) : AsyncHtmlTemplate
{
    public override async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) =>
    [
        H.Html [
            H.Body [
                H.Div.Class("header") [ "Sent right away, before anything is awaited" ],
                H.Div.Class("user") [ await repository.GetUserNameAsync(userId, ct) ],
                new Card("Orders") [
                    await H.EachAsync(repository.StreamOrders(userId, ct), order => new OrderRow(order))
                ],
                new Button("Done")
            ]
        ],
    ];
}
