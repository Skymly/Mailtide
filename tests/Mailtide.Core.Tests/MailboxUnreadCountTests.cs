using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class MailboxUnreadCountTests
{
    [TestMethod]
    public async Task ListMailboxes_counts_unread_Messages_per_Mailbox()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Sent", "Sent", MailboxRole.Sent));
        fixture.Imap.SeedMessages(
            "INBOX",
            Message("u1", "Unread one", isRead: false),
            Message("u2", "Unread two", isRead: false),
            Message("r1", "Read", isRead: true));
        fixture.Imap.SeedMessages(
            "Sent",
            Message("s1", "Sent read", isRead: true));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);

        var mailboxes = await app.ListMailboxesAsync(account.Id);
        Assert.AreEqual(2, mailboxes.Single(m => m.Name == "INBOX").UnreadCount);
        Assert.AreEqual(0, mailboxes.Single(m => m.Name == "Sent").UnreadCount);
    }

    [TestMethod]
    public async Task MarkRead_decrements_Mailbox_unread_count()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages("INBOX", Message("u1", "Unread", isRead: false));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();
        var message = (await app.ListMessagesAsync(account.Id, inbox.Id)).Single();
        Assert.AreEqual(1, inbox.UnreadCount);

        await app.MarkReadAsync(account.Id, message.Id);

        var after = (await app.ListMailboxesAsync(account.Id)).Single();
        Assert.AreEqual(0, after.UnreadCount);
    }

    private static RemoteMessage Message(string remoteId, string subject, bool isRead) =>
        new(
            RemoteId: remoteId,
            Subject: subject,
            FromAddress: "bob@example.com",
            ReceivedAt: new DateTimeOffset(2026, 8, 4, 9, 0, 0, TimeSpan.Zero),
            IsRead: isRead,
            BodyText: "body");

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
