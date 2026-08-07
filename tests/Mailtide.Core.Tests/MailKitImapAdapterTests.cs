using Mailtide.Core;
using Mailtide.Core.Imap;
using Mailtide.Core.Tests.Protocol;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class MailKitImapAdapterTests
{
    [TestMethod]
    public async Task Real_IMAP_adapter_discovers_Mailboxes_and_fetches_Messages()
    {
        await using var server = LoopbackImapServer.Start(
        [
            new SeededMailbox(
                Path: "INBOX",
                Attributes: ["Inbox"],
                Messages:
                [
                    new SeededImapMessage(
                        Uid: 1,
                        Subject: "Hello offline",
                        From: "bob@example.com",
                        InternalDate: new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero),
                        IsRead: false,
                        BodyText: "Body stays local."),
                ]),
            new SeededMailbox(
                Path: "Sent",
                Attributes: ["Sent"],
                Messages: []),
        ]);

        await using var client = new MailKitImapClientFactory().Create();
        await client.ConnectAndAuthenticateAsync(
            "127.0.0.1",
            server.Port,
            "alice@example.com",
            "s3cret-password");

        var mailboxes = await client.ListMailboxesAsync();
        Assert.IsTrue(mailboxes.Any(m => m.Path == "INBOX" && m.Role == MailboxRole.Inbox));
        Assert.IsTrue(mailboxes.Any(m => m.Path == "Sent" && m.Role == MailboxRole.Sent));

        var messages = await client.FetchMessagesAsync("INBOX");
        Assert.HasCount(1, messages);
        Assert.AreEqual("Hello offline", messages[0].Subject);
        Assert.AreEqual("bob@example.com", messages[0].FromAddress);
        Assert.AreEqual("Body stays local.", messages[0].BodyText);
        Assert.IsFalse(messages[0].IsRead);
    }

    [TestMethod]
    public async Task Real_IMAP_adapter_maps_authentication_failure()
    {
        await using var server = LoopbackImapServer.Start(
            [new SeededMailbox("INBOX", ["Inbox"], [])],
            rejectAuth: true);

        await using var client = new MailKitImapClientFactory().Create();

        await Assert.ThrowsAsync<ImapAuthenticationException>(async () =>
            await client.ConnectAndAuthenticateAsync(
                "127.0.0.1",
                server.Port,
                "alice@example.com",
                "wrong-password"));
    }

    [TestMethod]
    public async Task Real_IMAP_adapter_extracts_plain_text_from_HTML_only_Messages()
    {
        await using var server = LoopbackImapServer.Start(
        [
            new SeededMailbox(
                Path: "INBOX",
                Attributes: ["Inbox"],
                Messages:
                [
                    new SeededImapMessage(
                        Uid: 2,
                        Subject: "HTML only",
                        From: "bob@example.com",
                        InternalDate: new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
                        IsRead: true,
                        BodyText: string.Empty)
                    {
                        HtmlBody = "<p>Hello&nbsp;<b>world</b></p>",
                    },
                ]),
        ]);

        await using var client = new MailKitImapClientFactory().Create();
        await client.ConnectAndAuthenticateAsync(
            "127.0.0.1",
            server.Port,
            "alice@example.com",
            "s3cret-password");

        var messages = await client.FetchMessagesAsync("INBOX");
        Assert.HasCount(1, messages);
        Assert.AreEqual("Hello world", messages[0].BodyText);
        Assert.DoesNotContain("<", messages[0].BodyText, StringComparison.Ordinal);
    }
}
