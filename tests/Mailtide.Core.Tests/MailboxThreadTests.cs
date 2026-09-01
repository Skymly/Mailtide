using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class MailboxThreadTests
{
    [TestMethod]
    public async Task ListMailboxThreads_groups_reply_chain_and_keeps_Unified_Inbox_flat()
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
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Inbox);

        var threads = await app.ListMailboxThreadsAsync(account.Id, inbox.Id);
        var unified = await app.ListUnifiedInboxAsync();

        Assert.HasCount(1, threads);
        Assert.AreEqual("Re: Root", threads[0].Latest.Subject);
        Assert.HasCount(2, threads[0].Messages);
        Assert.HasCount(2, unified);
    }

    [TestMethod]
    public async Task ListMailboxThreads_singleton_when_Message_has_no_ids()
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
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var threads = await app.ListMailboxThreadsAsync(account.Id, inbox.Id);

        Assert.HasCount(1, threads);
        Assert.AreEqual("Alone", threads[0].Latest.Subject);
        Assert.HasCount(1, threads[0].Messages);
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
