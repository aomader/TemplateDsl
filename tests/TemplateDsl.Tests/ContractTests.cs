using System.Buffers;
using System.Text;
using Xunit;

namespace Tests;

/// <summary>An async contract without BuildAsync, which the generator adds as abstract.</summary>
public abstract partial class AsyncShell : IAsyncHtmlTemplate
{
    public abstract string Title { get; }
}

public sealed partial class AsyncShellPage : AsyncShell
{
    public override string Title => "async";

    public override async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken cancellationToken) =>
        [H.P [ await Task.FromResult(Title) ]];
}

public sealed partial class AsyncShellLayout(AsyncShell page) : IAsyncHtmlTemplate
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken cancellationToken) =>
        [H.H1 [ page.Title ], H.Main [ await page ]];
}

/// <summary>An async contract with a context.</summary>
public abstract partial class AsyncContextShell : IAsyncHtmlTemplate<TestContext>;

public sealed partial class AsyncContextShellPage : AsyncContextShell
{
    public override async Task<IEnumerable<HtmlElement>> BuildAsync(TestContext context, CancellationToken cancellationToken) =>
        [H.P [ await Task.FromResult(context.User) ]];
}

/// <summary>A sync contract with a context.</summary>
public abstract partial class ContextShell : IHtmlTemplate<TestContext>;

public sealed partial class ContextShellPage : ContextShell
{
    public override IEnumerable<HtmlElement> Build(TestContext context) => [H.P [ context.Greeting ]];
}

public sealed partial class ContextShellLayout(ContextShell page) : IHtmlTemplate<TestContext>
{
    public IEnumerable<HtmlElement> Build(TestContext context) => [H.Main [ page ]];
}

/// <summary>A contract that takes children, which the templates derived from it place.</summary>
public abstract partial class Frame : IHtmlTemplate, IAcceptsChildren;

public sealed partial class BlueFrame : Frame
{
    public override IEnumerable<HtmlElement> Build() => [H.Div.Class("blue") [ Children ]];
}

public sealed partial class FrameUser(Frame frame) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [frame [ "inside" ]];
}

/// <summary>An async contract that takes children.</summary>
public abstract partial class AsyncFrame : IAsyncHtmlTemplate, IAcceptsChildren;

public sealed partial class AsyncBlueFrame : AsyncFrame
{
    public override async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken cancellationToken) =>
        [H.Div.Class(await Task.FromResult("blue")) [ Children ]];
}

public sealed partial class AsyncFrameUser(AsyncFrame frame) : IAsyncHtmlTemplate
{
    public async Task<IEnumerable<HtmlElement>> BuildAsync(CancellationToken cancellationToken) => [await frame [ "inside" ]];
}

/// <summary>Takes children and isn't sealed, so that a template derived from it can write them its own way.</summary>
public partial class OpenCard(string title) : IHtmlTemplate, IAcceptsChildren
{
    protected string Title => title;

    public virtual IEnumerable<HtmlElement> Build() => [H.Section [ H.H2 [ title ], Children ]];
}

public sealed partial class FancyCard(string title) : OpenCard(title)
{
    public override IEnumerable<HtmlElement> Build() => [H.Article [ H.H3 [ Title ], Children ]];
}

/// <summary>Gives children to a template of a type parameter, which has the methods through its constraint.</summary>
public sealed partial class CardHolder<TCard>(TCard card) : IHtmlTemplate
    where TCard : OpenCard
{
    public IEnumerable<HtmlElement> Build() => [card [ "held" ]];
}

/// <summary>Content that implements the interfaces by hand, without the generated methods.</summary>
public sealed class NotGeneratedContent : ITemplateContent<TemplateNode, object>;

public class ContractTests
{
    private static readonly TestContext Context = new("Hello", "Ann");

    [Fact]
    public async Task An_async_contract_gets_build_and_its_render_methods_as_abstract()
    {
        Assert.Equal("<h1>async</h1><main><p>async</p></main>", await new AsyncShellLayout(new AsyncShellPage()).RenderToStringAsync());
        Assert.Equal("<p>Ann</p>", await new AsyncContextShellPage().RenderToStringAsync(Context));
    }

    [Fact]
    public void A_contract_with_a_context_renders_through_its_type()
    {
        Assert.Equal("<main><p>Hello</p></main>", new ContextShellLayout(new ContextShellPage()).RenderToString(Context));
    }

    [Fact]
    public async Task A_contract_that_takes_children_is_given_them_through_its_type()
    {
        Assert.Equal("<div class=\"blue\">inside</div>", new FrameUser(new BlueFrame()).RenderToString());
        Assert.Equal("<div class=\"blue\"></div>", new BlueFrame().RenderToString());
        Assert.Equal("<div class=\"blue\">inside</div>", await new AsyncFrameUser(new AsyncBlueFrame()).RenderToStringAsync());
        Assert.Equal("<div class=\"blue\"></div>", await new AsyncBlueFrame().RenderToStringAsync());
    }

    [Fact]
    public void A_derived_template_writes_its_children_its_own_way()
    {
        Assert.Equal("<article><h3>f</h3>held</article>", new CardHolder<OpenCard>(new FancyCard("f")).RenderToString());
        Assert.Equal("<section><h2>o</h2>held</section>", new CardHolder<OpenCard>(new OpenCard("o")).RenderToString());
    }

    [Fact]
    public void Rendering_with_a_context_writes_to_a_buffer()
    {
        var writer = new ArrayBufferWriter<byte>();

        var page = new ContextShellPage();

        // The extension method, which the generated instance method of the class hides.
        TemplateRendering.RenderTo(page, writer, Context);

        Assert.Equal("<p>Hello</p>", Encoding.UTF8.GetString(writer.WrittenSpan));
        Assert.Throws<ArgumentNullException>(() => TemplateRendering.RenderTo<ContextShellPage, TestContext>(null!, writer, Context));
        Assert.Throws<ArgumentNullException>(() => TemplateRendering.RenderTo(page, null!, Context));
        Assert.Throws<ArgumentNullException>(() => TemplateRendering.RenderTo<ContextShellPage, TestContext>(page, writer, null!));
    }

    [Fact]
    public async Task Content_without_generated_methods_says_why()
    {
        var sync = Assert.Throws<InvalidOperationException>(() => new NotGeneratedContent().RenderToString());
        var async = await Assert.ThrowsAsync<InvalidOperationException>(async () => await new NotGeneratedContent().RenderToAsync(Stream.Null));

        Assert.Contains("NotGeneratedContent", sync.Message);
        Assert.Contains("partial", async.Message);
        Assert.Throws<ArgumentNullException>(() => TemplateBuild.NotGenerated(null!));
    }
}
