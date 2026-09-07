using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class EditAsNewDraftTests
{
    [TestMethod]
    public async Task StartEditAsNew_copies_recipients_subject_and_body_without_reply_headers()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Sent", "Sent", MailboxRole.Sent));
        fixture.Imap.SeedMessages(
            "Sent",
            new RemoteMessage(
                RemoteId: "sent-1",
                Subject: "Invoice",
                FromAddress: "alice@example.com",
                ReceivedAt: new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "Please pay.")
            {
                ToAddresses = ["bob@example.com"],
                CcAddresses = ["carol@example.com"],
                BccAddresses = ["dave@example.com"],
                BodyHtml = "<p>Please pay.</p>",
                InternetMessageId = "<sent-1@example.com>",
            });

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var sent = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Role == MailboxRole.Sent);
        var message = (await app.ListMessagesAsync(account.Id, sent.Id)).Single();

        var draft = await app.StartEditAsNewAsync(account.Id, message.Id);

        CollectionAssert.AreEqual(new[] { "bob@example.com" }, draft.ToAddresses.ToArray());
        CollectionAssert.AreEqual(new[] { "carol@example.com" }, draft.CcAddresses.ToArray());
        CollectionAssert.AreEqual(new[] { "dave@example.com" }, draft.BccAddresses.ToArray());
        Assert.AreEqual("Invoice", draft.Subject);
        Assert.AreEqual("Please pay.", draft.BodyText);
        Assert.AreEqual("<p>Please pay.</p>", draft.BodyHtml);
        Assert.IsNull(draft.InReplyTo);
        Assert.IsEmpty(draft.References);
        Assert.IsFalse(draft.BodyText.Contains("wrote:", StringComparison.Ordinal));
        Assert.IsFalse(draft.Subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(draft.Subject.StartsWith("Fwd:", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task StartEditAsNew_copies_attachments_and_discard_keeps_originals()
    {
        var payload = "edit-as-new"u8.ToArray();
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "msg-attach",
                Subject: "Has file",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "See attached.")
            {
                ToAddresses = ["alice@example.com"],
                Attachments =
                [
                    new RemoteAttachment("notes.txt", "text/plain", payload),
                ],
            });

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;
        var message = (await app.ListMessagesAsync(account.Id, mailboxId)).Single();
        var original = (await app.ListAttachmentsAsync(account.Id, message.Id)).Single();

        var draft = await app.StartEditAsNewAsync(account.Id, message.Id);
        var copied = (await app.ListDraftAttachmentsAsync(account.Id, draft.Id)).Single();
        Assert.AreEqual("notes.txt", copied.FileName);
        Assert.AreNotEqual(original.Id, copied.Id);

        await app.DiscardDraftAsync(account.Id, draft.Id);
        var opened = await app.OpenAttachmentAsync(account.Id, original.Id);
        Assert.IsNotNull(opened);
        CollectionAssert.AreEqual(payload, opened.Content);
    }

    [TestMethod]
    public async Task StartEditAsNew_throws_when_Message_is_missing()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        var missingId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => app.StartEditAsNewAsync(account.Id, missingId));
        Assert.AreEqual($"Message '{missingId}' was not found.", ex.Message);
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
