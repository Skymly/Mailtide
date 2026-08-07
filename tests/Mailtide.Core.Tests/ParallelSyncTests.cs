using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class ParallelSyncTests
{
    [TestMethod]
    public async Task Two_Accounts_can_SyncNow_concurrently_without_blocking_each_other()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Imap.BlockConnectUntil = gate;

        await using var app = await fixture.OpenAppAsync();
        var accountA = await app.AddManualAccountAsync(Draft("Alice", "alice@example.com"));
        var accountB = await app.AddManualAccountAsync(Draft("Bob", "bob@example.com"));

        var syncA = app.SyncNowAsync(accountA.Id);
        var syncB = app.SyncNowAsync(accountB.Id);

        try
        {
            await WaitUntilAsync(
                () =>
                    app.GetAccountStatus(accountA.Id).State == AccountSyncState.Syncing
                    && app.GetAccountStatus(accountB.Id).State == AccountSyncState.Syncing,
                timeout: TimeSpan.FromSeconds(2));

            Assert.AreEqual(AccountSyncState.Syncing, app.GetAccountStatus(accountA.Id).State);
            Assert.AreEqual(AccountSyncState.Syncing, app.GetAccountStatus(accountB.Id).State);
        }
        finally
        {
            gate.TrySetResult();
            await Task.WhenAll(syncA, syncB);
        }

        Assert.AreEqual(AccountSyncState.Idle, app.GetAccountStatus(accountA.Id).State);
        Assert.AreEqual(AccountSyncState.Idle, app.GetAccountStatus(accountB.Id).State);

        Assert.HasCount(1, await app.ListMailboxesAsync(accountA.Id));
        Assert.HasCount(1, await app.ListMailboxesAsync(accountB.Id));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Fail("Timed out waiting for condition.");
    }

    private static ManualAccountDraft Draft(string displayName, string email) =>
        new(
            DisplayName: displayName,
            EmailAddress: email,
            ImapHost: "imap.example.com",
            ImapPort: 993,
            SmtpHost: "smtp.example.com",
            SmtpPort: 587,
            Password: "s3cret-password");
}
