using System.Reflection;
using FormatSamples;
using Xunit;

namespace Tests;

/// <summary>
/// The format attributes are data, read by the generator from metadata. These read them the way any other tool
/// would, from the HTML and JSON formats, which also checks that those declare what the documentation says.
/// </summary>
public class FormatDeclarationTests
{
    private static readonly Type Tag = typeof(Nodes.HtmlTag<>);

    [Fact]
    public void The_html_tags_belong_to_the_html_format()
    {
        Assert.Equal(typeof(HtmlElement), typeof(H).GetCustomAttribute<TemplateTagsAttribute>()!.Format);
    }

    [Fact]
    public void The_html_format_escapes_text_and_attributes()
    {
        var format = typeof(HtmlElement).GetCustomAttribute<TemplateFormatAttribute>()!;
        var rules = typeof(HtmlElement).GetCustomAttributes<EscapeAttribute>().ToDictionary(rule => rule.Context, rule => rule.Replacements);

        Assert.Equal((typeof(Nodes.Text), true, ValueCulture.Current), (format.TextNode, format.FormatsValues, format.TextCulture));
        Assert.Equal(["&", "&amp;", "<", "&lt;", ">", "&gt;"], rules["text"]);
        Assert.Equal(["&", "&amp;", "<", "&lt;", ">", "&gt;", "\"", "&quot;", "'", "&#39;"], rules["attribute"]);
    }

    [Fact]
    public void Every_element_has_a_list_for_any_attribute_and_one_for_data_attributes()
    {
        var any = Tag.GetMethod("Attr", [typeof(string), typeof(string)])!.GetCustomAttribute<AttrListAttribute>()!;
        var data = Tag.GetMethod("Data", [typeof(string), typeof(string)])!.GetCustomAttribute<AttrListAttribute>()!;

        Assert.Equal((null, false, false), (any.NamePrefix, any.TrueFalse, any.LowercaseNames));
        Assert.Equal(("data-", true, true), (data.NamePrefix, data.TrueFalse, data.LowercaseNames));
        Assert.Equal((" ", "=\"", "\"", "attribute"), (data.Before, data.BetweenNameAndValue, data.After, data.Escape));
        Assert.Null(data.CurrentCultureNames);
    }

    [Fact]
    public void Text_nodes_say_how_they_are_written()
    {
        var html = typeof(Nodes.Text).GetCustomAttribute<TextNodeAttribute>()!;
        var json = typeof(JsonString).GetCustomAttribute<TextNodeAttribute>()!;

        Assert.Equal(("", "", "text"), (html.Prefix, html.Suffix, html.Escape));
        Assert.Equal(("\"", "\"", "string"), (json.Prefix, json.Suffix, json.Escape));
    }

    [Fact]
    public void Nodes_say_what_surrounds_their_children()
    {
        var div = typeof(Nodes.Div).GetCustomAttribute<NodeAttribute>()!;
        var array = typeof(JsonArray).GetCustomAttribute<NodeAttribute>()!;

        Assert.Equal(("<div", ">", "</div>", false, null), (div.Start, div.Body, div.End, div.IsVoid, div.Separator));
        Assert.Equal(("[", "]", ","), (array.Start, array.End, array.Separator));
    }

    [Fact]
    public void Attribute_methods_say_how_their_values_are_written()
    {
        var id = Attribute(Tag, "Id");
        var title = Attribute(Tag, "Title");
        var @class = Attribute(Tag, "Class");
        var min = Attribute(typeof(Nodes.NumberInput), "Min");
        var step = Attribute(typeof(Nodes.NumberInput), "Step");
        var month = Attribute(typeof(Nodes.MonthInput), "Min");

        Assert.Equal((" id=\"", "\"", "attribute"), (id.Prefix, id.Suffix, id.Escape));
        Assert.Equal(ValueCulture.Current, title.Culture);
        Assert.Equal(ValueCulture.Invariant, id.Culture);
        Assert.True(@class.TextOnly);
        Assert.False(id.TextOnly);
        Assert.Equal((true, false), (min.AnyNumber, min.AlsoText));
        Assert.Equal((true, true), (step.AnyNumber, step.AlsoText));
        Assert.Equal("yyyy-MM", month.Format);
    }

    [Fact]
    public void The_attribute_list_says_how_each_attribute_is_written()
    {
        var list = Tag.GetMethod("Attr", [typeof(string), typeof(string)])!.GetCustomAttribute<AttrListAttribute>()!;

        Assert.Equal((" ", "=\"", "\"", "attribute"), (list.Before, list.BetweenNameAndValue, list.After, list.Escape));

        // The ARIA attributes for people of WAI-ARIA 1.2, such as aria-label, have methods of their own. These are of 1.3.
        Assert.Equal(["aria-braillelabel", "aria-brailleroledescription", "aria-colindextext", "aria-rowindextext"], list.CurrentCultureNames!);
    }

    [Fact]
    public void Aria_attributes_have_methods_and_a_list_for_the_others()
    {
        var aria = Tag.GetMethod("Aria", [typeof(string), typeof(string)])!.GetCustomAttribute<AttrListAttribute>()!;

        Assert.Equal(("aria-", true, true), (aria.NamePrefix, aria.TrueFalse, aria.LowercaseNames));
        Assert.Contains("aria-braillelabel", aria.CurrentCultureNames!);
        Assert.Equal(ValueCulture.Current, Attribute(Tag, "AriaLabel").Culture);
        Assert.Equal(ValueCulture.Invariant, Attribute(Tag, "AriaValueNow").Culture);
    }

    private static AttrAttribute Attribute(Type type, string method) =>
        type.GetMethods().Single(candidate => candidate.Name == method && candidate.GetCustomAttribute<AttrAttribute>() is not null)
            .GetCustomAttribute<AttrAttribute>()!;
}
