using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Reflection;
using Microsoft.AspNetCore.Http;

namespace TemplateDsl.AspNetCore;

/// <summary>Turns templates into results that minimal API endpoints and controller actions can return.</summary>
public static class TemplateResults
{
    /// <summary>Sent for a format that doesn't declare its content type.</summary>
    private const string DefaultContentType = "text/plain; charset=utf-8";

    private static readonly ConcurrentDictionary<Type, string> ContentTypes = new();

    /// <summary>
    /// A result that renders this template: <c>=> new Page(items).ToResult()</c>. The Content-Type is the one its format
    /// declares, such as <c>text/html; charset=utf-8</c>, unless <paramref name="contentType"/> is given.
    /// </summary>
    public static TemplateResult ToResult(this ITemplateRenderable template, int? statusCode = null, string? contentType = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        return new TemplateResult(template, statusCode, contentType ?? GetContentType(template));
    }

    /// <summary>
    /// A result that renders this template with the context it needs. In an endpoint, take the context as a parameter,
    /// as in <c>([FromServices] PageContext context) => new Page().ToResult(context)</c>. Without <c>[FromServices]</c>,
    /// a minimal API reads a type it doesn't find among the services from the request body.
    /// </summary>
    public static TemplateResult<TContext> ToResult<TContext>(this ITemplateRenderable<TContext> template, TContext context, int? statusCode = null, string? contentType = null)
        where TContext : notnull
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(context);
        return new TemplateResult<TContext>(template, context, statusCode, contentType ?? GetContentType(template));
    }

    /// <summary>Renders as content, which every template is, without or with the context it needs, and flushes.</summary>
    internal static async Task RenderAsync<TContext>(IRenderable template, PipeWriter writer, TContext context, CancellationToken cancellationToken)
        where TContext : notnull
    {
        await ((IAsyncTemplateContent<TemplateNode, TContext>)template).__RenderContentAsync(writer, context, cancellationToken).ConfigureAwait(false);
        await TemplateStreaming.FlushAsync(writer, cancellationToken).ConfigureAwait(false);
    }

    internal static void Prepare(HttpResponse response, int? statusCode, string contentType)
    {
        if (statusCode is { } status)
        {
            response.StatusCode = status;
        }

        response.ContentType = contentType;
    }

    /// <summary>
    /// The content type of the format the template outputs, looked up once per template type. A renderable that isn't
    /// a template, or a format that declares none, is sent as plain text.
    /// </summary>
    private static string GetContentType(IRenderable template) =>
        ContentTypes.GetOrAdd(template.GetType(), static type =>
            type.GetInterfaces()
                .Where(IsTemplateInterface)
                .Select(template => template.GetGenericArguments()[0].GetCustomAttribute<TemplateFormatAttribute>())
                .OfType<TemplateFormatAttribute>()
                .Select(format => format.ContentType)
                .FirstOrDefault()
            ?? DefaultContentType);

    private static bool IsTemplateInterface(Type type) =>
        type.IsGenericType
        && type.GetGenericTypeDefinition() is var definition
        && (definition == typeof(ITemplate<>) || definition == typeof(IAsyncTemplate<>)
            || definition == typeof(ITemplate<,>) || definition == typeof(IAsyncTemplate<,>));
}
