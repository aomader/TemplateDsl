using SampleApp;

HelloPage[] pages =
[
    new("Ann", isAdmin: true, unreadMessages: 3),
    new("Bob", isAdmin: false, unreadMessages: 2),
    new("Cid", isAdmin: false, unreadMessages: 0),
];

foreach (var page in pages)
{
    Console.WriteLine(page.RenderToString());
}

Console.WriteLine(new Button("Hello").RenderToString());
Console.WriteLine(new Button("Cancel") { Kind = ButtonKind.Secondary }.RenderToString());
Console.WriteLine(new SettingsPage().RenderToString());
Console.WriteLine(new TodoList([new("Write code", Done: true), new("Ship it", Done: false)]).RenderToString());

// The same mechanism, in other output formats.
Post[] posts = [new("Hello & welcome", "https://example.com/1", "A \"first\" post"), new("Second", "https://example.com/2", null)];
Console.WriteLine(new BlogFeed("My <blog>", posts).RenderToString());
Console.WriteLine(new PostsJson(posts).RenderToString());

Console.WriteLine(new ContextPage("Dashboard").RenderToString(new RequestContext("de", "Ann")));

// Watch this one appear piece by piece.
await using var output = Console.OpenStandardOutput();
await new OrdersPage(new OrderRepository(), userId: 5).RenderToAsync(output);
Console.WriteLine();
