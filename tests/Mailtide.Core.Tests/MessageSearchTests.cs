using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class MessageSearchTests
{
    [TestMethod]
    public void Parse_strips_flagged_operator()
    {
        var (flaggedOnly, text) = MessageSearch.Parse("is:flagged invoice");
        Assert.IsTrue(flaggedOnly);
        Assert.AreEqual("invoice", text);

        (flaggedOnly, text) = MessageSearch.Parse("IS:FLAGGED");
        Assert.IsTrue(flaggedOnly);
        Assert.AreEqual(string.Empty, text);

        (flaggedOnly, text) = MessageSearch.Parse("hello world");
        Assert.IsFalse(flaggedOnly);
        Assert.AreEqual("hello world", text);
    }

    [TestMethod]
    public void Matches_flagged_operator_ignores_unflagged()
    {
        Assert.IsTrue(MessageSearch.Matches(true, "Invoice", "a@b.com", "pay", null, "is:flagged"));
        Assert.IsFalse(MessageSearch.Matches(false, "Invoice", "a@b.com", "pay", null, "is:flagged"));
        Assert.IsTrue(MessageSearch.Matches(true, "Invoice", "a@b.com", "pay now", null, "is:flagged pay"));
        Assert.IsFalse(MessageSearch.Matches(true, "Hello", "a@b.com", "x", null, "is:flagged invoice"));
        Assert.IsTrue(MessageSearch.Matches(false, "Invoice", "a@b.com", "pay", null, "invoice"));
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

    [TestMethod]
    public async Task SearchUnifiedInbox_is_flagged_filters_across_Accounts()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages("INBOX", Message("1", "Alice flagged", flagged: true));

        await using var app = await fixture.OpenAppAsync();
        var alice = await app.AddManualAccountAsync(ValidDraft("Alice", "alice@example.com"));
        await app.SyncNowAsync(alice.Id);

        fixture.Imap.SeedMessages("INBOX", Message("2", "Bob plain", flagged: false));
        var bob = await app.AddManualAccountAsync(ValidDraft("Bob", "bob@example.com"));
        await app.SyncNowAsync(bob.Id);

        var hits = await app.SearchUnifiedInboxAsync("is:flagged");
        Assert.AreEqual("Alice flagged", hits.Single().Subject);
        Assert.AreEqual(alice.Id, hits.Single().AccountId);
    }

    private static RemoteMessage Message(
        string remoteId,
        string subject,
        bool flagged,
        string body = "body") =>
        new(
            RemoteId: remoteId,
            Subject: subject,
            FromAddress: "bob@example.com",
            ReceivedAt: new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero),
            IsRead: true,
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
