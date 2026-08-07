using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class QqMailPresetAccountTests
{
    [TestMethod]
    public async Task Add_QQ_Account_uses_preset_IMAP_SMTP_without_manual_server_entry()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await fixture.OpenAppAsync();

        var account = await app.AddQqMailAccountAsync(new QqMailAccountDraft(
            DisplayName: "QQ",
            EmailAddress: "alice@qq.com",
            AuthorizationCode: "abcdefghijklmnop"));

        Assert.AreEqual("imap.qq.com", account.ImapHost);
        Assert.AreEqual(993, account.ImapPort);
        Assert.AreEqual("smtp.qq.com", account.SmtpHost);
        Assert.AreEqual(465, account.SmtpPort);
        Assert.AreEqual("QQ", account.DisplayName);
        Assert.AreEqual("alice@qq.com", account.EmailAddress);
    }

    [TestMethod]
    public async Task Add_QQ_Account_stores_authorization_code_as_password_Credential()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await fixture.OpenAppAsync();

        var account = await app.AddQqMailAccountAsync(new QqMailAccountDraft(
            DisplayName: "QQ",
            EmailAddress: "bob@qq.com",
            AuthorizationCode: "qq-auth-code-16xx"));

        Assert.AreEqual(CredentialKind.Password, account.CredentialKind);

        var secret = await fixture.SecureStorage.RetrieveSecretAsync(account.CredentialHandle);
        Assert.AreEqual("qq-auth-code-16xx", secret);
        AssertAppFolderHasNoPlaintextSecret(fixture.AppDataDirectory, "qq-auth-code-16xx");
    }

    [TestMethod]
    public async Task QQ_Account_remove_clears_local_data_and_Credential()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await fixture.OpenAppAsync();

        var account = await app.AddQqMailAccountAsync(new QqMailAccountDraft(
            DisplayName: "Temp QQ",
            EmailAddress: "temp@qq.com",
            AuthorizationCode: "remove-qq-secretxx"));

        var partitionPath = Path.Combine(fixture.AppDataDirectory, "accounts", account.Id.ToString("D"));
        Assert.IsTrue(Directory.Exists(partitionPath));

        await app.RemoveAccountAsync(account.Id);

        Assert.IsEmpty(await app.ListAccountsAsync());
        Assert.IsNull(await fixture.SecureStorage.RetrieveSecretAsync(account.CredentialHandle));
        Assert.IsFalse(Directory.Exists(partitionPath));
        AssertAppFolderHasNoPlaintextSecret(fixture.AppDataDirectory, "remove-qq-secretxx");
    }

    [TestMethod]
    public async Task QQ_Account_SyncNow_behaves_like_password_Account()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddQqMailAccountAsync(new QqMailAccountDraft(
            DisplayName: "QQ",
            EmailAddress: "sync@qq.com",
            AuthorizationCode: "sync-qq-auth-code1"));

        await app.SyncNowAsync(account.Id);

        var mailboxes = await app.ListMailboxesAsync(account.Id);
        Assert.HasCount(1, mailboxes);
        Assert.AreEqual(MailboxRole.Inbox, mailboxes[0].Role);
        Assert.AreEqual(AccountSyncState.Idle, app.GetAccountStatus(account.Id).State);
    }

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
