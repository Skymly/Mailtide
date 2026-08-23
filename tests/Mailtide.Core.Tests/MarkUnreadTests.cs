using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class MarkUnreadTests
{
    [TestMethod]
    public async Task MarkUnread_clears_local_IsRead_and_IMAP_Seen()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "uid-9",
                Subject: "Put me back",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 9, 9, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "old"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;
        var message = (await app.ListMessagesAsync(account.Id, mailboxId)).Single();

        await app.MarkUnreadAsync(account.Id, message.Id);

        Assert.IsFalse((await app.ListMessagesAsync(account.Id, mailboxId)).Single().IsRead);
        Assert.AreEqual("INBOX", fixture.Imap.LastSetUnseenMailboxPath);
        Assert.AreEqual("uid-9", fixture.Imap.LastSetUnseenRemoteId);
    }

    [TestMethod]
    public async Task MarkUnread_skips_IMAP_when_Message_is_already_unread()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "uid-10",
                Subject: "Still new",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 9, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "new"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;
        var message = (await app.ListMessagesAsync(account.Id, mailboxId)).Single();

        await app.MarkUnreadAsync(account.Id, message.Id);

        Assert.IsNull(fixture.Imap.LastSetUnseenRemoteId);
        Assert.IsFalse((await app.ListMessagesAsync(account.Id, mailboxId)).Single().IsRead);
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
