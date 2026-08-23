using Mailtide.Core;
using Mailtide.Core.Imap;
using Mailtide.Core.Tests.Protocol;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class ImapIdleTests
{
    [TestMethod]
    public async Task Idle_signal_syncs_new_Inbox_Message_without_waiting_for_timer()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "idle-1",
                Subject: "Already here",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 5, 8, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "first"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        app.StartForegroundSync(TimeSpan.FromHours(1));
        await WaitUntilAsync(async () => (await app.ListUnifiedInboxAsync()).Count == 1);
        await WaitUntilAsync(() => Task.FromResult(fixture.Imap.IdleWaiters > 0));

        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "idle-1",
                Subject: "Already here",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 5, 8, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "first"),
            new RemoteMessage(
                RemoteId: "idle-2",
                Subject: "Just arrived",
                FromAddress: "carol@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 5, 8, 5, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "second"));
        fixture.Imap.SignalMailboxChange();

        await WaitUntilAsync(async () => (await app.ListUnifiedInboxAsync()).Count == 2);
        var subjects = (await app.ListUnifiedInboxAsync()).Select(m => m.Subject).ToList();
        CollectionAssert.Contains(subjects, "Just arrived");
    }

    [TestMethod]
    public async Task Real_IMAP_adapter_returns_when_IDLE_reports_EXISTS()
    {
        await using var server = LoopbackImapServer.Start(
        [
            new SeededMailbox(
                Path: "INBOX",
                Attributes: ["Inbox"],
                Messages:
                [
                    new SeededImapMessage(
                        Uid: 1,
                        Subject: "Hello",
                        From: "bob@example.com",
                        InternalDate: new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero),
                        IsRead: false,
                        BodyText: "body"),
                ]),
        ]);

        await using var client = new MailKitImapClientFactory().Create();
        await client.ConnectAndAuthenticateAsync(
            "127.0.0.1",
            server.Port,
            "alice@example.com",
            "s3cret-password");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await client.WaitForMailboxChangeAsync("INBOX", cts.Token);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Fail("Timed out waiting for condition.");
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
