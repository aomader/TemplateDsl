namespace SampleApp;

// Templates can be structs too. A small one, such as a badge or a row, is then rendered by its parent without
// allocating it. A record struct gets its properties from the primary constructor.

public sealed record Product(string Name, decimal Price, int Stock);

/// <summary>A small component as a readonly record struct.</summary>
public readonly partial record struct Badge(string Text, bool Highlighted = false) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        H.Span.Class(Highlighted ? "badge badge-highlight" : "badge") [ Text ]
    ];
}

/// <summary>A row, created once per item by the list it is in.</summary>
public readonly partial record struct ProductRow(Product Product) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        H.Tr [
            H.Td [ Product.Name ],
            H.Td [ $"{Product.Price:F2}" ],
            H.Td [ Product.Stock > 0 ? new Badge($"{Product.Stock} left") : new Badge("sold out", Highlighted: true) ]
        ]
    ];
}

/// <summary>A struct that takes children, which it places in a box with a heading.</summary>
public readonly partial record struct Panel(string Heading) : IHtmlTemplate, IAcceptsChildren
{
    public IEnumerable<HtmlElement> Build() =>
    [
        H.Section.Class("panel") [
            H.H2 [ Heading ],
            Children
        ]
    ];
}

/// <summary>
/// A struct page. C# doesn't let a lambda in a struct use its members, such as <c>Products</c> or <c>Title</c>, so
/// where a loop needs them, it is a <c>foreach</c> instead of <c>H.Each</c>. A lambda that only uses its own
/// parameter, as below, is fine.
/// </summary>
public readonly partial record struct ProductPage(string Title, Product[] Products) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build()
    {
        yield return H.H1 [ Title ];

        yield return new Panel("All products") [
            H.Table [ H.Each(Products, product => new ProductRow(product)) ]
        ];

        foreach (var product in Products)
        {
            if (product.Stock == 0)
            {
                yield return new Panel($"{product.Name} in {Title}") [ new Badge("back soon") ];
            }
        }
    }
}
