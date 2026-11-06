namespace TemplateDsl.Html;

/// <summary>
/// HTML content: any sync HTML template that needs no context. It is the type for content passed to a layout or a slot,
/// <c>Layout(IHtmlContent content)</c>, which places it in brackets: <c>H.Main [ content ]</c>. A template that needs a
/// context is a compiler error where it is passed. For a layout with a context, it is <see cref="IHtmlContent{TContext}"/>.
/// </summary>
public interface IHtmlContent : IHtmlContent<object>, IAsyncHtmlContent;

/// <summary>
/// HTML content that renders with a <typeparamref name="TContext"/>: any sync HTML template that needs no context, that
/// context, or less of it. It is the type for content passed to a layout or a slot that has that context,
/// <c>Layout(IHtmlContent&lt;PageContext&gt; content)</c>, which places it in brackets: <c>H.Main [ content ]</c>. A
/// template that needs another context is a compiler error where it is passed.
/// </summary>
public interface IHtmlContent<in TContext> : ITemplateContent<HtmlElement, TContext>, IAsyncHtmlContent<TContext>
    where TContext : notnull;

/// <summary>
/// HTML content for an async template, which places it with <c>await</c>: any HTML template, sync or async, that needs no
/// context. It is the type for content of an async layout that may host a sync or an async page,
/// <c>Layout(IAsyncHtmlContent content)</c>, which places it with <c>H.Main [ await content ]</c>. For a layout with a
/// context, it is <see cref="IAsyncHtmlContent{TContext}"/>.
/// </summary>
public interface IAsyncHtmlContent : IAsyncHtmlContent<object>;

/// <summary>
/// HTML content for an async template, which places it with <c>await</c>: any HTML template, sync or async, that renders
/// with a <typeparamref name="TContext"/>, because it needs it, less of it, or none. It is the type for content of an
/// async layout with that context that may host a sync or an async page,
/// <c>Layout(IAsyncHtmlContent&lt;PageContext&gt; content)</c>, which places it with <c>H.Main [ await content ]</c>.
/// </summary>
public interface IAsyncHtmlContent<in TContext> : IAsyncTemplateContent<HtmlElement, TContext>
    where TContext : notnull;

/// <summary>
/// A template that outputs HTML. It can be a class, a struct or a record, declared <c>partial</c>, and is created like
/// any other object: <c>new Button("Hello").RenderToString()</c>. To be given children in brackets, it also implements
/// <see cref="IAcceptsChildren"/>: <c>new Card("Title") [ "text", new Button("Save") ]</c>. The generator then adds a
/// private <c>Children</c> property, which <c>Build</c> places where the children should go.
/// </summary>
public interface IHtmlTemplate : ITemplate<HtmlElement>, IHtmlContent;

/// <summary>
/// An HTML template that needs something from the render call, such as translations or the current user:
/// <c>Build(TContext context)</c>. Rendering takes the context: <c>new Page().RenderToString(context)</c>. Nested
/// templates get it from their parent, without passing it along by hand.
/// </summary>
public interface IHtmlTemplate<in TContext> : ITemplate<HtmlElement, TContext>, IHtmlContent<TContext>
    where TContext : notnull;

/// <summary>
/// An HTML template that streams: output is sent as it is rendered, also before an <c>await</c>, and a slow client
/// slows down rendering. It doesn't have to await anything. It only renders asynchronously, and using it in
/// another async template takes an await: <c>await new Page()</c>.
/// </summary>
public interface IAsyncHtmlTemplate : IAsyncTemplate<HtmlElement>, IAsyncHtmlContent;

/// <summary>An <see cref="IAsyncHtmlTemplate"/> with a context, see <see cref="IHtmlTemplate{TContext}"/>.</summary>
public interface IAsyncHtmlTemplate<in TContext> : IAsyncTemplate<HtmlElement, TContext>, IAsyncHtmlContent<TContext>
    where TContext : notnull;
