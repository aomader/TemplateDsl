namespace SampleApp;

public sealed partial class HelloPage(string userName, bool isAdmin, int unreadMessages) : HtmlTemplate
{
    public override IEnumerable<HtmlElement> Build()
    {
        // An if statement can choose the whole output...
        if (isAdmin)
        {
            return
            [
                H.Html [
                    H.Body [
                        H.Div.Id($"admin-{userName}").Class("admin").Attr("data-role", "admin") [
                            $"Administrator {userName} at {DateTime.UtcNow}",
                            new Card("Admin tools") [
                                new Button("Save"),
                                new Button("Delete") { Kind = ButtonKind.Secondary },
                                unreadMessages > 0 ? new Button($"Read {unreadMessages}") : null
                            ]
                        ]
                    ]
                ]
            ];
        }

        // ...and ternaries can choose attributes, text and elements inside of it.
        return
        [
            H.Html [
                H.Body [
                    H.Div.Id($"user-{userName}").Class(unreadMessages > 0 ? "user has-unread" : "user") [
                        $"Hello {userName}! ",
                        unreadMessages > 0
                            ? H.Div.Class("unread") [ $"{unreadMessages} unread messages" ]
                            : "No new messages",
                        H.Div.Hidden(unreadMessages == 0).Attr("aria-label", "Message list") [ "Messages" ]
                    ]
                ]
            ]
        ];
    }
}
