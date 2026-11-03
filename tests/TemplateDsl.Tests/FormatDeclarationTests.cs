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
    private static readonly Type Tag = typeof(Elements.HtmlTag<>);

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

        Assert.Equal((typeof(Elements.Text), true, ValueCulture.Current), (format.TextNode, format.FormatsValues, format.TextCulture));
        Assert.Equal(["&", "&amp;", "<", "&lt;", ">", "&gt;"], rules["text"]);
        Assert.Equal(["&", "&amp;", "<", "&lt;", ">", "&gt;", "\"", "&quot;", "'", "&#39;"], rules["attribute"]);
    }

    [Fact]
    public void Text_nodes_say_how_they_are_written()
    {
        var html = typeof(Elements.Text).GetCustomAttribute<TextNodeAttribute>()!;
        var json = typeof(JsonString).GetCustomAttribute<TextNodeAttribute>()!;

        Assert.Equal(("", "", "text"), (html.Prefix, html.Suffix, html.Escape));
        Assert.Equal(("\"", "\"", "string"), (json.Prefix, json.Suffix, json.Escape));
    }

    [Fact]
    public void Nodes_say_what_surrounds_their_children()
    {
        var div = typeof(Elements.Div).GetCustomAttribute<NodeAttribute>()!;
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
        var min = Attribute(typeof(Elements.NumberInput), "Min");
        var step = Attribute(typeof(Elements.NumberInput), "Step");
        var month = Attribute(typeof(Elements.MonthInput), "Min");

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
        Assert.Contains("aria-label", list.CurrentCultureNames!);
    }

    private static AttrAttribute Attribute(Type type, string method) =>
        type.GetMethods().Single(candidate => candidate.Name == method && candidate.GetCustomAttribute<AttrAttribute>() is not null)
            .GetCustomAttribute<AttrAttribute>()!;
}
