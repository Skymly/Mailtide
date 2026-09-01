using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class UnifiedInboxThreadTests
{
    [TestMethod]
    public async Task ListUnifiedInboxThreads_groups_reply_chain()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-1",
                Subject: "Root",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "root")
            {
                InternetMessageId = "<root@example.com>",
            },
            new RemoteMessage(
                RemoteId: "m-2",
                Subject: "Re: Root",
                FromAddress: "alice@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 11, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "reply")
            {
                InternetMessageId = "<reply@example.com>",
                References = ["<root@example.com>"],
            });
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);

        var threads = await app.ListUnifiedInboxThreadsAsync();
        var unified = await app.ListUnifiedInboxAsync();

        Assert.HasCount(1, threads);
        Assert.AreEqual("Re: Root", threads[0].Latest.Subject);
        Assert.HasCount(2, threads[0].Messages);
        Assert.HasCount(2, unified);
    }

    [TestMethod]
    public async Task ListUnifiedInboxThreads_singleton_when_Message_has_no_ids()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-1",
                Subject: "Alone",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "hi"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);

        var threads = await app.ListUnifiedInboxThreadsAsync();

        Assert.HasCount(1, threads);
        Assert.AreEqual("Alone", threads[0].Latest.Subject);
        Assert.HasCount(1, threads[0].Messages);
    }

    [TestMethod]
    public async Task ListUnifiedInboxThreads_groups_reply_chain_across_Accounts()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        await using var app = await fixture.OpenAppAsync();

        var accountA = await app.AddManualAccountAsync(ValidDraft("Alice", "alice@example.com"));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "a-1",
                Subject: "Root",
                FromAddress: "carol@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "root")
            {
                InternetMessageId = "<root@example.com>",
            });
        await app.SyncNowAsync(accountA.Id);

        var accountB = await app.AddManualAccountAsync(ValidDraft("Bob", "bob@example.com"));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "b-1",
                Subject: "Re: Root",
                FromAddress: "dave@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "reply")
            {
                InternetMessageId = "<reply@example.com>",
                References = ["<root@example.com>"],
            });
        await app.SyncNowAsync(accountB.Id);

        var threads = await app.ListUnifiedInboxThreadsAsync();

        Assert.HasCount(1, threads);
        Assert.AreEqual("Re: Root", threads[0].Latest.Subject);
        Assert.HasCount(2, threads[0].Messages);
        Assert.AreEqual(accountB.Id, threads[0].Latest.AccountId);
        CollectionAssert.AreEquivalent(
            new[] { accountA.Id, accountB.Id },
            threads[0].Messages.Select(m => m.AccountId).Distinct().ToArray());
    }

    private static ManualAccountDraft ValidDraft(
        string displayName = "Personal",
        string email = "alice@example.com") =>
        new(
            DisplayName: displayName,
            EmailAddress: email,
            ImapHost: "imap.example.com",
            ImapPort: 993,
            SmtpHost: "smtp.example.com",
            SmtpPort: 587,
            Password: "s3cret-password");
}
