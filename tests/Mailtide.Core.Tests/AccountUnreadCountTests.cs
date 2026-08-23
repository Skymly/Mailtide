using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class AccountUnreadCountTests
{
    [TestMethod]
    public async Task ListAccounts_counts_unread_Messages_per_Account()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            Message("u1", isRead: false),
            Message("u2", isRead: false),
            Message("r1", isRead: true));

        await using var app = await fixture.OpenAppAsync();
        var alice = await app.AddManualAccountAsync(ValidDraft("Alice", "alice@example.com"));
        await app.SyncNowAsync(alice.Id);

        fixture.Imap.SeedMessages("INBOX", Message("b1", isRead: true));
        var bob = await app.AddManualAccountAsync(ValidDraft("Bob", "bob@example.com"));
        await app.SyncNowAsync(bob.Id);

        var accounts = await app.ListAccountsAsync();
        Assert.AreEqual(2, accounts.Single(a => a.Id == alice.Id).UnreadCount);
        Assert.AreEqual(0, accounts.Single(a => a.Id == bob.Id).UnreadCount);
    }

    [TestMethod]
    public async Task MarkRead_decrements_Account_unread_count()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages("INBOX", Message("u1", isRead: false));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Alice", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();
        var message = (await app.ListMessagesAsync(account.Id, inbox.Id)).Single();
        Assert.AreEqual(1, (await app.ListAccountsAsync()).Single().UnreadCount);

        await app.MarkReadAsync(account.Id, message.Id);

        Assert.AreEqual(0, (await app.ListAccountsAsync()).Single().UnreadCount);
    }

    private static RemoteMessage Message(string remoteId, bool isRead) =>
        new(
            RemoteId: remoteId,
            Subject: remoteId,
            FromAddress: "bob@example.com",
            ReceivedAt: new DateTimeOffset(2026, 8, 7, 9, 0, 0, TimeSpan.Zero),
            IsRead: isRead,
            BodyText: "body");

    private static ManualAccountDraft ValidDraft(string displayName, string email) =>
        new(
            DisplayName: displayName,
            EmailAddress: email,
            ImapHost: "imap.example.com",
            ImapPort: 993,
            SmtpHost: "smtp.example.com",
            SmtpPort: 587,
            Password: "s3cret-password");
}
