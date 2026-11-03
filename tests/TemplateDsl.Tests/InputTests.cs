using System.Globalization;
using Xunit;

namespace Tests;

public sealed partial class TypedInputs(decimal price, int count, double? level, DateOnly day, TimeOnly time, DateTime moment) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() =>
    [
        H.InputNumber.Name("price").Min(0).Max(price).Step("any").Value(price),
        H.InputRange.Min(0.5).Max(count).Step(0.25).Value(level),
        H.InputDate.Value(day).Min(day).Step(7),
        H.InputMonth.Value(day),
        H.InputTime.Value(time),
        H.InputDatetimeLocal.Value(moment),
        H.InputEmail.Name("mail").Placeholder("you@example.com").Multiple().Required(),
        H.InputCheckbox.Name("ok").Value(count).Checked(count > 0),
        H.InputHidden.Name("id").Value(count).Hidden(),
        H.InputSubmit.Value(1.5),
        H.Input.Name("q")
    ];
}

public sealed partial class InputOfType(InputType type, InputType? maybe) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build() =>
    [
        H.Input.Type(InputType.DatetimeLocal),
        H.Input.Type(type).Name("a"),
        H.Input.Type(maybe),
        H.Input.Type(null),
        H.Input.Type(type == InputType.Email ? InputType.Url : InputType.Tel),
        H.Input.Attr("type", "x-custom")
    ];
}

public class InputTests
{
    [Fact]
    public void Each_input_type_takes_the_values_it_allows()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

        try
        {
            Assert.Equal(
                "<input type=\"number\" name=\"price\" min=\"0\" max=\"9.95\" step=\"any\" value=\"9.95\">"
                    + "<input type=\"range\" min=\"0.5\" max=\"3\" step=\"0.25\" value=\"0.5\">"
                    + "<input type=\"date\" value=\"2026-11-02\" min=\"2026-11-02\" step=\"7\">"
                    + "<input type=\"month\" value=\"2026-11\">"
                    + "<input type=\"time\" value=\"13:30:00\">"
                    + "<input type=\"datetime-local\" value=\"2026-11-02T13:30:00\">"
                    + "<input type=\"email\" name=\"mail\" placeholder=\"you@example.com\" multiple required>"
                    + "<input type=\"checkbox\" name=\"ok\" value=\"3\" checked>"
                    + "<input type=\"hidden\" name=\"id\" value=\"3\" hidden>"
                    + "<input type=\"submit\" value=\"1,5\">"
                    + "<input name=\"q\">",
                new TypedInputs(9.95m, 3, 0.5, new DateOnly(2026, 11, 2), new TimeOnly(13, 30), new DateTime(2026, 11, 2, 13, 30, 0)).RenderToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(InputType.Week, null, "<input type=\"week\" name=\"a\"><input><input><input type=\"tel\">")]
    [InlineData(InputType.Email, InputType.Checkbox, "<input type=\"email\" name=\"a\"><input type=\"checkbox\"><input><input type=\"url\">")]
    [InlineData((InputType)99, null, "<input name=\"a\"><input><input><input type=\"tel\">")]
    public void The_plain_input_takes_its_type_as_an_enum(InputType type, InputType? maybe, string middle) =>
        Assert.Equal(
            "<input type=\"datetime-local\">" + middle + "<input type=\"x-custom\">",
            new InputOfType(type, maybe).RenderToString());
}
