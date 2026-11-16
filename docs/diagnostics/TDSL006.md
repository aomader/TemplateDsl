# TDSL006: Invalid format declaration

A format is declared with attributes, which the generator reads like data (see "Other formats" in the README). This
error means one of the declarations doesn't have the shape it needs. The message names the declaration and what is
missing, for example:

| Message | Fix |
|---------|-----|
| `'XmlNode' must derive from TemplateNode` | The base class of the nodes, marked `[TemplateFormat]`, derives from `TemplateNode`. |
| `'XmlNode' must name a class marked with [TextNode] as its text node` | The type in `[TemplateFormat(typeof(XmlText))]` is a node class marked `[TextNode]`. |
| `'XmlNode' has an invalid escape rule '...'` | An `[Escape]` rule has a name usable in code, followed by pairs of a single character and its replacement. |
| `'Id' must be a partial method without a body, in a top-level partial class` | Declare attributes as `public partial Item Id([Attr(...)] string? value);` and let the generator implement them. |
| `'Id' must take a string, bool or value type that can format itself, such as int?, for each attribute` | Each parameter marked `[Attr]` takes a string, a `bool?`, an enum, or a value type such as `int?` or `DateOnly?`. |
| `'Item.Pair' must say which attribute each of its parameters writes, with [Attr]` | Mark every parameter of an attribute method or a factory with `[Attr]`, as in `Prop([Attr("\"", "\":")] string name)`. The text of a text node, as in `Text(string? value)`, needs none. |
| `'Item.Pair' writes several attributes, so a parameter can only default to null` | An argument that is left out writes nothing, so an optional parameter defaults to `null`. |
| `'Item.Names' takes a list of names, so it can write no other attribute` | A `params AttributeToken[]` parameter is the only one of its method, which takes as many names as it's given. |
| `'Xml.Make' can't take a list of names, which only a method of the node can` | Declare the list as a method of the node, such as `Class`, and leave it out of the factory. |
| `'Htmx.HxGet' must extend a node of a format` | An extension method that writes attributes takes a node, such as `this Form form`, or a type parameter constrained to one, such as `this TElement element` with `where TElement : HtmlTag<TElement>`. |
| `'Xml.Words' creates a text node, so it takes one string, its text, which needs no [Attr]` | A factory of a node marked `[TextNode]` is `public static XmlText Words(string? value) => new();`: its argument is the text, which has no attributes. |
| `'Attr' must take a string name and a string? value` | An `[AttrList]` method is `public partial Item Attr(string name, string? value);`. |
| `'Xml' must be a static class` | The `[TemplateTags]` class holding the factories is `static partial`. |

The HTML format in `src/TemplateDsl.Html` and the RSS and JSON formats in `samples/TemplateDsl.FormatSamples` are
complete examples.
