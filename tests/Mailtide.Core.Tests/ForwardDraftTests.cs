using System.Globalization;
using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class ForwardDraftTests
{
    [TestMethod]
    public async Task StartForward_creates_local_Draft_with_empty_To_and_forwarded_body()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "msg-1",
                Subject: "Hello offline",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "Body stays local."));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;
        var message = (await app.ListMessagesAsync(account.Id, mailboxId)).Single();

        var draft = await app.StartForwardAsync(account.Id, message.Id);

        Assert.AreEqual(account.Id, draft.AccountId);
        Assert.IsEmpty(draft.ToAddresses);
        Assert.AreEqual("Fwd: Hello offline", draft.Subject);
        StringAssert.Contains(draft.BodyText, "---------- Forwarded Message ----------");
        StringAssert.Contains(draft.BodyText, "From: bob@example.com");
        StringAssert.Contains(
            draft.BodyText,
            "Date: " + new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        StringAssert.Contains(draft.BodyText, "Subject: Hello offline");
        StringAssert.Contains(draft.BodyText, "Body stays local.");
        Assert.IsFalse(draft.BodyText.Contains(" UTC", StringComparison.Ordinal));
        Assert.AreEqual(0, fixture.Smtp.Submitted.Count);
    }

    [TestMethod]
    public async Task StartForward_does_not_double_existing_Fwd_prefix()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "msg-2",
                Subject: "FWD: Already forwarded",
                FromAddress: "carol@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 2, 15, 30, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "payload"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var message = (await app.ListMessagesAsync(
            account.Id,
            (await app.ListMailboxesAsync(account.Id)).Single().Id)).Single();

        var draft = await app.StartForwardAsync(account.Id, message.Id);
        Assert.AreEqual("FWD: Already forwarded", draft.Subject);
        Assert.IsEmpty(draft.ToAddresses);
    }

    [TestMethod]
    public async Task StartForward_throws_when_Message_is_missing()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        var missingId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");
        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => app.StartForwardAsync(account.Id, missingId));
        Assert.AreEqual($"Message '{missingId}' was not found.", ex.Message);
    }

    [TestMethod]
    public async Task StartForward_copies_Message_attachments_onto_the_Draft()
    {
        var payload = "forward-me"u8.ToArray();
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "msg-attach",
                Subject: "Has file",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "See attached.")
            {
                Attachments =
                [
                    new RemoteAttachment(
                        FileName: "report.pdf",
                        ContentType: "application/pdf",
                        Content: payload),
                ],
            });

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;
        var message = (await app.ListMessagesAsync(account.Id, mailboxId)).Single();
        var original = (await app.ListAttachmentsAsync(account.Id, message.Id)).Single();

        var draft = await app.StartForwardAsync(account.Id, message.Id);
        var copied = (await app.ListDraftAttachmentsAsync(account.Id, draft.Id)).Single();

        Assert.AreEqual("report.pdf", copied.FileName);
        Assert.AreEqual("application/pdf", copied.ContentType);
        Assert.AreNotEqual(original.Id, copied.Id);

        await app.DiscardDraftAsync(account.Id, draft.Id);
        Assert.IsEmpty(await app.ListDraftAttachmentsAsync(account.Id, draft.Id));
        var opened = await app.OpenAttachmentAsync(account.Id, original.Id);
        Assert.IsNotNull(opened);
        CollectionAssert.AreEqual(payload, opened.Content);
    }

    [TestMethod]
    public async Task StartForwardAsAttachment_attaches_eml_without_quoting_the_body()
    {
        var payload = "keep-me"u8.ToArray();
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "msg-eml",
                Subject: "Has file",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "See attached.")
            {
                Attachments =
                [
                    new RemoteAttachment(
                        FileName: "report.pdf",
                        ContentType: "application/pdf",
                        Content: payload),
                ],
            });

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;
        var message = (await app.ListMessagesAsync(account.Id, mailboxId)).Single();
        var original = (await app.ListAttachmentsAsync(account.Id, message.Id)).Single();

        var draft = await app.StartForwardAsAttachmentAsync(account.Id, message.Id);
        var attached = (await app.ListDraftAttachmentsAsync(account.Id, draft.Id)).Single();

        Assert.IsEmpty(draft.ToAddresses);
        Assert.AreEqual("Fwd: Has file", draft.Subject);
        Assert.IsNull(draft.InReplyTo);
        Assert.IsFalse(draft.BodyText.Contains("Forwarded Message", StringComparison.Ordinal));
        Assert.AreEqual("Has file.eml", attached.FileName);
        Assert.AreEqual("message/rfc822", attached.ContentType);

        await app.DiscardDraftAsync(account.Id, draft.Id);
        Assert.IsEmpty(await app.ListDraftAttachmentsAsync(account.Id, draft.Id));
        var opened = await app.OpenAttachmentAsync(account.Id, original.Id);
        Assert.IsNotNull(opened);
        CollectionAssert.AreEqual(payload, opened.Content);
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
