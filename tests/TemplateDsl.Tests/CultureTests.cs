using System.Globalization;
using Xunit;

namespace Tests;

public sealed partial class ForPrograms(double real, DateOnly date, DateTime moment, DateTimeOffset offset, TimeOnly time, int? number) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        H.Div.Style($"width:{real}%").Attr("data-a", real),
        H.Input.Value(real).Min(date).Max(moment),
        H.Time.DateTime(offset),
        H.Time.DateTime($"{time}"),
        H.Td.ColSpan(number),
        H.A.Href($"/x?v={real,6}")
    ];
}

public sealed partial class ForPeople(double real, int number) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        H.Img.Alt(real),
        H.Div.Title($"{real} of {number,3}").Attr("aria-label", real) [ real, $" {real}" ]
    ];
}

public sealed partial class AwaitedInvariant(Task<double> real, Task<double?> maybe) : IAsyncHtmlTemplate
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken ct) =>
        [H.Div.Id($"{await real}").Attr("data-a", await maybe) [ await real ]];
}

public sealed partial class TrueOrFalse(bool yes, bool? maybe) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        H.Div.Draggable(true).Spellcheck(false).ContentEditable(null),
        H.Div.Draggable(yes).Spellcheck(maybe),
        H.Div.Attr("contenteditable", "plaintext-only")
    ];
}

public sealed partial class Numbers(int count, double level) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        H.Td.ColSpan(2).RowSpan(count > 0 ? count : null),
        H.Meter.Value(level).Max(1),
        H.Td.Attr("colspan", "{{columns}}")
    ];
}

public class CultureTests
{
    private static void InCulture(string culture, Action test)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);

        try
        {
            test();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("ar-SA")]
    public void What_programs_read_is_the_same_in_every_culture(string culture) =>
        InCulture(culture, () => Assert.Equal(
            "<div style=\"width:-1.5%\" data-a=\"-1.5\"></div>"
                + "<input value=\"-1.5\" min=\"2026-11-01\" max=\"2026-11-01T13:14:15\">"
                + "<time datetime=\"2026-11-01T13:14:15+01:00\"></time>"
                + "<time datetime=\"13:14:15\"></time>"
                + "<td colspan=\"-7\"></td>"
                + "<a href=\"/x?v=  -1.5\"></a>",
            new ForPrograms(
                -1.5,
                new DateOnly(2026, 11, 1),
                new DateTime(2026, 11, 1, 13, 14, 15),
                new DateTimeOffset(2026, 11, 1, 13, 14, 15, TimeSpan.FromHours(1)),
                new TimeOnly(13, 14, 15),
                -7).RenderToString()));

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    [InlineData("sv-SE")]
    public void What_people_read_uses_their_culture(string culture) =>
        InCulture(culture, () =>
        {
            var real = 1234.5;
            var number = 5;

            Assert.Equal(
                $"<img alt=\"{real}\"><div title=\"{real} of {number,3}\" aria-label=\"{real}\">{real} {real}</div>",
                new ForPeople(real, number).RenderToString());
        });

    [Fact]
    public async Task Awaited_values_follow_the_same_rule()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

        try
        {
            Assert.Equal(
                "<div id=\"1.5\" data-a=\"2.5\">1,5</div>",
                await new AwaitedInvariant(Task.FromResult(1.5), Task.FromResult<double?>(2.5)).RenderToStringAsync());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(true, null, "<div draggable=\"true\"></div>")]
    [InlineData(false, true, "<div draggable=\"false\" spellcheck=\"true\"></div>")]
    public void A_true_or_false_attribute_writes_the_word(bool yes, bool? maybe, string second) =>
        Assert.Equal(
            "<div draggable=\"true\" spellcheck=\"false\"></div>" + second + "<div contenteditable=\"plaintext-only\"></div>",
            new TrueOrFalse(yes, maybe).RenderToString());

    [Fact]
    public void Typed_attributes_take_their_type_and_attr_takes_anything_else() =>
        InCulture("de-DE", () => Assert.Equal(
            "<td colspan=\"2\" rowspan=\"3\"></td><meter value=\"0.5\" max=\"1\"></meter><td colspan=\"{{columns}}\"></td>",
            new Numbers(3, 0.5).RenderToString()));
}
