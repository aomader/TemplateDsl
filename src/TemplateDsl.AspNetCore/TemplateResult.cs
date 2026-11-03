using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace TemplateDsl.AspNetCore;

/// <summary>
/// Renders a template as the response of a minimal API endpoint or a controller action: <c>=> new Page(items).ToResult()</c>.
/// An async template streams, a sync one is sent in one go. The status and headers go out with the first output, so set
/// headers before returning. Once an async template has sent output, an exception can only abort the response, so load
/// what can fail before returning, and stream what can't.
/// </summary>
public sealed class TemplateResult : IResult, IActionResult, IStatusCodeHttpResult, IContentTypeHttpResult
{
    internal TemplateResult(ITemplateRenderable template, int? statusCode, string contentType)
    {
        Template = template;
        StatusCode = statusCode;
        ContentType = contentType;
    }

    /// <summary>The template that this result renders.</summary>
    public ITemplateRenderable Template { get; }

    /// <summary>The status code to send, or null to leave it as it is, which is 200 unless something changed it.</summary>
    public int? StatusCode { get; }

    /// <summary>The Content-Type to send, by default the one the template's format declares.</summary>
    public string ContentType { get; }

    /// <summary>Sets the status and the Content-Type, then renders the template as the body of the response.</summary>
    public Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        TemplateResults.Prepare(httpContext.Response, StatusCode, ContentType);
        return Template.RenderToAsync(httpContext.Response.BodyWriter, httpContext.RequestAborted).AsTask();
    }

    Task IActionResult.ExecuteResultAsync(ActionContext context) => ExecuteAsync(context.HttpContext);
}

/// <summary>
/// Like <see cref="TemplateResult"/>, for a template that needs a <typeparamref name="TContext"/>, which it is given.
/// </summary>
public sealed class TemplateResult<TContext> : IResult, IActionResult, IStatusCodeHttpResult, IContentTypeHttpResult
    where TContext : notnull
{
    private readonly TContext _context;

    internal TemplateResult(ITemplateRenderable<TContext> template, TContext context, int? statusCode, string contentType)
    {
        Template = template;
        _context = context;
        StatusCode = statusCode;
        ContentType = contentType;
    }

    /// <inheritdoc cref="TemplateResult.Template"/>
    public ITemplateRenderable<TContext> Template { get; }

    /// <inheritdoc cref="TemplateResult.StatusCode"/>
    public int? StatusCode { get; }

    /// <inheritdoc cref="TemplateResult.ContentType"/>
    public string ContentType { get; }

    /// <inheritdoc cref="TemplateResult.ExecuteAsync"/>
    public Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        TemplateResults.Prepare(httpContext.Response, StatusCode, ContentType);
        return Template.RenderToAsync(httpContext.Response.BodyWriter, _context, httpContext.RequestAborted).AsTask();
    }

    Task IActionResult.ExecuteResultAsync(ActionContext context) => ExecuteAsync(context.HttpContext);
}
