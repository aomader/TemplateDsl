using TemplateDsl;
using TemplateDsl.AspNetCore;
using WebSample;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// The page doesn't know its layout, so anything can decide: try /?layout=plain.
app.MapGet("/", (string? layout) =>
{
    Page page = new IndexPage(Entries.StreamAsync(count: 10));

    ITemplateRenderable view = layout == "plain"
        ? new PlainLayout(page)
        : new MainLayout(page);

    return view.ToResult();
});

app.Run();
