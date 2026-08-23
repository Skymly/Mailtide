using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class ForegroundSyncTests
{
    [TestMethod]
    public async Task StartForegroundSync_fetches_Messages_without_manual_SyncNow()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "fg-1",
                Subject: "Arrived on its own",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 3, 8, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "self-drive"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());

        app.StartForegroundSync(TimeSpan.FromHours(1));

        await WaitUntilAsync(async () =>
            (await app.ListUnifiedInboxAsync()).Count == 1);

        var messages = await app.ListUnifiedInboxAsync();
        Assert.HasCount(1, messages);
        Assert.AreEqual("Arrived on its own", messages[0].Subject);
        Assert.AreEqual(AccountSyncState.Idle, app.GetAccountStatus(account.Id).State);
    }

    [TestMethod]
    public async Task StopForegroundSync_prevents_later_passes()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());

        app.StartForegroundSync(TimeSpan.FromMilliseconds(40));
        await WaitUntilAsync(async () =>
            (await app.ListMailboxesAsync(account.Id)).Count == 1);
        await app.StopForegroundSyncAsync();

        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "fg-late",
                Subject: "Should not arrive",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 3, 9, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "after stop"));

        await Task.Delay(150);
        Assert.IsEmpty(await app.ListUnifiedInboxAsync());
    }

    [TestMethod]
    public async Task StartForegroundSync_flushes_Outbox_without_manual_SendNow()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        var draft = await app.SaveDraftAsync(
            account.Id,
            new DraftContent(["bob@example.com"], "Hello", "Body"));
        await app.SendAsync(account.Id, draft.Id);

        app.StartForegroundSync(TimeSpan.FromHours(1));
        await WaitUntilAsync(() => Task.FromResult(fixture.Smtp.Submitted.Count == 1));

        Assert.HasCount(1, fixture.Smtp.Submitted);
        Assert.AreEqual("Hello", fixture.Smtp.Submitted[0].Subject);
        Assert.IsEmpty(await app.ListOutboxAsync(account.Id));
    }


    [TestMethod]
    public async Task SyncNow_on_the_same_Account_does_not_overlap_protocol_work()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Imap.BlockConnectUntil = gate;

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());

        var first = app.SyncNowAsync(account.Id);
        await WaitUntilAsync(() =>
            Task.FromResult(app.GetAccountStatus(account.Id).State == AccountSyncState.Syncing));
        var second = app.SyncNowAsync(account.Id);

        await Task.Delay(50);
        Assert.AreEqual(1, fixture.Imap.MaxActiveConnects);

        gate.SetResult();
        await Task.WhenAll(first, second);
        Assert.AreEqual(1, fixture.Imap.MaxActiveConnects);
        Assert.AreEqual(AccountSyncState.Idle, app.GetAccountStatus(account.Id).State);
    }

    [TestMethod]
    public async Task Foreground_pass_raises_AccountWorkCompleted()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());

        Guid? completed = null;
        app.AccountWorkCompleted += (_, accountId) => completed = accountId;
        app.StartForegroundSync(TimeSpan.FromHours(1));

        await WaitUntilAsync(() => Task.FromResult(completed == account.Id));
        Assert.AreEqual(account.Id, completed);
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
