using System.Text;
using Mailtide.Core;
using Mailtide.Core.Auth;
using Mailtide.Core.Security;
using Mailtide.Desktop.Host;

namespace Mailtide.Desktop.Tests;

[TestClass]
public sealed class HostSecureStorageAccountTests
{
    [TestMethod]
    public async Task Remove_Account_clears_Host_backed_Credential()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("Host secure storage is Windows DPAPI or Linux libsecret only.");
        }

        var appData = Path.Combine(Path.GetTempPath(), "mailtide-host-cred", Guid.NewGuid().ToString("N"));
        try
        {
            ISecureStorage storage;
            try
            {
                storage = DesktopSecureStorageFactory.Create(appData);
            }
            catch (SecureStorageException ex)
            {
                Assert.Inconclusive($"Host secure storage backend unavailable: {ex.Message}");
                return;
            }

            await using var app = await MailtideApp.OpenAsync(
                appData,
                storage,
                new UnsupportedOAuthClient(),
                new FakeImapClientFactory(),
                new FakeSmtpClientFactory());

            var account = await app.AddManualAccountAsync(new ManualAccountDraft(
                DisplayName: "Temp",
                EmailAddress: "temp@example.com",
                ImapHost: "imap.example.com",
                ImapPort: 993,
                SmtpHost: "smtp.example.com",
                SmtpPort: 587,
                Password: "host-remove-secret"));

            var handle = account.CredentialHandle;
            Assert.AreEqual("host-remove-secret", await storage.RetrieveSecretAsync(handle));

            await app.RemoveAccountAsync(account.Id);

            Assert.IsNull(await storage.RetrieveSecretAsync(handle));
            AssertAppFolderHasNoPlaintextSecret(appData, "host-remove-secret");

            if (storage is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        catch (SecureStorageException ex)
        {
            Assert.Inconclusive($"Host secure storage backend unavailable: {ex.Message}");
        }
        finally
        {
            if (Directory.Exists(appData))
            {
                Directory.Delete(appData, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task Add_Google_Account_stores_refresh_Credential_via_Host_secure_storage()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("Host secure storage is Windows DPAPI or Linux libsecret only.");
        }

        var appData = Path.Combine(Path.GetTempPath(), "mailtide-host-oauth", Guid.NewGuid().ToString("N"));
        try
        {
            ISecureStorage storage;
            try
            {
                storage = DesktopSecureStorageFactory.Create(appData);
            }
            catch (SecureStorageException ex)
            {
                Assert.Inconclusive($"Host secure storage backend unavailable: {ex.Message}");
                return;
            }

            var oauth = new ControllableOAuthClient
            {
                AuthorizeResult = new OAuthAuthorizationResult(
                    EmailAddress: "oauth@gmail.com",
                    RefreshSecret: "host-oauth-refresh-secret",
                    Metadata: new OAuthTokenMetadata(
                        OAuthProvider.Google,
                        GoogleMailPreset.Authority,
                        "test-google-client")),
            };

            await using var app = await MailtideApp.OpenAsync(
                appData,
                storage,
                oauth,
                new FakeImapClientFactory(),
                new FakeSmtpClientFactory());

            var account = await app.AddGoogleAccountAsync("Gmail");

            Assert.AreEqual(CredentialKind.OAuth, account.CredentialKind);
            Assert.AreEqual(
                "host-oauth-refresh-secret",
                await storage.RetrieveSecretAsync(account.CredentialHandle));
            AssertAppFolderHasNoPlaintextSecret(appData, "host-oauth-refresh-secret");

            if (storage is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        catch (SecureStorageException ex)
        {
            Assert.Inconclusive($"Host secure storage backend unavailable: {ex.Message}");
        }
        finally
        {
            if (Directory.Exists(appData))
            {
                Directory.Delete(appData, recursive: true);
            }
        }
    }

    private static void AssertAppFolderHasNoPlaintextSecret(string appDataDirectory, string secret)
    {
        if (!Directory.Exists(appDataDirectory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(appDataDirectory, "*", SearchOption.AllDirectories))
        {
            using var stream = new FileStream(
                file,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            Assert.DoesNotContain(secret, reader.ReadToEnd(), StringComparison.Ordinal);
        }
    }
}

internal sealed class ControllableOAuthClient : IOAuthClient
{
    public OAuthAuthorizationResult? AuthorizeResult { get; set; }

    public OAuthAccessTokenResult? RefreshResult { get; set; }

    public Exception? RefreshFailWith { get; set; }

    public Task<OAuthAuthorizationResult> AuthorizeAsync(
        OAuthAuthorizeRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = request;
        _ = cancellationToken;
        return Task.FromResult(
            AuthorizeResult
            ?? throw new InvalidOperationException("AuthorizeResult was not set."));
    }

    public Task<OAuthAccessTokenResult> RefreshAsync(
        OAuthRefreshRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = request;
        _ = cancellationToken;
        if (RefreshFailWith is not null)
        {
            throw RefreshFailWith;
        }

        return Task.FromResult(
            RefreshResult
            ?? throw new InvalidOperationException("RefreshResult was not set."));
    }
}
