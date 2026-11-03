using System.Globalization;
using FormatSamples;

namespace SampleApp;

public sealed record Post(string Title, string Url, string? Summary);

/// <summary>The same kind of template as a page, with loops, ternaries and nesting, but it outputs RSS.</summary>
public sealed partial class BlogFeed(string blogTitle, IEnumerable<Post> posts) : RssTemplate
{
    public override IEnumerable<RssNode> Build() =>
    [
        Rss.Root [
            Rss.Channel [
                Rss.Title [ blogTitle ],
                Rss.Each(posts, post => new FeedItem(post))
            ]
        ]
    ];
}

public sealed partial class FeedItem(Post post) : RssTemplate
{
    public override IEnumerable<RssNode> Build() =>
    [
        Rss.Item [
            Rss.Title [ post.Title ],
            Rss.Link [ post.Url ],
            post.Summary is { } summary ? Rss.Description [ summary ] : null,
            Rss.Guid.IsPermaLink("true") [ post.Url ]
        ]
    ];
}

/// <summary>And this one outputs JSON.</summary>
public sealed partial class PostsJson(IEnumerable<Post> posts) : JsonTemplate
{
    public override IEnumerable<JsonNode> Build() =>
    [
        Json.Obj [
            Json.Prop("count") [ Json.Lit(posts.Count().ToString(CultureInfo.InvariantCulture)) ],
            Json.Prop("posts") [
                Json.Arr [
                    Json.Each(posts, post => Json.Obj [
                        Json.Prop("title") [ post.Title ],
                        Json.Prop("url") [ post.Url ],
                        post.Summary is { } summary ? Json.Prop("summary") [ summary ] : null
                    ])
                ]
            ]
        ]
    ];
}
