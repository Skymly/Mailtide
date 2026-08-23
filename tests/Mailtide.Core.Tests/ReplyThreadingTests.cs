using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class ReplyThreadingTests
{
    [TestMethod]
    public async Task StartReply_sets_InReplyTo_and_References_from_stored_Message()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "uid-thread",
                Subject: "Team thread",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 2, 11, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "please reply")
            {
                InternetMessageId = "<orig@example.com>",
                References = ["<root@example.com>"],
            });

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var message = (await app.ListUnifiedInboxAsync()).Single();

        var draft = await app.StartReplyAsync(account.Id, message.Id);

        Assert.AreEqual("<orig@example.com>", draft.InReplyTo);
        CollectionAssert.AreEqual(
            new[] { "<root@example.com>", "<orig@example.com>" },
            draft.References.ToArray());
    }

    [TestMethod]
    public async Task SendNow_submits_InReplyTo_and_References()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "uid-send-thread",
                Subject: "Team thread",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 2, 11, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "please reply")
            {
                InternetMessageId = "<orig@example.com>",
                References = ["<root@example.com>"],
            });

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var message = (await app.ListUnifiedInboxAsync()).Single();
        var draft = await app.StartReplyAsync(account.Id, message.Id);
        await app.SendAsync(account.Id, draft.Id);
        await app.SendNowAsync(account.Id);

        Assert.HasCount(1, fixture.Smtp.Submitted);
        Assert.AreEqual("<orig@example.com>", fixture.Smtp.Submitted[0].InReplyTo);
        CollectionAssert.AreEqual(
            new[] { "<root@example.com>", "<orig@example.com>" },
            fixture.Smtp.Submitted[0].References.ToArray());
    }

    [TestMethod]
    public async Task Second_SyncNow_keeps_InternetMessageId_without_refetching()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "uid-keep",
                Subject: "Keep",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 2, 11, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "keep")
            {
                InternetMessageId = "<keep@example.com>",
            });

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        fixture.Imap.FetchedRemoteIds.Clear();
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "uid-keep",
                Subject: "Keep",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 2, 11, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "changed")
            {
                InternetMessageId = "<other@example.com>",
            });
        await app.SyncNowAsync(account.Id);

        Assert.AreEqual(0, fixture.Imap.FetchedRemoteIds.Count);
        var message = (await app.ListUnifiedInboxAsync()).Single();
        var draft = await app.StartReplyAsync(account.Id, message.Id);
        Assert.AreEqual("<keep@example.com>", draft.InReplyTo);
    }

    private static ManualAccountDraft ValidDraft() =>
        new(
            DisplayName: "Personal",
            EmailAddress: "alice@example.com",
            ImapHost: "imap.example.com",
            ImapPort: 993,
            SmtpHost: "smtp.example.com",
            SmtpPort: 587,
            Password: "s3cret-password");
}
