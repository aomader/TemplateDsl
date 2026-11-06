namespace SampleApp;

/// <summary>What a shared component needs. It doesn't have to know about the rest of the app's context.</summary>
public interface IHasLanguage
{
    string Language { get; }
}

/// <summary>Everything the templates of this app can use while rendering one request.</summary>
public sealed record RequestContext(string Language, string UserName) : IHasLanguage;

public sealed partial class LanguageBadge : IHtmlTemplate<IHasLanguage>
{
    public IEnumerable<HtmlElement> Build(IHasLanguage context) =>
    [
        H.Span.Class("language") [ context.Language ]
    ];
}

/// <summary>Neither this page nor the card in between passes the context to the badge: it arrives by itself.</summary>
public sealed partial class ContextPage(string title) : IHtmlTemplate<RequestContext>
{
    public IEnumerable<HtmlElement> Build(RequestContext context) =>
    [
        H.H1 [ title, " for ", context.UserName ],
        new Card("Settings") [ new LanguageBadge() ]
    ];
}
