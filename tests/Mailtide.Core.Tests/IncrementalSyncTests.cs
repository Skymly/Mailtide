using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class IncrementalSyncTests
{
    [TestMethod]
    public async Task Second_SyncNow_keeps_local_Message_and_Mailbox_ids()
    {
        using var fixture = new CoreAppFixture();
        SeedInbox(fixture, Message("uid-1", "Hello", isRead: false, "first"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);

        var mailbox = (await app.ListMailboxesAsync(account.Id)).Single();
        var message = (await app.ListMessagesAsync(account.Id, mailbox.Id)).Single();

        await app.SyncNowAsync(account.Id);

        var mailboxAgain = (await app.ListMailboxesAsync(account.Id)).Single();
        var messageAgain = (await app.ListMessagesAsync(account.Id, mailboxAgain.Id)).Single();
        Assert.AreEqual(mailbox.Id, mailboxAgain.Id);
        Assert.AreEqual(message.Id, messageAgain.Id);
        Assert.AreEqual("Hello", messageAgain.Subject);
    }

    [TestMethod]
    public async Task SyncNow_inserts_new_RemoteId_and_removes_vanished_one()
    {
        using var fixture = new CoreAppFixture();
        SeedInbox(fixture, Message("uid-1", "Keep", isRead: false, "a"), Message("uid-2", "Gone", isRead: false, "b"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;
        var first = (await app.ListMessagesAsync(account.Id, mailboxId)).Single(m => m.RemoteId == "uid-1");

        fixture.Imap.SeedMessages(
            "INBOX",
            Message("uid-1", "Keep", isRead: false, "a"),
            Message("uid-3", "New", isRead: false, "c"));
        await app.SyncNowAsync(account.Id);

        var messages = await app.ListMessagesAsync(account.Id, mailboxId);
        Assert.HasCount(2, messages);
        Assert.AreEqual(first.Id, messages.Single(m => m.RemoteId == "uid-1").Id);
        Assert.IsTrue(messages.Any(m => m.RemoteId == "uid-3" && m.Subject == "New"));
        Assert.IsFalse(messages.Any(m => m.RemoteId == "uid-2"));
    }

    [TestMethod]
    public async Task SyncNow_updates_flags_and_headers_on_existing_RemoteId()
    {
        using var fixture = new CoreAppFixture();
        SeedInbox(fixture, Message("uid-1", "Old subject", isRead: false, "body"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;
        var originalId = (await app.ListMessagesAsync(account.Id, mailboxId)).Single().Id;

        fixture.Imap.SeedMessages("INBOX", Message("uid-1", "New subject", isRead: true, "body"));
        await app.SyncNowAsync(account.Id);

        var updated = (await app.ListMessagesAsync(account.Id, mailboxId)).Single();
        Assert.AreEqual(originalId, updated.Id);
        Assert.AreEqual("New subject", updated.Subject);
        Assert.IsTrue(updated.IsRead);
    }

    [TestMethod]
    public async Task Second_SyncNow_keeps_Attachment_id_and_bytes()
    {
        using var fixture = new CoreAppFixture();
        var payload = "stable-blob"u8.ToArray();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "uid-att",
                Subject: "File",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "see file")
            {
                Attachments = [new RemoteAttachment("a.txt", "text/plain", payload)],
            });

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;
        var messageId = (await app.ListMessagesAsync(account.Id, mailboxId)).Single().Id;
        var attachment = (await app.ListAttachmentsAsync(account.Id, messageId)).Single();

        await app.SyncNowAsync(account.Id);

        var attachmentAgain = (await app.ListAttachmentsAsync(account.Id, messageId)).Single();
        Assert.AreEqual(attachment.Id, attachmentAgain.Id);
        var opened = await app.OpenAttachmentAsync(account.Id, attachment.Id);
        Assert.IsNotNull(opened);
        CollectionAssert.AreEqual(payload, opened.Content);
    }

    [TestMethod]
    public async Task Second_SyncNow_fetches_bodies_only_for_unknown_RemoteIds()
    {
        using var fixture = new CoreAppFixture();
        SeedInbox(fixture, Message("uid-1", "Hello", isRead: false, "first"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        fixture.Imap.FetchedRemoteIds.Clear();

        fixture.Imap.SeedMessages(
            "INBOX",
            Message("uid-1", "Hello", isRead: true, "first"),
            Message("uid-2", "Newer", isRead: false, "second"));
        await app.SyncNowAsync(account.Id);

        CollectionAssert.AreEqual(new[] { "uid-2" }, fixture.Imap.FetchedRemoteIds.ToArray());
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;
        var messages = await app.ListMessagesAsync(account.Id, mailboxId);
        Assert.HasCount(2, messages);
        Assert.IsTrue(messages.Single(m => m.RemoteId == "uid-1").IsRead);
        Assert.AreEqual("Newer", messages.Single(m => m.RemoteId == "uid-2").Subject);
        Assert.AreEqual("first", await app.GetMessageBodyAsync(account.Id, messages.Single(m => m.RemoteId == "uid-1").Id));
        Assert.AreEqual("second", await app.GetMessageBodyAsync(account.Id, messages.Single(m => m.RemoteId == "uid-2").Id));
    }
    private static void SeedInbox(CoreAppFixture fixture, params RemoteMessage[] messages)
    {
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages("INBOX", messages);
    }

    private static RemoteMessage Message(string remoteId, string subject, bool isRead, string body) =>
        new(
            RemoteId: remoteId,
            Subject: subject,
            FromAddress: "bob@example.com",
            ReceivedAt: new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero),
            IsRead: isRead,
            BodyText: body);

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
