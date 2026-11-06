using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using Xunit;

namespace Tests;

public enum BadgeKind
{
    Primary,
    Secondary,
}

public sealed partial class RecordBadge(string label) : IHtmlTemplate
{
    public BadgeKind Kind { get; init; } = BadgeKind.Primary;

    public IEnumerable<HtmlElement> Build() =>
    [
        H.Div.Class(Kind == BadgeKind.Primary ? "primary" : "secondary")[label]
    ];
}

public sealed partial class ClassBadge(string label) : IHtmlTemplate
{
    public BadgeKind Kind { get; init; } = BadgeKind.Primary;

    public IEnumerable<HtmlElement> Build() =>
    [
        H.Div.Class(Kind == BadgeKind.Primary ? "primary" : "secondary")[label]
    ];
}

public sealed partial class HelperBadge(string label) : IHtmlTemplate
{
    private string Decorated => $"*{label}*";

    public IEnumerable<HtmlElement> Build() => [H.Div.Id(this.Decorated)[Shout()]];

    private string Shout() => label.ToUpperInvariant();
}

public class ComponentTests
{
    private const string Html = "<div class=\"primary\">Hello</div>";

    [Fact]
    public void RenderToString_can_be_called_on_a_record()
    {
        Assert.Equal(Html, new RecordBadge("Hello").RenderToString());
    }

    [Fact]
    public void Class_primary_constructor_parameters_can_be_used_directly()
    {
        Assert.Equal(Html, new ClassBadge("Hello").RenderToString());
    }

    [Fact]
    public void Properties_of_the_instance_are_used()
    {
        Assert.Equal(
            "<div class=\"secondary\">Cancel</div>",
            new ClassBadge("Cancel") { Kind = BadgeKind.Secondary }.RenderToString());
    }

    [Fact]
    public void Instance_members_and_this_can_be_used()
    {
        Assert.Equal("<div id=\"*a*\">A</div>", new HelperBadge("a").RenderToString());
    }

    [Fact]
    public void RenderTo_writes_to_a_buffer_writer()
    {
        var writer = new ArrayBufferWriter<byte>();

        new RecordBadge("Hello").RenderTo(writer);

        Assert.Equal(Html, Encoding.UTF8.GetString(writer.WrittenSpan));
    }

    [Fact]
    public async Task RenderToAsync_writes_to_a_stream()
    {
        using var stream = new MemoryStream();

        await new RecordBadge("Hello").RenderToAsync(stream);

        Assert.Equal(Html, Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public async Task RenderToAsync_writes_to_a_pipe_writer()
    {
        var pipe = new Pipe();

        await new RecordBadge("Hello").RenderToAsync(pipe.Writer);

        var result = await pipe.Reader.ReadAsync();
        Assert.Equal(Html, Encoding.UTF8.GetString(result.Buffer.ToArray()));
    }
}

public class TemplateRenderingTests
{
    private const string Hello = "<div class=\"primary\">Hello</div>";

    [Fact]
    public void Generated_code_has_only_what_needs_private_access()
    {
        var methods = typeof(RecordBadge).GetMethods().Select(m => m.Name).ToArray();

        Assert.Contains("RenderTo", methods);
        Assert.DoesNotContain("__RenderAsync", methods);
        Assert.DoesNotContain("RenderToString", methods);
        Assert.DoesNotContain("RenderToAsync", methods);
    }

    [Fact]
    public void Empty_template_renders_to_an_empty_string()
    {
        Assert.Equal("", new EmptyTemplate().RenderToString());
    }

    [Fact]
    public async Task Stream_stays_open_and_is_written_through_the_interface()
    {
        IAsyncHtmlContent view = new RecordBadge("Hello");
        using var stream = new MemoryStream();

        await view.RenderToAsync(stream);

        Assert.True(stream.CanWrite);
        Assert.Equal(Hello, System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }
}

public sealed partial class TemplateWithRenderParameterNames(string writer, string context) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [H.P [ writer ], H.P [ context ]];
}

public class ParameterNameTests
{
    [Fact]
    public void A_template_can_have_data_named_like_the_parameters_of_RenderTo() =>
        Assert.Equal("<p>a</p><p>b</p>", new TemplateWithRenderParameterNames("a", "b").RenderToString());
}
