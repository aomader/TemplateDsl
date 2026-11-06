using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using Xunit;

namespace Tests;

public readonly partial record struct StructGreeting(string Name) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [H.P [ "Hi ", Name ]];
}

/// <summary>The same output as <see cref="StructGreeting"/>, from a class.</summary>
public sealed partial class ClassGreeting(string name) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [H.P [ "Hi ", name ]];
}

/// <summary>Asks for less than the context it is given, one interface of it.</summary>
public readonly partial record struct StructGreetingFromContext : IHtmlTemplate<IHasGreeting>
{
    public IEnumerable<HtmlElement> Build(IHasGreeting context) => [H.P [ context.Greeting ]];
}

public readonly partial record struct StructContextContent : IHtmlTemplate<TestContext>
{
    public IEnumerable<HtmlElement> Build(TestContext context) => [H.B [ context.User ]];
}

public readonly partial record struct AsyncStructGreeting(string Name) : IAsyncHtmlTemplate
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken cancellationToken) => [H.P [ await Task.FromResult(Name) ]];
}

public class StructRenderingTests
{
    private static long Allocated(Action render)
    {
        // Once first, so that what is allocated once, such as the compiled code, doesn't count.
        render();

        var before = GC.GetAllocatedBytesForCurrentThread();
        render();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public void A_struct_renders_without_being_boxed()
    {
        var template = new StructGreeting("Ann");
        var writer = new ArrayBufferWriter<byte>(256);

        Assert.Equal(0, Allocated(() => { writer.Clear(); template.RenderTo(writer); }));
        Assert.Equal("<p>Hi Ann</p>", Encoding.UTF8.GetString(writer.WrittenSpan));
    }

    [Fact]
    public void A_struct_with_a_context_renders_without_being_boxed()
    {
        var template = new StructGreetingFromContext();
        var context = new TestContext("Hello", "Ann");
        var writer = new ArrayBufferWriter<byte>(256);

        Assert.Equal(0, Allocated(() => { writer.Clear(); template.RenderTo(writer, context); }));
        Assert.Equal("<p>Hello</p>", Encoding.UTF8.GetString(writer.WrittenSpan));
        Assert.Equal("<p>Hello</p>", template.RenderToString(context));
    }

    [Fact]
    public void A_struct_allocates_only_the_string_like_a_class()
    {
        var structTemplate = new StructGreeting("Ann");
        var classTemplate = new ClassGreeting("Ann");

        Assert.Equal(Allocated(() => classTemplate.RenderToString()), Allocated(() => structTemplate.RenderToString()));
        Assert.Equal("<p>Hi Ann</p>", structTemplate.RenderToString());
    }

    [Fact]
    public async Task A_struct_renders_to_a_stream_and_a_pipe()
    {
        using var stream = new MemoryStream();
        await new StructGreeting("Ann").RenderToAsync(stream);
        Assert.Equal("<p>Hi Ann</p>", Encoding.UTF8.GetString(stream.ToArray()));

        using var withContext = new MemoryStream();
        await new StructGreetingFromContext().RenderToAsync(withContext, new TestContext("Hello", "Ann"));
        Assert.Equal("<p>Hello</p>", Encoding.UTF8.GetString(withContext.ToArray()));

        var pipe = new Pipe();
        await new StructGreeting("Bob").RenderToAsync(pipe.Writer);
        await pipe.Writer.CompleteAsync();
        var read = await pipe.Reader.ReadAsync();
        Assert.Equal("<p>Hi Bob</p>", Encoding.UTF8.GetString(read.Buffer));
    }

    [Fact]
    public void Struct_content_through_a_type_parameter_renders_without_being_boxed()
    {
        var context = new TestContext("Hello", "Ann");
        var withoutContext = new ShapeGenericLayout<ShapeRow>(new ShapeRow("a", 1));
        var withContext = new ShapeGenericLayout<StructContextContent>(new StructContextContent());
        var writer = new ArrayBufferWriter<byte>(256);

        Assert.Equal(0, Allocated(() => { writer.Clear(); withoutContext.RenderTo(writer, context); }));
        Assert.Equal("<main><li>a 1</li></main>", Encoding.UTF8.GetString(writer.WrittenSpan));
        Assert.Equal(0, Allocated(() => { writer.Clear(); withContext.RenderTo(writer, context); }));
        Assert.Equal("<main><b>Ann</b></main>", Encoding.UTF8.GetString(writer.WrittenSpan));
    }

    [Fact]
    public async Task An_async_struct_renders_through_the_interface()
    {
        Assert.Equal("<p>Ann</p>", await new AsyncStructGreeting("Ann").RenderToStringAsync());

        using var stream = new MemoryStream();
        await new AsyncStructGreeting("Bob").RenderToAsync(stream);
        Assert.Equal("<p>Bob</p>", Encoding.UTF8.GetString(stream.ToArray()));
    }
}
