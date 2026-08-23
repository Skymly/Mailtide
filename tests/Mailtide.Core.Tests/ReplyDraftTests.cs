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
        Assert.AreEqual(
            """

            On 2026-04-01 10:00 UTC, bob@example.com wrote:

            > Body stays local.
            """.ReplaceLineEndings("\n"),
            draft.BodyText.ReplaceLineEndings("\n"));

        var drafts = await app.ListDraftsAsync(account.Id);
        Assert.HasCount(1, drafts);
        Assert.AreEqual(draft.Id, drafts[0].Id);
        Assert.AreEqual(0, fixture.Smtp.Submitted.Count);
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
        Assert.AreEqual(
            """

            On 2026-04-02 15:30 UTC, carol@example.com wrote:

            > line one
            > line two
            """.ReplaceLineEndings("\n"),
            draft.BodyText.ReplaceLineEndings("\n"));
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
