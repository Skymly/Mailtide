using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class HtmlBodyTests
{
    [TestMethod]
    public async Task SyncNow_stores_BodyHtml_readable_offline()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "html-1",
                Subject: "Hello html",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "Hello world")
            {
                BodyHtml = "<p>Hello <b>world</b></p>",
            });

        Guid accountId;
        Guid mailboxId;
        Guid messageId;

        await using (var app = await fixture.OpenAppAsync())
        {
            var account = await app.AddManualAccountAsync(ValidDraft());
            accountId = account.Id;
            await app.SyncNowAsync(accountId);

            mailboxId = (await app.ListMailboxesAsync(accountId)).Single().Id;
            messageId = (await app.ListMessagesAsync(accountId, mailboxId)).Single().Id;

            Assert.AreEqual("<p>Hello <b>world</b></p>", await app.GetMessageHtmlAsync(accountId, messageId));
            Assert.AreEqual("Hello world", await app.GetMessageBodyAsync(accountId, messageId));
        }

        fixture.Imap.SeedMailboxes();
        fixture.Imap.ClearMessages();

        await using (var restarted = await fixture.OpenAppAsync())
        {
            Assert.AreEqual(
                "<p>Hello <b>world</b></p>",
                await restarted.GetMessageHtmlAsync(accountId, messageId));
            Assert.AreEqual("Hello world", await restarted.GetMessageBodyAsync(accountId, messageId));
        }
    }

    [TestMethod]
    public async Task GetMessageBodyAsync_falls_back_to_stripped_HTML()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "html-only",
                Subject: "Hello html",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "")
            {
                BodyHtml = "<p>Hello <b>world</b></p>",
            });

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;
        var message = (await app.ListMessagesAsync(account.Id, mailboxId)).Single();

        Assert.AreEqual("Hello world", await app.GetMessageBodyAsync(account.Id, message.Id));
        Assert.AreEqual("Hello world", message.Preview);
    }

    [TestMethod]
    public async Task GetMessageHtmlAsync_returns_null_when_Message_has_only_BodyText()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "text-1",
                Subject: "Plain",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "just text"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;
        var messageId = (await app.ListMessagesAsync(account.Id, mailboxId)).Single().Id;

        Assert.IsNull(await app.GetMessageHtmlAsync(account.Id, messageId));
        Assert.AreEqual("just text", await app.GetMessageBodyAsync(account.Id, messageId));
    }

    [TestMethod]
    public async Task Second_SyncNow_keeps_BodyHtml_without_refetching()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "html-keep",
                Subject: "Keep html",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "Keep")
            {
                BodyHtml = "<p>Keep</p>",
            });

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        fixture.Imap.FetchedRemoteIds.Clear();

        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "html-keep",
                Subject: "Keep html",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "changed-text")
            {
                BodyHtml = "<p>changed</p>",
            });
        await app.SyncNowAsync(account.Id);

        Assert.AreEqual(0, fixture.Imap.FetchedRemoteIds.Count);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;
        var message = (await app.ListMessagesAsync(account.Id, mailboxId)).Single();
        Assert.IsTrue(message.IsRead);
        Assert.AreEqual("<p>Keep</p>", await app.GetMessageHtmlAsync(account.Id, message.Id));
        Assert.AreEqual("Keep", await app.GetMessageBodyAsync(account.Id, message.Id));
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
