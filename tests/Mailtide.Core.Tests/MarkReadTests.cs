using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class MarkReadTests
{
    [TestMethod]
    public async Task MarkRead_sets_local_IsRead_and_stores_Seen_on_IMAP()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "uid-7",
                Subject: "Please read me",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 4, 9, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "secret"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);

        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;
        var message = (await app.ListMessagesAsync(account.Id, mailboxId)).Single();
        Assert.IsFalse(message.IsRead);

        await app.MarkReadAsync(account.Id, message.Id);

        var reread = (await app.ListMessagesAsync(account.Id, mailboxId)).Single();
        Assert.IsTrue(reread.IsRead);
        Assert.AreEqual("INBOX", fixture.Imap.LastSetSeenMailboxPath);
        Assert.AreEqual("uid-7", fixture.Imap.LastSetSeenRemoteId);
    }

    [TestMethod]
    public async Task MarkRead_skips_IMAP_when_Message_is_already_read()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "uid-8",
                Subject: "Already read",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 4, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "old"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;
        var message = (await app.ListMessagesAsync(account.Id, mailboxId)).Single();

        await app.MarkReadAsync(account.Id, message.Id);

        Assert.IsNull(fixture.Imap.LastSetSeenRemoteId);
        Assert.IsTrue((await app.ListMessagesAsync(account.Id, mailboxId)).Single().IsRead);
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

