using TemplateDsl;

namespace FormatSamples;

// A second whole format, unlike markup: separators between children, and quotes around text.

[TemplateFormat(typeof(JsonString), TextCulture = ValueCulture.Invariant, ContentType = "application/json; charset=utf-8")]
[Escape(
    "string",
    "\"", "\\\"", "\\", "\\\\", "\n", "\\n", "\r", "\\r", "\t", "\\t", "\b", "\\b", "\f", "\\f",
    "\u0000", "\\u0000", "\u0001", "\\u0001", "\u0002", "\\u0002", "\u0003", "\\u0003", "\u0004", "\\u0004",
    "\u0005", "\\u0005", "\u0006", "\\u0006", "\u0007", "\\u0007", "\u000B", "\\u000b", "\u000E", "\\u000e",
    "\u000F", "\\u000f", "\u0010", "\\u0010", "\u0011", "\\u0011", "\u0012", "\\u0012", "\u0013", "\\u0013",
    "\u0014", "\\u0014", "\u0015", "\\u0015", "\u0016", "\\u0016", "\u0017", "\\u0017", "\u0018", "\\u0018",
    "\u0019", "\\u0019", "\u001A", "\\u001a", "\u001B", "\\u001b", "\u001C", "\\u001c", "\u001D", "\\u001d",
    "\u001E", "\\u001e", "\u001F", "\\u001f")]
public abstract partial class JsonNode : TemplateNode;

[TextNode(Prefix = "\"", Suffix = "\"", Escape = "string")]
public sealed partial class JsonString : JsonNode;

/// <summary>Numbers, true, false and null: written as they are, so never from untrusted input.</summary>
[TextNode]
public sealed partial class JsonLiteral : JsonNode;

[Node(Start = "{", End = "}", Separator = ",")]
public sealed partial class JsonObject : JsonNode;

[Node(Start = "[", End = "]", Separator = ",")]
public sealed partial class JsonArray : JsonNode;

/// <summary>A property of an object, whose value follows in brackets: <c>Json.Prop("name") [ "value" ]</c>.</summary>
[Node]
public sealed partial class JsonProperty : JsonNode
{
    [Attr("\"", "\":", Escape = "string")]
    public partial JsonProperty Name(string? value);
}

[TemplateTags(typeof(JsonNode))]
public static partial class Json
{
    public static JsonObject Obj => new();

    public static JsonArray Arr => new();

    public static JsonProperty Prop(string name) => new();

    public static JsonString Str(string? value) => new();

    public static JsonLiteral Lit(string? value) => new();
}

/// <summary>A template that outputs JSON. To be given children, it also implements <see cref="IAcceptsChildren"/>.</summary>
public interface IJsonTemplate : ITemplate<JsonNode>;

/// <summary>A JSON template that streams.</summary>
public interface IAsyncJsonTemplate : IAsyncTemplate<JsonNode>;
