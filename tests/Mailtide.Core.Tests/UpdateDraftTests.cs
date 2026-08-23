using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class UpdateDraftTests
{
    [TestMethod]
    public async Task SaveDraft_with_existing_Id_updates_body_and_keeps_threading_headers()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "uid-1",
                Subject: "Team thread",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 6, 11, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "please reply")
            {
                InternetMessageId = "<orig@example.com>",
                References = ["<root@example.com>"],
            });

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var message = (await app.ListUnifiedInboxAsync()).Single();
        var created = await app.StartReplyAsync(account.Id, message.Id);

        var updated = await app.SaveDraftAsync(
            account.Id,
            new DraftContent(["bob@example.com"], "Re: Team thread", "edited reply")
            {
                CcAddresses = ["carol@example.com"],
            },
            created.Id);

        Assert.AreEqual(created.Id, updated.Id);
        Assert.AreEqual("edited reply", updated.BodyText);
        CollectionAssert.AreEqual(new[] { "carol@example.com" }, updated.CcAddresses.ToArray());
        Assert.AreEqual("<orig@example.com>", updated.InReplyTo);
        CollectionAssert.AreEqual(
            new[] { "<root@example.com>", "<orig@example.com>" },
            updated.References.ToArray());
        Assert.HasCount(1, await app.ListDraftsAsync(account.Id));
    }

    [TestMethod]
    public async Task SendNow_after_updating_Reply_Draft_submits_edited_body_and_headers()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "uid-2",
                Subject: "Team thread",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 6, 11, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "please reply")
            {
                InternetMessageId = "<orig@example.com>",
            });

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var message = (await app.ListUnifiedInboxAsync()).Single();
        var created = await app.StartReplyAsync(account.Id, message.Id);
        await app.SaveDraftAsync(
            account.Id,
            new DraftContent(["bob@example.com"], "Re: Team thread", "final wording"),
            created.Id);
        await app.SendAsync(account.Id, created.Id);
        await app.SendNowAsync(account.Id);

        Assert.HasCount(1, fixture.Smtp.Submitted);
        Assert.AreEqual("final wording", fixture.Smtp.Submitted[0].BodyText);
        Assert.AreEqual("<orig@example.com>", fixture.Smtp.Submitted[0].InReplyTo);
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
