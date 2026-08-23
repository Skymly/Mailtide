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
        Assert.AreEqual(
            """

            ---------- Forwarded Message ----------
            From: bob@example.com
            Date: 2026-04-01 10:00 UTC
            Subject: Hello offline

            Body stays local.
            """.ReplaceLineEndings("\n"),
            draft.BodyText.ReplaceLineEndings("\n"));
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
