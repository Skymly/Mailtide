using Mailtide.Core;
using Mailtide.Core.Auth;
using Mailtide.Core.Imap;
using Mailtide.Core.Security;
using Mailtide.Core.Smtp;
using Mailtide.UI;

namespace Mailtide.Android.Tests;

[TestClass]
public sealed class BrowseShellCompositionTests
{
    [TestMethod]
    public async Task BrowseShell_lists_Accounts_and_Unified_Inbox_through_Core_composition()
    {
        var appData = Path.Combine(Path.GetTempPath(), "mailtide-android-browse", Guid.NewGuid().ToString("N"));
        try
        {
            await using var app = await MailtideApp.OpenAsync(
                appData,
                new FakeSecureStorage(),
                new TestUnsupportedOAuthClient(),
                new FakeImapClientFactory(),
                new FakeSmtpClientFactory());

            var account = await app.AddManualAccountAsync(new ManualAccountDraft(
                DisplayName: "Personal",
                EmailAddress: "alice@example.com",
                ImapHost: "imap.example.com",
                ImapPort: 993,
                SmtpHost: "smtp.example.com",
                SmtpPort: 587,
                Password: "browse-secret"));

            var shell = new BrowseShell(app);
            await shell.LoadAccountsAsync();
            Assert.HasCount(1, shell.Accounts);
            Assert.AreEqual(account.Id, shell.Accounts[0].Id);

            await shell.ShowUnifiedInboxAsync();
            Assert.IsTrue(shell.ShowingUnifiedInbox);
            Assert.IsNotNull(shell.Messages);
        }
        finally
        {
            if (Directory.Exists(appData))
            {
                Directory.Delete(appData, recursive: true);
            }
        }
    }
}

internal sealed class FakeSecureStorage : ISecureStorage
{
    private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);

    public Task StoreSecretAsync(string key, string secret, CancellationToken cancellationToken = default)
    {
        _secrets[key] = secret;
        return Task.CompletedTask;
    }

    public Task<string?> RetrieveSecretAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_secrets.TryGetValue(key, out var secret) ? secret : null);

    public Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        _secrets.Remove(key);
        return Task.CompletedTask;
    }
}

internal sealed class TestUnsupportedOAuthClient : IOAuthClient
{
    public Task<OAuthAuthorizationResult> AuthorizeAsync(
        OAuthAuthorizeRequest request,
        CancellationToken cancellationToken = default) =>
        throw new OAuthAuthenticationException("OAuth is not available in this test.");

    public Task<OAuthAccessTokenResult> RefreshAsync(
        OAuthRefreshRequest request,
        CancellationToken cancellationToken = default) =>
        throw new OAuthAuthenticationException("OAuth is not available in this test.");
}

internal sealed class FakeImapClientFactory : IImapClientFactory
{
    public IImapClient Create() => new FakeImapClient();
}

internal sealed class FakeImapClient : IImapClient
{
    public Task ConnectAndAuthenticateAsync(
        string host,
        int port,
        string username,
        string password,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<RemoteMailbox>> ListMailboxesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RemoteMailbox>>([]);

    public Task<IReadOnlyList<RemoteMessage>> FetchMessagesAsync(
        string mailboxPath,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RemoteMessage>>([]);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class FakeSmtpClientFactory : ISmtpClientFactory
{
    public ISmtpClient Create() => new FakeSmtpClient();
}

internal sealed class FakeSmtpClient : ISmtpClient
{
    public Task ConnectAndAuthenticateAsync(
        string host,
        int port,
        string username,
        string password,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task SubmitAsync(OutboundMessage message, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
