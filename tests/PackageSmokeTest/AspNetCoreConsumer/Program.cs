using System.Text;
using Microsoft.AspNetCore.Http;
using TemplateDsl.Html;

// No web server needed: the result writes to the response of any HttpContext.
var context = new DefaultHttpContext();
context.Response.Body = new MemoryStream();

await new Hello("web").ToResult(statusCode: 201).ExecuteAsync(context);

Console.Write($"{context.Response.StatusCode} {context.Response.ContentType} {Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray())}");

public sealed partial class Hello(string name) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() => [H.B [ name ]];
}
