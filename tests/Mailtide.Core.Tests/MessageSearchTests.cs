using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class MessageSearchTests
{
    [TestMethod]
    public void Parse_strips_flagged_and_unread_operators()
    {
        var (flaggedOnly, unreadOnly, text) = MessageSearch.Parse("is:flagged invoice");
        Assert.IsTrue(flaggedOnly);
        Assert.IsFalse(unreadOnly);
        Assert.AreEqual("invoice", text);

        (flaggedOnly, unreadOnly, text) = MessageSearch.Parse("IS:UNREAD keep");
        Assert.IsFalse(flaggedOnly);
        Assert.IsTrue(unreadOnly);
        Assert.AreEqual("keep", text);

        (flaggedOnly, unreadOnly, text) = MessageSearch.Parse("is:unread is:flagged");
        Assert.IsTrue(flaggedOnly);
        Assert.IsTrue(unreadOnly);
        Assert.AreEqual(string.Empty, text);
    }

    [TestMethod]
    public void Matches_unread_operator_ignores_read()
    {
        Assert.IsTrue(MessageSearch.Matches(false, false, "Invoice", "a@b.com", "pay", null, "is:unread"));
        Assert.IsFalse(MessageSearch.Matches(false, true, "Invoice", "a@b.com", "pay", null, "is:unread"));
        Assert.IsTrue(MessageSearch.Matches(true, false, "Invoice", "a@b.com", "pay", null, "is:unread is:flagged"));
        Assert.IsFalse(MessageSearch.Matches(false, false, "Invoice", "a@b.com", "pay", null, "is:unread is:flagged"));
        Assert.IsTrue(MessageSearch.Matches(false, true, "Invoice", "a@b.com", "pay", null, "invoice"));
    }

    [TestMethod]
    public async Task SearchMessages_is_unread_filters_current_Mailbox()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            Message("1", "New", flagged: false, read: false, body: "keep"),
            Message("2", "Old", flagged: false, read: true, body: "keep"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;

        var unread = await app.SearchMessagesAsync(account.Id, mailboxId, "is:unread");
        Assert.AreEqual("New", unread.Single().Subject);

        var keep = await app.SearchMessagesAsync(account.Id, mailboxId, "keep");
        Assert.HasCount(2, keep);
    }

    [TestMethod]
    public async Task SearchMessages_is_flagged_filters_current_Mailbox()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            Message("1", "Star me", flagged: true, body: "keep"),
            Message("2", "Ordinary", flagged: false, body: "keep"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;

        var flagged = await app.SearchMessagesAsync(account.Id, mailboxId, "is:flagged");
        Assert.AreEqual("Star me", flagged.Single().Subject);

        var flaggedKeep = await app.SearchMessagesAsync(account.Id, mailboxId, "is:flagged keep");
        Assert.AreEqual("Star me", flaggedKeep.Single().Subject);

        var keep = await app.SearchMessagesAsync(account.Id, mailboxId, "keep");
        Assert.HasCount(2, keep);
    }

    private static RemoteMessage Message(
        string remoteId,
        string subject,
        bool flagged,
        bool read = true,
        string body = "body") =>
        new(
            RemoteId: remoteId,
            Subject: subject,
            FromAddress: "bob@example.com",
            ReceivedAt: new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero),
            IsRead: read,
            BodyText: body)
        {
            IsFlagged = flagged,
        };

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
