# TDSL006: Invalid format declaration

A format is declared with attributes, which the generator reads like data (see "Other formats" in the README). This
error means one of the declarations doesn't have the shape it needs. The message names the declaration and what is
missing, for example:

| Message | Fix |
|---------|-----|
| `'XmlNode' must derive from TemplateNode` | The base class of the nodes, marked `[TemplateFormat]`, derives from `TemplateNode`. |
| `'XmlNode' must name a class marked with [TextNode] as its text node` | The type in `[TemplateFormat(typeof(XmlText))]` is a node class marked `[TextNode]`. |
| `'XmlNode' has an invalid escape rule '...'` | An `[Escape]` rule has a name usable in code, followed by pairs of a single character and its replacement. |
| `'Id' must be a partial method without a body, in a top-level partial class` | Declare attributes as `[Attr(...)] public partial Item Id(string? value);` and let the generator implement them. |
| `'Id' must take one string, bool or value type that can format itself` | An attribute takes one parameter: a string, a `bool?`, an enum, or a value type such as `int?` or `DateOnly?`. |
| `'Attr' must take a string name and a string? value` | An `[AttrList]` method is `public partial Item Attr(string name, string? value);`. |
| `'Xml' must be a static class` | The `[TemplateTags]` class holding the factories is `static partial`. |

The HTML format in `src/TemplateDsl.Html` and the RSS and JSON formats in `samples/TemplateDsl.FormatSamples` are
complete examples.
