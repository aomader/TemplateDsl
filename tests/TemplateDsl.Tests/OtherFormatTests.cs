using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using FormatSamples;
using TemplateDsl.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Tests;

public sealed record FeedEntry(string Title, string? Summary);

public sealed partial class TestFeed(string name, IEnumerable<FeedEntry> entries) : IRssTemplate
{
    public IEnumerable<RssNode> Build() =>
    [
        Rss.Root
        [
            Rss.Channel
            [
                Rss.Title[name],
                Rss.Each(entries, entry => new TestFeedItem(entry))
            ]
        ]
    ];
}

public sealed partial class TestFeedItem(FeedEntry entry) : IRssTemplate
{
    public IEnumerable<RssNode> Build() =>
    [
        Rss.Item
        [
            Rss.Title[entry.Title],
            entry.Summary is { } summary ? Rss.Description[summary] : null,
            Rss.Guid.IsPermaLink(entry.Title)["const & id"]
        ]
    ];
}

public sealed partial class JsonDoc(string name, bool flag, string[] tags) : IJsonTemplate
{
    public IEnumerable<JsonNode> Build() =>
    [
        Json.Obj
        [
            flag ? Json.Prop("first")[Json.Lit("1")] : null,
            Json.Prop("name")[name],
            Json.Prop($"key-{name}")["const \"quoted\""],
            flag ? null : Json.Prop("second")[Json.Lit("true")],
            Json.Prop("tags")[Json.Arr[Json.Each(tags, tag => tag == "skip" ? null : Json.Str(tag))]],
            Json.Prop("empty")[Json.Obj],
            Json.Prop("nested")[new JsonPart(name)],
            Json.Prop("array")[Json.Arr[Json.Lit("1"), Json.Each(tags, tag => Json.Lit("2")), Json.Lit("3"), new JsonPart("x"), flag ? Json.Lit("4") : "five"]]
        ]
    ];
}

public sealed partial class JsonPart(string value) : IJsonTemplate
{
    public IEnumerable<JsonNode> Build() => [Json.Obj[Json.Prop("value")[value]]];
}

public sealed partial class JsonNullable(string? value) : IJsonTemplate
{
    public IEnumerable<JsonNode> Build() => [Json.Arr[Json.Lit("1"), value, Json.Lit("2")]];
}

public sealed partial class JsonEnvelope(string kind) : IJsonTemplate, IAcceptsChildren
{
    public IEnumerable<JsonNode> Build() => [Json.Obj[Json.Prop("kind")[kind], Json.Prop("data")[new JsonWrapper()[Children]]]];
}

public sealed partial class JsonWrapper : IJsonTemplate, IAcceptsChildren
{
    public IEnumerable<JsonNode> Build() => [Children];
}

public sealed partial class StreamedJson(IAsyncEnumerable<string> rows, Task<string> name) : IAsyncJsonTemplate
{
    public async Task<IEnumerable<JsonNode>> BuildAsync(CancellationToken ct) =>
    [
        Json.Obj
        [
            Json.Prop("name")[await name],
            Json.Prop("rows")[Json.Arr[await Json.EachAsync(rows, row => Json.Obj[Json.Prop("row")[row]])]]
        ]
    ];
}

// A format in the same project as its templates, where nothing generated can be seen while analyzing them.

[TemplateFormat(typeof(MiniText))]
[Escape("text", "|", "\\|", "(", "\\(")]
public abstract partial class MiniNode : TemplateNode;

[TextNode(Escape = "text")]
public sealed partial class MiniText : MiniNode;

[Node(Start = "(", End = ")", Separator = "|")]
public sealed partial class MiniList : MiniNode;

[TemplateTags(typeof(MiniNode))]
public static partial class Mini
{
    public static MiniList List => new();
}

public interface IMiniTemplate : ITemplate<MiniNode>;

public sealed partial class MiniDoc(string value, bool flag) : IMiniTemplate
{
    public IEnumerable<MiniNode> Build() =>
    [
        Mini.List["a", flag ? value : null, Mini.List[value, "x"], new MiniPart()]
    ];
}

public sealed partial class MiniPart : IMiniTemplate
{
    public IEnumerable<MiniNode> Build() => ["part"];
}

public class OtherFormatTests
{
    private const string Hostile = "a\"b\\c\nd\te\u0001f<g>&h'i";

    private static readonly FeedEntry[] Entries =
    [
        new("One & <two>", "A \"summary\""),
        new("Three", null),
    ];

    [Fact]
    public void Rss_output_is_valid_xml_with_the_original_values()
    {
        var xml = new TestFeed("My <blog> & more", Entries).RenderToString();

        var channel = XDocument.Parse(xml).Root!.Element("channel")!;

        Assert.Equal("My <blog> & more", channel.Element("title")!.Value);

        var items = channel.Elements("item").ToArray();
        Assert.Equal(2, items.Length);
        Assert.Equal("One & <two>", items[0].Element("title")!.Value);
        Assert.Equal("A \"summary\"", items[0].Element("description")!.Value);
        Assert.Null(items[1].Element("description"));
        Assert.Equal("One & <two>", items[0].Element("guid")!.Attribute("isPermaLink")!.Value);
        Assert.Equal("const & id", items[0].Element("guid")!.Value);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Json_output_parses_with_commas_only_between_rendered_children(bool flag)
    {
        var json = new JsonDoc(Hostile, flag, ["t1", "skip", "t2"]).RenderToString();

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(Hostile, root.GetProperty("name").GetString());
        Assert.Equal("const \"quoted\"", root.GetProperty($"key-{Hostile}").GetString());
        Assert.Equal(flag, root.TryGetProperty("first", out _));
        Assert.Equal(!flag, root.TryGetProperty("second", out _));
        Assert.Equal(["t1", "t2"], root.GetProperty("tags").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(Hostile, root.GetProperty("nested").GetProperty("value").GetString());
        Assert.Equal(7, root.GetProperty("array").GetArrayLength());
    }

    [Theory]
    [InlineData(new string[0], 3)]
    [InlineData(new[] { "skip" }, 4)]
    public void Json_handles_loops_without_output(string[] tags, int arrayLength)
    {
        using var document = JsonDocument.Parse(new JsonDoc("n", true, tags).RenderToString());

        Assert.Empty(document.RootElement.GetProperty("tags").EnumerateArray());
        Assert.Equal(arrayLength + 1, document.RootElement.GetProperty("array").GetArrayLength());
    }

    [Fact]
    public void Every_control_character_is_escaped_in_json()
    {
        var all = string.Concat(Enumerable.Range(0, 32).Select(i => (char)i));

        using var document = JsonDocument.Parse(new JsonDoc(all, true, []).RenderToString());

        Assert.Equal(all, document.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Json_async_path_matches_the_sync_path()
    {
        var template = new JsonDoc(Hostile, true, ["a", "b"]);
        using var stream = new MemoryStream();

        await template.RenderToAsync(stream);

        Assert.Equal(template.RenderToString(), Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public async Task Json_streams_rows_as_they_arrive()
    {
        var rows = System.Threading.Channels.Channel.CreateUnbounded<string>();
        var name = new TaskCompletionSource<string>();
        using var stream = new MemoryStream();
        var writer = System.IO.Pipelines.PipeWriter.Create(stream, new System.IO.Pipelines.StreamPipeWriterOptions(leaveOpen: true));

        var render = new StreamedJson(rows.Reader.ReadAllAsync(), name.Task).RenderToAsync(writer).AsTask();

        await WaitForAsync(() => Encoding.UTF8.GetString(stream.ToArray()) == "{\"name\":");

        name.SetResult("n");
        rows.Writer.TryWrite("one");
        await WaitForAsync(() => Encoding.UTF8.GetString(stream.ToArray()).Contains("\"one\""));
        Assert.False(render.IsCompleted);

        rows.Writer.TryWrite("two");
        rows.Writer.Complete();
        await render;
        await writer.CompleteAsync();

        using var document = JsonDocument.Parse(stream.ToArray());
        Assert.Equal(2, document.RootElement.GetProperty("rows").GetArrayLength());
    }

    [Fact]
    public void Format_in_the_same_project_as_its_templates_works()
    {
        Assert.Equal("(a|v\\|w|(v\\|w|x)|part)", new MiniDoc("v|w", true).RenderToString());
        Assert.Equal("(a|(\\(|x)|part)", new MiniDoc("(", false).RenderToString());
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(25);
        }

        Assert.True(condition());
    }

    private const string Usings = """
        using System.Collections.Generic;
        using TemplateDsl;
        using TemplateDsl.Html;
        using FormatSamples;

        """;

    [Fact]
    public void A_format_that_does_not_opt_in_rejects_values_that_are_not_strings()
    {
        var (_, errors) = Run("""
            public sealed partial class T : IRssTemplate
            {
                public IEnumerable<RssNode> Build() => [Rss.Title [ 5 ]];
            }
            """);

        // In brackets, what isn't a node goes to the Add method for templates, whose constraint a number doesn't meet.
        Assert.Contains("CS0315", errors);
    }

    private static (string[] DiagnosticIds, string[] Errors) Run(string source, string usings = Usings)
    {
        var compilation = CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText(usings + source)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        CSharpGeneratorDriver.Create(new TemplateGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        return (
            diagnostics.Select(d => d.Id).ToArray(),
            output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id).ToArray());
    }

    [Fact]
    public void Nodes_of_another_format_are_rejected()
    {
        var (diagnostics, _) = Run("""
            public sealed partial class T : IJsonTemplate
            {
                public IEnumerable<JsonNode> Build() => [Json.Obj[TemplateDsl.Html.H.div]];
            }
            """);

        Assert.Contains("TDSL004", diagnostics);
    }

    [Fact]
    public void Templates_of_another_format_do_not_compile()
    {
        var (_, errors) = Run("""
            public sealed partial class Html : IHtmlTemplate
            {
                public IEnumerable<HtmlElement> Build() => [];
            }

            public sealed partial class T : IJsonTemplate
            {
                public IEnumerable<JsonNode> Build() => [Json.Obj[new Html()]];
            }
            """);

        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Children_in_a_node_with_a_separator_are_reported()
    {
        var (diagnostics, _) = Run("""
            public sealed partial class T : IJsonTemplate, IAcceptsChildren
            {
                public IEnumerable<JsonNode> Build() => [Json.Arr[Children]];
            }
            """);

        Assert.Equal("TDSL004", Assert.Single(diagnostics));
    }

    [Fact]
    public void Template_of_something_that_is_not_a_format_is_reported()
    {
        var (diagnostics, _) = Run("""
            public sealed class NotAFormat : TemplateNode;

            public sealed partial class T : ITemplate<NotAFormat>
            {
                public IEnumerable<NotAFormat> Build() => [];
            }
            """);

        Assert.Equal("TDSL006", Assert.Single(diagnostics));
    }

}



public sealed partial class JsonEnvelopePage : IJsonTemplate
{
    public IEnumerable<JsonNode> Build() => [new JsonEnvelope("k") [ Json.Str("payload") ]];
}

public class OtherFormatChildrenTests
{
    [Theory]
    [InlineData(null, "[1,2]")]
    [InlineData("s", "[1,\"s\",2]")]
    public void Null_string_child_renders_nothing(string? value, string expected)
    {
        Assert.Equal(expected, new JsonNullable(value).RenderToString());
    }

    [Fact]
    public void Json_template_accepts_children_outside_a_separator()
    {
        using var document = JsonDocument.Parse(new JsonEnvelopePage().RenderToString());

        Assert.Equal("k", document.RootElement.GetProperty("kind").GetString());
        Assert.Equal("payload", document.RootElement.GetProperty("data").GetString());
    }
}
