using Mailtide.Core;
using Mailtide.Core.Imap;
using Mailtide.Core.Smtp;
using Mailtide.Core.Tests.Protocol;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class RealProtocolAdapterOrchestrationTests
{
    [TestMethod]
    public async Task SyncNow_works_unchanged_against_real_IMAP_adapter()
    {
        await using var imap = LoopbackImapServer.Start(
        [
            new SeededMailbox(
                Path: "INBOX",
                Attributes: ["Inbox"],
                Messages:
                [
                    new SeededImapMessage(
                        Uid: 7,
                        Subject: "From real IMAP",
                        From: "bob@example.com",
                        InternalDate: new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero),
                        IsRead: true,
                        BodyText: "Synced via MailKit."),
                ]),
        ]);

        using var fixture = new CoreAppFixture();
        await using var app = await MailtideApp.OpenAsync(
            fixture.AppDataDirectory,
            fixture.SecureStorage,
            new MailKitImapClientFactory(),
            new MailKitSmtpClientFactory());

        var account = await app.AddManualAccountAsync(
            new ManualAccountDraft(
                DisplayName: "Personal",
                EmailAddress: "alice@example.com",
                ImapHost: "127.0.0.1",
                ImapPort: imap.Port,
                SmtpHost: "127.0.0.1",
                SmtpPort: 9,
                Password: "s3cret-password"));

        await app.SyncNowAsync(account.Id);

        var status = app.GetAccountStatus(account.Id);
        Assert.AreEqual(AccountSyncState.Idle, status.State);

        var mailboxes = await app.ListMailboxesAsync(account.Id);
        Assert.HasCount(1, mailboxes);
        Assert.AreEqual(MailboxRole.Inbox, mailboxes[0].Role);

        var messages = await app.ListMessagesAsync(account.Id, mailboxes[0].Id);
        Assert.HasCount(1, messages);
        Assert.AreEqual("From real IMAP", messages[0].Subject);

        var body = await app.GetMessageBodyAsync(account.Id, messages[0].Id);
        Assert.AreEqual("Synced via MailKit.", body);
    }

    [TestMethod]
    public async Task SendNow_works_unchanged_against_real_SMTP_adapter()
    {
        await using var smtp = LoopbackSmtpServer.Start();

        using var fixture = new CoreAppFixture();
        await using var app = await MailtideApp.OpenAsync(
            fixture.AppDataDirectory,
            fixture.SecureStorage,
            new MailKitImapClientFactory(),
            new MailKitSmtpClientFactory());

        var account = await app.AddManualAccountAsync(
            new ManualAccountDraft(
                DisplayName: "Personal",
                EmailAddress: "alice@example.com",
                ImapHost: "127.0.0.1",
                ImapPort: 9,
                SmtpHost: "127.0.0.1",
                SmtpPort: smtp.Port,
                Password: "s3cret-password"));

        var draft = await app.SaveDraftAsync(
            account.Id,
            new DraftContent(["bob@example.com"], "Outbox via MailKit", "Send me"));
        await app.SendAsync(account.Id, draft.Id);
        await app.SendNowAsync(account.Id);

        Assert.AreEqual(0, (await app.ListOutboxAsync(account.Id)).Count);
        Assert.HasCount(1, smtp.AcceptedMessages);
        StringAssert.Contains(smtp.AcceptedMessages[0], "Outbox via MailKit");
        StringAssert.Contains(smtp.AcceptedMessages[0], "Send me");
    }
}
