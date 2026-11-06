using System.Globalization;
using System.Text;
using Xunit;

namespace Tests;

public sealed partial class DynamicTemplate(string name, string? optionalId, int count, decimal price) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        name,
        H.Div.Id($"Oli{name}").Class(optionalId)
        [
            "a&b ",
            name,
            H.Text(name),
            $" a&b {{braces}} {name} {count:D3} {count,3} ",
            price.ToString("F2", CultureInfo.InvariantCulture),
            " ",
            Shout(name),
            H.Div.Id(optionalId),
            H.Div.Class($"x\"&y-{count}")
        ]
    ];

    private static string Shout(string value) => value.ToUpperInvariant() + "!";
}

public sealed partial class OliTemplate(string model) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [H.Div.Id($"Oli{model}")];
}

public sealed partial class InterpolationFormsTemplate(int model) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        $"regular {{x}} }}{{ \t \\ {model}",
        @$"verbatim {{x}} ""q"" \n {model}",
        $@"verbatim2 {{x}} {model}",
        $"""raw "q" {model}""",
        $$"""double-dollar {x} {{model}} {{{model}}}""",
        $"""
            multi
              line {model}
            """,
        $$"""
            multi {x}
              line {{model}}
            """,
    ];
}

public class DynamicTests
{
    [Fact]
    public void Generated_output_matches_what_csharp_produces_for_all_interpolation_forms()
    {
        const int Model = 7;

        // The same strings as in the template, so C# itself says what each form means.
        var expected = string.Concat(
            $"regular {{x}} }}{{ \t \\ {Model}",
            @$"verbatim {{x}} ""q"" \n {Model}",
            $@"verbatim2 {{x}} {Model}",
            $"""raw "q" {Model}""",
            $$"""double-dollar {x} {{Model}} {{{Model}}}""",
            $"""
                multi
                  line {Model}
                """,
            $$"""
                multi {x}
                  line {{Model}}
                """);

        Assert.Equal(expected, new InterpolationFormsTemplate(7).RenderToString());
    }

    [Fact]
    public void Interpolated_attribute_from_the_request_works()
    {
        Assert.Equal("<div id=\"Oli123\"></div>", new OliTemplate("123").RenderToString());
    }

    [Fact]
    public void Text_and_attributes_use_model_values()
    {
        var html = new DynamicTemplate("ann", "the-id", 7, 1.5m).RenderToString();

        Assert.Equal(
            "ann"
            + "<div id=\"Oliann\" class=\"the-id\">"
            + "a&amp;b annann a&amp;b {braces} ann 007   7 1.50 ANN!"
            + "<div id=\"the-id\"></div>"
            + "<div class=\"x&quot;&amp;y-7\"></div>"
            + "</div>",
            html);
    }

    [Fact]
    public void Null_attribute_value_leaves_the_attribute_out()
    {
        var html = new DynamicTemplate("a", null, 1, 1m).RenderToString();

        Assert.Contains("<div id=\"Olia\">", html);
        Assert.Contains("<div></div>", html);
        Assert.DoesNotContain("class=\"\"", html);
    }

    [Fact]
    public void Dynamic_values_are_escaped()
    {
        Assert.Equal("<div id=\"Oli&quot;&lt;&amp;&gt;\"></div>", new OliTemplate("\"<&>").RenderToString());
    }

    [Fact]
    public void Non_ascii_dynamic_values_are_written_as_utf8()
    {
        Assert.Equal("<div id=\"Oliäö€😀\"></div>", new OliTemplate("äö€😀").RenderToString());
    }

    [Fact]
    public async Task Dynamic_values_are_written_to_streams()
    {
        using var stream = new MemoryStream();

        await new OliTemplate("äö").RenderToAsync(stream);

        Assert.Equal("<div id=\"Oliäö\"></div>", Encoding.UTF8.GetString(stream.ToArray()));
    }

}
