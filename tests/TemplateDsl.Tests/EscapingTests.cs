using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using Xunit;

namespace Tests;

public sealed partial class EscapedEverywhere(string value, string? optional, int number, bool flag) : IHtmlTemplate
{
    private string Helper() => value + "<helper>";

    public IEnumerable<HtmlElement> Build() =>
    [
        value,
        H.Text(value),
        H.Text(value),
        H.Div.Id(value).Class(optional)
        [
            $"lit& {value} {number:D2} {Helper()}",
            flag ? value : "<const>",
            value + "<concat>",
            H.Each(new[] { value }, item => H.Div.Class($"c{item}")[item]),
            new Chip(value) { Css = value },
            new Panel(value)[value]
        ]
    ];
}

public sealed partial class RawTemplate(string trusted, string untrusted) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        H.Raw("<b>const & raw</b>"),
        H.Div[H.Raw(trusted), untrusted, H.Raw($"<i>{trusted}</i>"), H.Raw(null)]
    ];
}

public sealed partial class AsyncEscaped(string value) : IAsyncHtmlTemplate
{
    private static async IAsyncEnumerable<string> Stream(string value, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Delay(0, ct);
        yield return value;
    }

    public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) =>
    [
        await Task.FromResult(value),
        H.Div.Id(await Task.FromResult(value))[$"{await Task.FromResult(value)}"],
        await H.EachAsync(Stream(value, ct), item => H.Div[item])
    ];
}

/// <summary>A number type whose text has characters that need escaping: Complex is written as &lt;1; 2&gt;.</summary>
public sealed partial class AnyNumberAttributes(Complex number) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        H.InputNumber.Min(number).Max(number).Value(number),
        H.InputRange.Value(number)
    ];
}

// A format whose escaping replaces a character that isn't ASCII, so its formatted values can't be checked as bytes,
// and a digit, so even plain numbers need escaping.
[TemplateFormat(typeof(EuroText), FormatsValues = true, TextCulture = ValueCulture.Invariant)]
[Escape("text", "€", "EUR", "<", "&lt;", "5", "five")]
public abstract partial class EuroNode : TemplateNode;

[TextNode(Escape = "text")]
public sealed partial class EuroText : EuroNode;

[Node(Start = "<p", Body = ">", End = "</p>")]
public sealed partial class EuroParagraph : EuroNode
{
    /// <summary>Declares no escaping, so its values are written as they format.</summary>
    [Attr(" code=", "")]
    public partial EuroParagraph Code(Bracketed? value);
}

[TemplateTags(typeof(EuroNode))]
public static partial class Euro
{
    public static EuroParagraph P => new();
}

public interface IEuroTemplate : ITemplate<EuroNode>;

public sealed partial class EuroPrice(decimal price) : IEuroTemplate
{
    public IEnumerable<EuroNode> Build() => [Euro.P [ $"{price:0.00 €}" ], Euro.P [ price ]];
}

public sealed partial class EuroCode(Bracketed code) : IEuroTemplate
{
    public IEnumerable<EuroNode> Build() => [Euro.P.Code(code) [ "x" ]];
}

/// <summary>A value that formats itself only as characters, not as UTF-8, with characters that need escaping.</summary>
public readonly struct Bracketed(int value) : ISpanFormattable
{
    public string ToString(string? format, IFormatProvider? formatProvider) => $"<{value}&>";

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider) =>
        destination.TryWrite($"<{value}&>", out charsWritten);
}

public sealed partial class BracketedText(Bracketed value) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [H.P.Id(value) [ $"{value}" ]];
}

public class EscapingTests
{
    private const string Attack = "\"><script>alert('x')</script>&";

    private const string InText = "\"&gt;&lt;script&gt;alert('x')&lt;/script&gt;&amp;";

    private const string InAttribute = "&quot;&gt;&lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt;&amp;";

    [Fact]
    public void Attack_string_is_neutralized_in_every_position()
    {
        var html = new EscapedEverywhere(Attack, Attack, 7, flag: true).RenderToString();

        Assert.DoesNotContain("<script", html);
        Assert.Contains($"<div id=\"{InAttribute}\" class=\"{InAttribute}\">", html);
        Assert.StartsWith($"{InText}{InText}{InText}<div", html);
        Assert.Contains($"lit&amp; {InText} 07 {InText}&lt;helper&gt;", html);
        Assert.Contains($"{InText}&lt;concat&gt;", html);
        Assert.Contains($"<div class=\"c{InAttribute}\">{InText}</div>", html);
    }

    [Fact]
    public void Constant_branch_of_a_ternary_is_escaped_too()
    {
        Assert.Contains("&lt;const&gt;", new EscapedEverywhere("v", null, 1, flag: false).RenderToString());
    }

    [Fact]
    public async Task Async_path_escapes_the_same_way()
    {
        var template = new EscapedEverywhere(Attack, Attack, 7, flag: true);
        using var stream = new MemoryStream();

        await template.RenderToAsync(stream);

        Assert.Equal(template.RenderToString(), Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public async Task Async_templates_escape_values()
    {
        Assert.Equal(
            $"{InText}<div id=\"{InAttribute}\">{InText}</div><div>{InText}</div>",
            await new AsyncEscaped(Attack).RenderToStringAsync());
    }

    [Fact]
    public void Raw_writes_trusted_markup_and_only_that()
    {
        Assert.Equal(
            "<b>const & raw</b><div><u>t</u>&lt;u&gt;<i><u>t</u></i></div>",
            new RawTemplate("<u>t</u>", "<u>").RenderToString());
    }

    [Fact]
    public void Values_of_any_number_type_are_escaped_in_number_attributes()
    {
        var html = new AnyNumberAttributes(new Complex(1, 2)).RenderToString();

        Assert.DoesNotContain("<1", html);
        Assert.Contains("<input type=\"number\" min=\"&lt;1; 2&gt;\" max=\"&lt;1; 2&gt;\" value=\"&lt;1; 2&gt;\">", html);
    }

    [Fact]
    public void Values_that_only_format_as_characters_are_escaped()
    {
        Assert.Equal("<p id=\"&lt;1&amp;&gt;\">&lt;1&amp;&gt;</p>", new BracketedText(new Bracketed(1)).RenderToString());
    }

    [Fact]
    public void An_attribute_that_declares_no_escaping_writes_values_as_they_format()
    {
        Assert.Equal("<p code=<7&>>x</p>", new EuroCode(new Bracketed(7)).RenderToString());
    }

    [Fact]
    public void Formatted_values_are_escaped_by_rules_with_characters_that_are_not_ascii()
    {
        Assert.Equal("<p>1.five0 EUR</p><p>1.five</p>", new EuroPrice(1.5m).RenderToString());
    }

}

public class NonAsciiAndBuildTests
{
    [Fact]
    public void Non_ascii_values_are_not_changed()
    {
        Assert.StartsWith("äö€😀äö€😀", new EscapedEverywhere("äö€😀", null, 1, true).RenderToString());
    }

    [Fact]
    public void Build_can_not_be_executed()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new Panel("t").Build().ToList());

        Assert.Contains("generated render methods", error.Message);
    }
}
