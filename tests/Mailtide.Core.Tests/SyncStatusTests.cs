using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class SyncStatusTests
{
    [TestMethod]
    public async Task Account_status_is_idle_then_syncing_then_idle_around_SyncNow()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Imap.BlockConnectUntil = gate;

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());

        Assert.AreEqual(AccountSyncState.Idle, app.GetAccountStatus(account.Id).State);

        var syncTask = app.SyncNowAsync(account.Id);

        await WaitUntilAsync(() => app.GetAccountStatus(account.Id).State == AccountSyncState.Syncing);
        Assert.AreEqual(AccountSyncState.Syncing, app.GetAccountStatus(account.Id).State);

        gate.SetResult();
        await syncTask;

        Assert.AreEqual(AccountSyncState.Idle, app.GetAccountStatus(account.Id).State);
        Assert.IsNull(app.GetAccountStatus(account.Id).ErrorMessage);
    }

    [TestMethod]
    public async Task Auth_failure_surfaces_as_Account_error_without_protocol_details()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.FailWith = new ImapAuthenticationException("NO [AUTHENTICATIONFAILED] Invalid credentials (Failure)");

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());

        await app.SyncNowAsync(account.Id);

        var status = app.GetAccountStatus(account.Id);
        Assert.AreEqual(AccountSyncState.Error, status.State);
        Assert.AreEqual("Authentication failed. Sign in again.", status.ErrorMessage);
        Assert.DoesNotContain("AUTHENTICATIONFAILED", status.ErrorMessage!, StringComparison.Ordinal);
        Assert.DoesNotContain("NO [", status.ErrorMessage!, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task Protocol_failure_surfaces_as_Account_error_without_protocol_details()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.FailWith = new ImapProtocolException("BAD FETCH unexpected tag * 1 FETCH (FLAGS (\\Seen))");

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());

        await app.SyncNowAsync(account.Id);

        var status = app.GetAccountStatus(account.Id);
        Assert.AreEqual(AccountSyncState.Error, status.State);
        Assert.AreEqual("Could not sync this Account. Try again later.", status.ErrorMessage);
        Assert.DoesNotContain("FETCH", status.ErrorMessage!, StringComparison.Ordinal);
        Assert.DoesNotContain("\\Seen", status.ErrorMessage!, StringComparison.Ordinal);
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
