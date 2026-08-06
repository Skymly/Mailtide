using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class SyncMessageTests
{
    [TestMethod]
    public async Task SyncNow_stores_Message_metadata_and_bodies_readable_offline()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "msg-1",
                Subject: "Hello offline",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "Body stays local."));

        Guid accountId;
        Guid mailboxId;
        Guid messageId;

        await using (var app = await fixture.OpenAppAsync())
        {
            var account = await app.AddManualAccountAsync(ValidDraft());
            accountId = account.Id;
            await app.SyncNowAsync(accountId);

            var mailboxes = await app.ListMailboxesAsync(accountId);
            mailboxId = mailboxes.Single().Id;

            var messages = await app.ListMessagesAsync(accountId, mailboxId);
            Assert.HasCount(1, messages);
            Assert.AreEqual("Hello offline", messages[0].Subject);
            Assert.AreEqual("bob@example.com", messages[0].FromAddress);
            Assert.AreEqual(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero), messages[0].ReceivedAt);
            messageId = messages[0].Id;

            var body = await app.GetMessageBodyAsync(accountId, messageId);
            Assert.AreEqual("Body stays local.", body);
        }

        // Simulate offline: protocol port has nothing; store still serves content.
        fixture.Imap.SeedMailboxes();
        fixture.Imap.ClearMessages();

        await using (var restarted = await fixture.OpenAppAsync())
        {
            var messages = await restarted.ListMessagesAsync(accountId, mailboxId);
            Assert.HasCount(1, messages);
            Assert.AreEqual(messageId, messages[0].Id);
            Assert.AreEqual("Hello offline", messages[0].Subject);

            var body = await restarted.GetMessageBodyAsync(accountId, messageId);
            Assert.AreEqual("Body stays local.", body);
        }
    }

    [TestMethod]
    public async Task SyncNow_reflects_unread_and_read_flags_locally()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "unread-1",
                Subject: "Unread",
                FromAddress: "a@example.com",
                ReceivedAt: new DateTimeOffset(2026, 3, 2, 9, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "new"),
            new RemoteMessage(
                RemoteId: "read-1",
                Subject: "Read",
                FromAddress: "b@example.com",
                ReceivedAt: new DateTimeOffset(2026, 3, 2, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "old"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);

        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;
        var messages = await app.ListMessagesAsync(account.Id, mailboxId);

        Assert.IsFalse(messages.Single(m => m.Subject == "Unread").IsRead);
        Assert.IsTrue(messages.Single(m => m.Subject == "Read").IsRead);
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
