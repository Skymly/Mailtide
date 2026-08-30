using Mailtide.Core;
using Mailtide.Core.Auth;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class UpdateAccountTests
{
    [TestMethod]
    public async Task UpdateManual_updates_display_fields_and_keeps_secret_when_Password_blank()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await fixture.OpenAppAsync();

        var account = await app.AddManualAccountAsync(new ManualAccountDraft(
            DisplayName: "Personal",
            EmailAddress: "alice@example.com",
            ImapHost: "imap.example.com",
            ImapPort: 993,
            SmtpHost: "smtp.example.com",
            SmtpPort: 587,
            Password: "original-password"));

        var updated = await app.UpdateManualAccountAsync(
            account.Id,
            new ManualAccountDraft(
                DisplayName: "Renamed",
                EmailAddress: "alice+new@example.com",
                ImapHost: "imap2.example.com",
                ImapPort: 994,
                SmtpHost: "smtp2.example.com",
                SmtpPort: 465,
                Password: ""));

        Assert.IsNotNull(updated);
        Assert.AreEqual(account.Id, updated.Id);
        Assert.AreEqual(account.CredentialHandle, updated.CredentialHandle);
        Assert.AreEqual("Renamed", updated.DisplayName);
        Assert.AreEqual("alice+new@example.com", updated.EmailAddress);
        Assert.AreEqual("imap2.example.com", updated.ImapHost);
        Assert.AreEqual(994, updated.ImapPort);
        Assert.AreEqual("smtp2.example.com", updated.SmtpHost);
        Assert.AreEqual(465, updated.SmtpPort);
        Assert.AreEqual(CredentialKind.Password, updated.CredentialKind);

        var secret = await fixture.SecureStorage.RetrieveSecretAsync(updated.CredentialHandle);
        Assert.AreEqual("original-password", secret);
        AssertAppFolderHasNoPlaintextSecret(fixture.AppDataDirectory, "original-password");
    }

    [TestMethod]
    public async Task UpdateManual_replaces_secret_when_Password_non_blank_under_same_handle()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await fixture.OpenAppAsync();

        var account = await app.AddManualAccountAsync(new ManualAccountDraft(
            DisplayName: "Personal",
            EmailAddress: "alice@example.com",
            ImapHost: "imap.example.com",
            ImapPort: 993,
            SmtpHost: "smtp.example.com",
            SmtpPort: 587,
            Password: "old-password"));
        var handle = account.CredentialHandle;

        var updated = await app.UpdateManualAccountAsync(
            account.Id,
            new ManualAccountDraft(
                DisplayName: "Personal",
                EmailAddress: "alice@example.com",
                ImapHost: "imap.example.com",
                ImapPort: 993,
                SmtpHost: "smtp.example.com",
                SmtpPort: 587,
                Password: "new-password"));

        Assert.IsNotNull(updated);
        Assert.AreEqual(handle, updated.CredentialHandle);
        Assert.AreEqual("new-password", await fixture.SecureStorage.RetrieveSecretAsync(handle));
        Assert.IsNull(await fixture.SecureStorage.RetrieveSecretAsync("old-password"));
        AssertAppFolderHasNoPlaintextSecret(fixture.AppDataDirectory, "new-password");
        AssertAppFolderHasNoPlaintextSecret(fixture.AppDataDirectory, "old-password");
    }

    [TestMethod]
    public async Task UpdateManual_preserves_Mailboxes_and_Messages()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m1",
                Subject: "Hello",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "hi"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(new ManualAccountDraft(
            DisplayName: "Personal",
            EmailAddress: "alice@example.com",
            ImapHost: "imap.example.com",
            ImapPort: 993,
            SmtpHost: "smtp.example.com",
            SmtpPort: 587,
            Password: "secret"));
        await app.SyncNowAsync(account.Id);

        var mailboxBefore = (await app.ListMailboxesAsync(account.Id)).Single();
        var messageBefore = (await app.ListMessagesAsync(account.Id, mailboxBefore.Id)).Single();

        await app.UpdateManualAccountAsync(
            account.Id,
            new ManualAccountDraft(
                DisplayName: "Personal 2",
                EmailAddress: "alice@example.com",
                ImapHost: "imap.example.com",
                ImapPort: 993,
                SmtpHost: "smtp.example.com",
                SmtpPort: 587,
                Password: ""));

        var mailboxAfter = (await app.ListMailboxesAsync(account.Id)).Single();
        var messageAfter = (await app.ListMessagesAsync(account.Id, mailboxAfter.Id)).Single();
        Assert.AreEqual(mailboxBefore.Id, mailboxAfter.Id);
        Assert.AreEqual(messageBefore.Id, messageAfter.Id);
        Assert.AreEqual("Hello", messageAfter.Subject);
    }

    [TestMethod]
    public async Task UpdateQqMail_keeps_preset_endpoints_and_secret_when_AuthorizationCode_blank()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await fixture.OpenAppAsync();

        var account = await app.AddQqMailAccountAsync(new QqMailAccountDraft(
            DisplayName: "QQ",
            EmailAddress: "alice@qq.com",
            AuthorizationCode: "original-auth-code"));

        var updated = await app.UpdateQqMailAccountAsync(
            account.Id,
            new QqMailAccountDraft(
                DisplayName: "QQ Renamed",
                EmailAddress: "alice2@qq.com",
                AuthorizationCode: ""));

        Assert.IsNotNull(updated);
        Assert.AreEqual(account.Id, updated.Id);
        Assert.AreEqual(account.CredentialHandle, updated.CredentialHandle);
        Assert.AreEqual("QQ Renamed", updated.DisplayName);
        Assert.AreEqual("alice2@qq.com", updated.EmailAddress);
        Assert.AreEqual(QqMailPreset.ImapHost, updated.ImapHost);
        Assert.AreEqual(QqMailPreset.ImapPort, updated.ImapPort);
        Assert.AreEqual(QqMailPreset.SmtpHost, updated.SmtpHost);
        Assert.AreEqual(QqMailPreset.SmtpPort, updated.SmtpPort);
        Assert.AreEqual(
            "original-auth-code",
            await fixture.SecureStorage.RetrieveSecretAsync(updated.CredentialHandle));
    }

    [TestMethod]
    public async Task UpdateQqMail_replaces_AuthorizationCode_under_same_handle()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await fixture.OpenAppAsync();

        var account = await app.AddQqMailAccountAsync(new QqMailAccountDraft(
            DisplayName: "QQ",
            EmailAddress: "bob@qq.com",
            AuthorizationCode: "old-qq-code-xxxx"));
        var handle = account.CredentialHandle;

        var updated = await app.UpdateQqMailAccountAsync(
            account.Id,
            new QqMailAccountDraft(
                DisplayName: "QQ",
                EmailAddress: "bob@qq.com",
                AuthorizationCode: "new-qq-code-yyyy"));

        Assert.IsNotNull(updated);
        Assert.AreEqual(handle, updated.CredentialHandle);
        Assert.AreEqual("new-qq-code-yyyy", await fixture.SecureStorage.RetrieveSecretAsync(handle));
        AssertAppFolderHasNoPlaintextSecret(fixture.AppDataDirectory, "new-qq-code-yyyy");
    }

    [TestMethod]
    public async Task Reauthorize_replaces_refresh_secret_keeps_Id_handle_Mailboxes_Messages()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "oauth-m1",
                Subject: "Keep me",
                FromAddress: "news@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "body"));
        fixture.OAuth.AuthorizeResult = GoogleAuthorization("carol@gmail.com", "old-refresh");
        fixture.OAuth.RefreshResult = new OAuthAccessTokenResult("access-token");

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddGoogleAccountAsync("Gmail");
        await app.SyncNowAsync(account.Id);

        var mailboxBefore = (await app.ListMailboxesAsync(account.Id)).Single();
        var messageBefore = (await app.ListMessagesAsync(account.Id, mailboxBefore.Id)).Single();
        var handle = account.CredentialHandle;

        fixture.OAuth.AuthorizeResult = GoogleAuthorization("carol@gmail.com", "new-refresh");

        var updated = await app.ReauthorizeAccountAsync(account.Id);

        Assert.IsNotNull(updated);
        Assert.AreEqual(account.Id, updated.Id);
        Assert.AreEqual(handle, updated.CredentialHandle);
        Assert.AreEqual("carol@gmail.com", updated.EmailAddress);
        Assert.AreEqual(CredentialKind.OAuth, updated.CredentialKind);
        Assert.AreEqual(OAuthProvider.Google, updated.OAuthProvider);
        Assert.AreEqual("new-refresh", await fixture.SecureStorage.RetrieveSecretAsync(handle));
        Assert.AreEqual(OAuthProvider.Google, fixture.OAuth.LastAuthorizeRequest?.Provider);

        var mailboxAfter = (await app.ListMailboxesAsync(account.Id)).Single();
        var messageAfter = (await app.ListMessagesAsync(account.Id, mailboxAfter.Id)).Single();
        Assert.AreEqual(mailboxBefore.Id, mailboxAfter.Id);
        Assert.AreEqual(messageBefore.Id, messageAfter.Id);
        AssertAppFolderHasNoPlaintextSecret(fixture.AppDataDirectory, "new-refresh");
    }

    [TestMethod]
    public async Task Reauthorize_updates_EmailAddress_when_provider_returns_different_one()
    {
        using var fixture = new CoreAppFixture();
        fixture.OAuth.AuthorizeResult = GoogleAuthorization("old@gmail.com", "refresh-one");

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddGoogleAccountAsync("Gmail");

        fixture.OAuth.AuthorizeResult = GoogleAuthorization("new@gmail.com", "refresh-two");
        var updated = await app.ReauthorizeAccountAsync(account.Id);

        Assert.IsNotNull(updated);
        Assert.AreEqual(account.Id, updated.Id);
        Assert.AreEqual("new@gmail.com", updated.EmailAddress);
        Assert.AreEqual("Gmail", updated.DisplayName);
        Assert.AreEqual("refresh-two", await fixture.SecureStorage.RetrieveSecretAsync(account.CredentialHandle));
    }

    [TestMethod]
    public async Task UpdateManual_on_OAuth_Account_is_an_error()
    {
        using var fixture = new CoreAppFixture();
        fixture.OAuth.AuthorizeResult = GoogleAuthorization("oauth@gmail.com", "refresh");

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddGoogleAccountAsync("Gmail");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await app.UpdateManualAccountAsync(
                account.Id,
                new ManualAccountDraft(
                    DisplayName: "Nope",
                    EmailAddress: "oauth@gmail.com",
                    ImapHost: "imap.gmail.com",
                    ImapPort: 993,
                    SmtpHost: "smtp.gmail.com",
                    SmtpPort: 587,
                    Password: "")));
    }

    [TestMethod]
    public async Task UpdateQqMail_on_OAuth_Account_is_an_error()
    {
        using var fixture = new CoreAppFixture();
        fixture.OAuth.AuthorizeResult = GoogleAuthorization("oauth@gmail.com", "refresh");

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddGoogleAccountAsync("Gmail");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await app.UpdateQqMailAccountAsync(
                account.Id,
                new QqMailAccountDraft("QQ", "oauth@gmail.com", "")));
    }

    [TestMethod]
    public async Task Reauthorize_on_Password_Account_is_an_error()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(new ManualAccountDraft(
            DisplayName: "Personal",
            EmailAddress: "alice@example.com",
            ImapHost: "imap.example.com",
            ImapPort: 993,
            SmtpHost: "smtp.example.com",
            SmtpPort: 587,
            Password: "secret"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await app.ReauthorizeAccountAsync(account.Id));
    }

    [TestMethod]
    public async Task Update_and_Reauthorize_missing_Account_are_no_ops()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var missingId = Guid.NewGuid();

        Assert.IsNull(await app.UpdateManualAccountAsync(
            missingId,
            new ManualAccountDraft(
                DisplayName: "X",
                EmailAddress: "x@example.com",
                ImapHost: "imap.example.com",
                ImapPort: 993,
                SmtpHost: "smtp.example.com",
                SmtpPort: 587,
                Password: "")));

        Assert.IsNull(await app.UpdateQqMailAccountAsync(
            missingId,
            new QqMailAccountDraft("QQ", "x@qq.com", "")));

        Assert.IsNull(await app.ReauthorizeAccountAsync(missingId));
        Assert.IsEmpty(await app.ListAccountsAsync());
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
