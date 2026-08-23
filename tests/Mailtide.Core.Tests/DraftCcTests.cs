using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class DraftCcTests
{
    [TestMethod]
    public async Task SaveDraft_round_trips_Cc_separately_from_To()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidAccountDraft());

        var draft = await app.SaveDraftAsync(
            account.Id,
            new DraftContent(["bob@example.com"], "Hello", "Body")
            {
                CcAddresses = ["carol@example.com"],
            });

        CollectionAssert.AreEqual(new[] { "bob@example.com" }, draft.ToAddresses.ToArray());
        CollectionAssert.AreEqual(new[] { "carol@example.com" }, draft.CcAddresses.ToArray());

        var listed = (await app.ListDraftsAsync(account.Id)).Single();
        CollectionAssert.AreEqual(new[] { "carol@example.com" }, listed.CcAddresses.ToArray());
    }

    [TestMethod]
    public async Task SendNow_submits_Cc_on_OutboundMessage()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidAccountDraft());

        var draft = await app.SaveDraftAsync(
            account.Id,
            new DraftContent(["bob@example.com"], "Hello", "Body")
            {
                CcAddresses = ["carol@example.com"],
            });
        await app.SendAsync(account.Id, draft.Id);
        await app.SendNowAsync(account.Id);

        Assert.HasCount(1, fixture.Smtp.Submitted);
        CollectionAssert.AreEqual(
            new[] { "bob@example.com" },
            fixture.Smtp.Submitted[0].ToAddresses.ToArray());
        CollectionAssert.AreEqual(
            new[] { "carol@example.com" },
            fixture.Smtp.Submitted[0].CcAddresses.ToArray());
    }

    [TestMethod]
    public async Task StartReplyAll_puts_original_Cc_on_Draft_Cc()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "uid-ra",
                Subject: "Team thread",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 5, 2, 11, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "please reply all")
            {
                ToAddresses = ["alice@example.com", "carol@example.com"],
                CcAddresses = ["dave@example.com", "alice@example.com"],
            });

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidAccountDraft());
        await app.SyncNowAsync(account.Id);
        var message = (await app.ListUnifiedInboxAsync()).Single();

        var draft = await app.StartReplyAllAsync(account.Id, message.Id);

        CollectionAssert.AreEqual(
            new[] { "bob@example.com", "carol@example.com" },
            draft.ToAddresses.ToArray());
        CollectionAssert.AreEqual(
            new[] { "dave@example.com" },
            draft.CcAddresses.ToArray());
    }

    private static ManualAccountDraft ValidAccountDraft() =>
        new(
            DisplayName: "Personal",
            EmailAddress: "alice@example.com",
            ImapHost: "imap.example.com",
            ImapPort: 993,
            SmtpHost: "smtp.example.com",
            SmtpPort: 587,
            Password: "s3cret-password");
}
