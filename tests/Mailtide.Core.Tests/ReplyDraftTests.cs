using System.Globalization;
using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class ReplyDraftTests
{
    [TestMethod]
    public async Task StartReply_creates_local_Draft_addressed_to_sender_with_quoted_body()
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

        var draft = await app.StartReplyAsync(account.Id, message.Id);

        Assert.AreEqual(account.Id, draft.AccountId);
        CollectionAssert.AreEqual(new[] { "bob@example.com" }, draft.ToAddresses.ToArray());
        Assert.AreEqual("Re: Hello offline", draft.Subject);
        StringAssert.Contains(
            draft.BodyText.ReplaceLineEndings("\n"),
            "On " + LocalWhen(2026, 4, 1, 10, 0) + ", bob@example.com wrote:");
        StringAssert.Contains(draft.BodyText, "> Body stays local.");
        Assert.IsFalse(draft.BodyText.Contains(" UTC,", StringComparison.Ordinal));

        var drafts = await app.ListDraftsAsync(account.Id);
        Assert.HasCount(1, drafts);
        Assert.AreEqual(draft.Id, drafts[0].Id);
        Assert.AreEqual(0, fixture.Smtp.Submitted.Count);
    }

    [TestMethod]
    public async Task StartReply_quotes_only_the_provided_excerpt()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "msg-sel",
                Subject: "Hello",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "Keep this paragraph.\nIgnore the rest."));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var message = (await app.ListMessagesAsync(
            account.Id,
            (await app.ListMailboxesAsync(account.Id)).Single().Id)).Single();

        var draft = await app.StartReplyAsync(account.Id, message.Id, "Keep this paragraph.");
        StringAssert.Contains(draft.BodyText, "> Keep this paragraph.");
        Assert.IsFalse(draft.BodyText.Contains("Ignore the rest", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task StartReply_does_not_double_existing_Re_prefix()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "msg-2",
                Subject: "RE: Already a reply",
                FromAddress: "carol@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 2, 15, 30, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "line one" + "\n" + "line two"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);

        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;
        var message = (await app.ListMessagesAsync(account.Id, mailboxId)).Single();

        var draft = await app.StartReplyAsync(account.Id, message.Id);

        Assert.AreEqual("RE: Already a reply", draft.Subject);
        StringAssert.Contains(
            draft.BodyText.ReplaceLineEndings("\n"),
            "On " + LocalWhen(2026, 4, 2, 15, 30) + ", carol@example.com wrote:");
        StringAssert.Contains(draft.BodyText.ReplaceLineEndings("\n"), "> line one" + "\n" + "> line two");
        Assert.IsFalse(draft.BodyText.Contains(" UTC,", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task StartReply_quotes_HTML_when_BodyText_is_empty()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "html-only",
                Subject: "Hello html",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
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
        var draft = await app.StartReplyAsync(account.Id, message.Id);

        StringAssert.Contains(draft.BodyText, "> Hello world");
    }

    [TestMethod]
    public async Task StartReply_throws_when_Message_is_missing()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());

        var missingId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => app.StartReplyAsync(account.Id, missingId));

        Assert.AreEqual($"Message '{missingId}' was not found.", ex.Message);
        Assert.IsEmpty(await app.ListDraftsAsync(account.Id));
    }

    [TestMethod]
    public async Task StartReply_does_not_copy_Message_attachments()
    {
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
                        Content: "keep-on-reply"u8.ToArray()),
                ],
            });

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;
        var message = (await app.ListMessagesAsync(account.Id, mailboxId)).Single();

        var draft = await app.StartReplyAsync(account.Id, message.Id);
        Assert.IsEmpty(await app.ListDraftAttachmentsAsync(account.Id, draft.Id));
    }

    [TestMethod]
    public async Task StartReply_uses_ReplyTo_when_present()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "rt-1",
                Subject: "List post",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "hello")
            {
                ReplyToAddresses = ["list@example.com"],
            });

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;
        var message = (await app.ListMessagesAsync(account.Id, mailboxId)).Single();
        CollectionAssert.AreEqual(new[] { "list@example.com" }, message.ReplyToAddresses.ToArray());
        var searched = await app.SearchMessagesAsync(account.Id, mailboxId, "List post");
        CollectionAssert.AreEqual(new[] { "list@example.com" }, searched.Single().ReplyToAddresses.ToArray());
        var draft = await app.StartReplyAsync(account.Id, searched.Single().Id);
        CollectionAssert.AreEqual(new[] { "list@example.com" }, draft.ToAddresses.ToArray());
    }

    [TestMethod]
    public async Task StartReply_on_Sent_addresses_original_recipients()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Sent", "Sent", MailboxRole.Sent));
        fixture.Imap.SeedMessages(
            "Sent",
            new RemoteMessage(
                RemoteId: "sent-1",
                Subject: "Hello",
                FromAddress: "Alice <alice@example.com>",
                ReceivedAt: new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "hi")
            {
                ToAddresses = ["Bob <bob@example.com>", "carol@example.com"],
                CcAddresses = ["dave@example.com"],
            });

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var sent = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Role == MailboxRole.Sent);
        var message = (await app.ListMessagesAsync(account.Id, sent.Id)).Single();

        var draft = await app.StartReplyAsync(account.Id, message.Id);

        CollectionAssert.AreEqual(
            new[] { "Bob <bob@example.com>", "carol@example.com" },
            draft.ToAddresses.ToArray());
        Assert.AreEqual("Re: Hello", draft.Subject);
        Assert.IsFalse(draft.ToAddresses.Any(address =>
            address.Contains("alice@example.com", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task StartReply_on_own_Message_to_self_keeps_self()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "self-1",
                Subject: "Note",
                FromAddress: "alice@example.com",
                ReceivedAt: new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "remember")
            {
                ToAddresses = ["alice@example.com"],
            });

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var message = (await app.ListUnifiedInboxAsync()).Single();
        var draft = await app.StartReplyAsync(account.Id, message.Id);
        CollectionAssert.AreEqual(new[] { "alice@example.com" }, draft.ToAddresses.ToArray());
    }

    private static string LocalWhen(int year, int month, int day, int hour, int minute) =>
        new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero)
            .ToLocalTime()
            .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

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
