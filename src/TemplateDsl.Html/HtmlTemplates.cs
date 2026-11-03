namespace TemplateDsl.Html;

/// <summary>A template that outputs HTML, created like any other object: <c>new Button("Hello").RenderToString()</c>.</summary>
[TemplateBase]
public abstract partial class HtmlTemplate : HtmlElement, ITemplate<HtmlElement>;

/// <summary>
/// An HTML template that accepts children: <c>new Card("Title") [ "text", new Button("Save") ]</c>. The generator
/// adds the brackets and a private <c>Children</c> property, which <c>Build</c> places where the children
/// should go: <c>H.Div.Class("body") [ Children ]</c>. Other templates cannot be given children.
/// </summary>
[TemplateBase]
public abstract partial class HtmlTemplateWithChildren : HtmlTemplate, IAcceptsChildren;

/// <summary>
/// An HTML template that streams: output is sent as it is rendered, also before an <c>await</c>, and a slow client
/// slows down rendering. It doesn't have to await anything. It only renders asynchronously, and using it in
/// another async template takes an await: <c>await new Page()</c>.
/// </summary>
[TemplateBase]
public abstract partial class AsyncHtmlTemplate : IAsyncTemplate<HtmlElement>;

/// <summary>An <see cref="AsyncHtmlTemplate"/> that accepts children, see <see cref="HtmlTemplateWithChildren"/>.</summary>
[TemplateBase]
public abstract partial class AsyncHtmlTemplateWithChildren : AsyncHtmlTemplate, IAcceptsChildren;

/// <summary>
/// An HTML template that needs something from the render call, such as translations or the current user:
/// <c>Build(TContext context)</c>. Rendering takes the context: <c>new Page().RenderToString(context)</c>. Nested
/// templates get it from their parent, without passing it along by hand.
/// </summary>
[TemplateBase]
public abstract partial class HtmlTemplate<TContext> : HtmlElement, ITemplate<HtmlElement, TContext>
    where TContext : notnull;

/// <summary>An <see cref="HtmlTemplate{TContext}"/> that accepts children, see <see cref="HtmlTemplateWithChildren"/>.</summary>
[TemplateBase]
public abstract partial class HtmlTemplateWithChildren<TContext> : HtmlTemplate<TContext>, IAcceptsChildren
    where TContext : notnull;

/// <summary>An <see cref="AsyncHtmlTemplate"/> with a context, see <see cref="HtmlTemplate{TContext}"/>.</summary>
[TemplateBase]
public abstract partial class AsyncHtmlTemplate<TContext> : IAsyncTemplate<HtmlElement, TContext>
    where TContext : notnull;

/// <summary>An <see cref="AsyncHtmlTemplate{TContext}"/> that accepts children, see <see cref="HtmlTemplateWithChildren"/>.</summary>
[TemplateBase]
public abstract partial class AsyncHtmlTemplateWithChildren<TContext> : AsyncHtmlTemplate<TContext>, IAcceptsChildren
    where TContext : notnull;
