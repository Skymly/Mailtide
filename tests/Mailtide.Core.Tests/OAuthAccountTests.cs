using Mailtide.Core;
using Mailtide.Core.Auth;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class OAuthAccountTests
{
    [TestMethod]
    public async Task Add_Google_Account_creates_OAuth_Credential_with_preset_and_secure_refresh()
    {
        using var fixture = new CoreAppFixture();
        fixture.OAuth.AuthorizeResult = new OAuthAuthorizationResult(
            EmailAddress: "alice@gmail.com",
            RefreshSecret: "google-refresh-secret",
            Metadata: new OAuthTokenMetadata(
                OAuthProvider.Google,
                Authority: GoogleMailPreset.Authority,
                ClientId: "test-google-client"));

        await using var app = await fixture.OpenAppAsync();

        var account = await app.AddGoogleAccountAsync("Gmail");

        Assert.AreEqual("Gmail", account.DisplayName);
        Assert.AreEqual("alice@gmail.com", account.EmailAddress);
        Assert.AreEqual(GoogleMailPreset.ImapHost, account.ImapHost);
        Assert.AreEqual(GoogleMailPreset.ImapPort, account.ImapPort);
        Assert.AreEqual(GoogleMailPreset.SmtpHost, account.SmtpHost);
        Assert.AreEqual(GoogleMailPreset.SmtpPort, account.SmtpPort);
        Assert.AreEqual(CredentialKind.OAuth, account.CredentialKind);
        Assert.AreEqual(OAuthProvider.Google, account.OAuthProvider);
        Assert.AreEqual(GoogleMailPreset.Authority, account.OAuthAuthority);

        var secret = await fixture.SecureStorage.RetrieveSecretAsync(account.CredentialHandle);
        Assert.AreEqual("google-refresh-secret", secret);
        AssertAppFolderHasNoPlaintextSecret(fixture.AppDataDirectory, "google-refresh-secret");

        Assert.AreEqual(OAuthProvider.Google, fixture.OAuth.LastAuthorizeRequest?.Provider);
    }

    [TestMethod]
    public async Task Add_Microsoft_consumer_Account_uses_consumer_authority_not_Entra()
    {
        using var fixture = new CoreAppFixture();
        fixture.OAuth.AuthorizeResult = new OAuthAuthorizationResult(
            EmailAddress: "bob@outlook.com",
            RefreshSecret: "ms-refresh-secret",
            Metadata: new OAuthTokenMetadata(
                OAuthProvider.MicrosoftConsumer,
                Authority: MicrosoftConsumerMailPreset.Authority,
                ClientId: "test-ms-client"));

        await using var app = await fixture.OpenAppAsync();

        var account = await app.AddMicrosoftConsumerAccountAsync("Outlook");

        Assert.AreEqual("Outlook", account.DisplayName);
        Assert.AreEqual("bob@outlook.com", account.EmailAddress);
        Assert.AreEqual(MicrosoftConsumerMailPreset.ImapHost, account.ImapHost);
        Assert.AreEqual(MicrosoftConsumerMailPreset.ImapPort, account.ImapPort);
        Assert.AreEqual(MicrosoftConsumerMailPreset.SmtpHost, account.SmtpHost);
        Assert.AreEqual(MicrosoftConsumerMailPreset.SmtpPort, account.SmtpPort);
        Assert.AreEqual(CredentialKind.OAuth, account.CredentialKind);
        Assert.AreEqual(OAuthProvider.MicrosoftConsumer, account.OAuthProvider);
        Assert.AreEqual(MicrosoftConsumerMailPreset.Authority, account.OAuthAuthority);
        StringAssert.Contains(account.OAuthAuthority!, "/consumers");
        Assert.DoesNotContain("/organizations", account.OAuthAuthority!, StringComparison.Ordinal);
        Assert.DoesNotContain("/common", account.OAuthAuthority!, StringComparison.Ordinal);

        var secret = await fixture.SecureStorage.RetrieveSecretAsync(account.CredentialHandle);
        Assert.AreEqual("ms-refresh-secret", secret);
        AssertAppFolderHasNoPlaintextSecret(fixture.AppDataDirectory, "ms-refresh-secret");

        Assert.AreEqual(
            OAuthProvider.MicrosoftConsumer,
            fixture.OAuth.LastAuthorizeRequest?.Provider);
    }

    [TestMethod]
    public async Task SyncNow_for_OAuth_Account_passes_access_token_not_refresh()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.OAuth.AuthorizeResult = GoogleAuthorization("carol@gmail.com", "oauth-refresh-secret");
        fixture.OAuth.RefreshResult = new OAuthAccessTokenResult("oauth-access-token");

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddGoogleAccountAsync("Gmail");

        await app.SyncNowAsync(account.Id);

        Assert.AreEqual(1, fixture.OAuth.RefreshCallCount);
        Assert.AreEqual("oauth-refresh-secret", fixture.OAuth.LastRefreshRequest?.RefreshSecret);
        Assert.AreEqual("oauth-access-token", fixture.Imap.LastPassword);
        Assert.AreNotEqual("oauth-refresh-secret", fixture.Imap.LastPassword);
        Assert.AreEqual(AccountSyncState.Idle, app.GetAccountStatus(account.Id).State);
    }

    [TestMethod]
    public async Task OAuth_refresh_failure_surfaces_as_Account_relogin_error_and_invalidates()
    {
        using var fixture = new CoreAppFixture();
        fixture.OAuth.AuthorizeResult = GoogleAuthorization("dave@gmail.com", "oauth-refresh-secret");
        fixture.OAuth.RefreshFailWith = new OAuthAuthenticationException("invalid_grant");

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddGoogleAccountAsync("Gmail");

        await app.SyncNowAsync(account.Id);

        var status = app.GetAccountStatus(account.Id);
        Assert.AreEqual(AccountSyncState.Error, status.State);
        Assert.AreEqual("Authentication failed. Sign in again.", status.ErrorMessage);
        Assert.IsNull(fixture.Imap.LastPassword);
        Assert.IsNull(await fixture.SecureStorage.RetrieveSecretAsync(account.CredentialHandle));
    }

    [TestMethod]
    public async Task Account_never_holds_both_OAuth_and_password_Credentials()
    {
        using var fixture = new CoreAppFixture();
        fixture.OAuth.AuthorizeResult = GoogleAuthorization("eve@gmail.com", "oauth-only-refresh");

        await using var app = await fixture.OpenAppAsync();

        var oauth = await app.AddGoogleAccountAsync("Gmail");
        var password = await app.AddManualAccountAsync(new ManualAccountDraft(
            DisplayName: "Manual",
            EmailAddress: "manual@example.com",
            ImapHost: "imap.example.com",
            ImapPort: 993,
            SmtpHost: "smtp.example.com",
            SmtpPort: 587,
            Password: "password-secret"));

        Assert.AreEqual(CredentialKind.OAuth, oauth.CredentialKind);
        Assert.IsNotNull(oauth.OAuthProvider);
        Assert.AreEqual(CredentialKind.Password, password.CredentialKind);
        Assert.IsNull(password.OAuthProvider);

        var oauthSecret = await fixture.SecureStorage.RetrieveSecretAsync(oauth.CredentialHandle);
        var passwordSecret = await fixture.SecureStorage.RetrieveSecretAsync(password.CredentialHandle);
        Assert.AreEqual("oauth-only-refresh", oauthSecret);
        Assert.AreEqual("password-secret", passwordSecret);
        Assert.AreNotEqual(oauth.CredentialHandle, password.CredentialHandle);
    }

    [TestMethod]
    public async Task Remove_OAuth_Account_clears_refresh_Credential()
    {
        using var fixture = new CoreAppFixture();
        fixture.OAuth.AuthorizeResult = GoogleAuthorization("temp@gmail.com", "remove-oauth-refresh");

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddGoogleAccountAsync("Temp");
        var handle = account.CredentialHandle;

        await app.RemoveAccountAsync(account.Id);

        Assert.IsEmpty(await app.ListAccountsAsync());
        Assert.IsNull(await fixture.SecureStorage.RetrieveSecretAsync(handle));
        AssertAppFolderHasNoPlaintextSecret(fixture.AppDataDirectory, "remove-oauth-refresh");
    }

    private static OAuthAuthorizationResult GoogleAuthorization(string email, string refreshSecret) =>
        new(
            EmailAddress: email,
            RefreshSecret: refreshSecret,
            Metadata: new OAuthTokenMetadata(
                OAuthProvider.Google,
                Authority: GoogleMailPreset.Authority,
                ClientId: "test-google-client"));

    private static void AssertAppFolderHasNoPlaintextSecret(string appDataDirectory, string secret)
    {
        foreach (var file in Directory.EnumerateFiles(appDataDirectory, "*", SearchOption.AllDirectories))
        {
            using var stream = new FileStream(
                file,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var text = reader.ReadToEnd();
            Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
        }
    }
}
