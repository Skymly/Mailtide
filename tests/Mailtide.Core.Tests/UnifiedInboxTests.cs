using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class UnifiedInboxTests
{
    [TestMethod]
    public async Task Unified_Inbox_returns_Messages_from_Inbox_role_Mailboxes_across_Accounts()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await fixture.OpenAppAsync();

        var accountA = await app.AddManualAccountAsync(Draft("Alice", "alice@example.com"));
        var accountB = await app.AddManualAccountAsync(Draft("Bob", "bob@example.com"));

        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Sent", "Sent", MailboxRole.Sent));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "a-1",
                Subject: "From Alice Inbox",
                FromAddress: "carol@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "a"));
        fixture.Imap.SeedMessages(
            "Sent",
            new RemoteMessage(
                RemoteId: "a-sent",
                Subject: "Alice Sent only",
                FromAddress: "alice@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 11, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "sent"));
        await app.SyncNowAsync(accountA.Id);

        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "b-1",
                Subject: "From Bob Inbox",
                FromAddress: "dave@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "b"));
        fixture.Imap.SeedMessages(
            "Sent",
            new RemoteMessage(
                RemoteId: "b-sent",
                Subject: "Bob Sent only",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 13, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "sent"));
        await app.SyncNowAsync(accountB.Id);

        var unified = await app.ListUnifiedInboxAsync();

        Assert.HasCount(2, unified);
        Assert.AreEqual("From Bob Inbox", unified[0].Subject);
        Assert.AreEqual(accountB.Id, unified[0].AccountId);
        Assert.AreEqual("From Alice Inbox", unified[1].Subject);
        Assert.AreEqual(accountA.Id, unified[1].AccountId);

        Assert.IsFalse(unified.Any(m => m.Subject.Contains("Sent", StringComparison.Ordinal)));

        var aliceInbox = (await app.ListMailboxesAsync(accountA.Id))
            .Single(m => m.Role == MailboxRole.Inbox);
        var bobInbox = (await app.ListMailboxesAsync(accountB.Id))
            .Single(m => m.Role == MailboxRole.Inbox);

        Assert.AreEqual(aliceInbox.Id, unified.Single(m => m.AccountId == accountA.Id).MailboxId);
        Assert.AreEqual(bobInbox.Id, unified.Single(m => m.AccountId == accountB.Id).MailboxId);

        var aliceInboxMessages = await app.ListMessagesAsync(accountA.Id, aliceInbox.Id);
        var bobInboxMessages = await app.ListMessagesAsync(accountB.Id, bobInbox.Id);
        CollectionAssert.AreEquivalent(
            aliceInboxMessages.Concat(bobInboxMessages).Select(m => m.Id).ToArray(),
            unified.Select(m => m.Id).ToArray());
    }

    [TestMethod]
    public async Task Unified_Inbox_is_a_query_view_not_a_persisted_Mailbox()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Sent", "Sent", MailboxRole.Sent));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-1",
                Subject: "Only Inbox",
                FromAddress: "x@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 2, 9, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "body"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(Draft("Alice", "alice@example.com"));
        await app.SyncNowAsync(account.Id);

        var unified = await app.ListUnifiedInboxAsync();
        Assert.HasCount(1, unified);

        var mailboxes = await app.ListMailboxesAsync(account.Id);
        Assert.HasCount(2, mailboxes);
        Assert.IsFalse(
            mailboxes.Any(m =>
                m.Name.Contains("Unified", StringComparison.OrdinalIgnoreCase)
                || m.Path.Contains("Unified", StringComparison.OrdinalIgnoreCase)));

        var inbox = mailboxes.Single(m => m.Role == MailboxRole.Inbox);
        var inboxMessages = await app.ListMessagesAsync(account.Id, inbox.Id);
        Assert.HasCount(1, inboxMessages);
        Assert.AreEqual(inboxMessages[0].Id, unified[0].Id);
        Assert.AreEqual(account.Id, unified[0].AccountId);
        Assert.AreEqual(inbox.Id, unified[0].MailboxId);
    }

    private static ManualAccountDraft Draft(string displayName, string email) =>
        new(
            DisplayName: displayName,
            EmailAddress: email,
            ImapHost: "imap.example.com",
            ImapPort: 993,
            SmtpHost: "smtp.example.com",
            SmtpPort: 587,
            Password: "s3cret-password");
}
