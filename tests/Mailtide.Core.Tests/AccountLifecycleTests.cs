using Mailtide.Core;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class AccountLifecycleTests
{
    [TestMethod]
    public async Task Add_Account_creates_Account_with_single_password_Credential()
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
            Password: "s3cret-password"));

        var accounts = await app.ListAccountsAsync();
        Assert.HasCount(1, accounts);
        Assert.AreEqual(account.Id, accounts[0].Id);
        Assert.AreEqual("Personal", accounts[0].DisplayName);
        Assert.AreEqual("alice@example.com", accounts[0].EmailAddress);
        Assert.AreEqual("imap.example.com", accounts[0].ImapHost);
        Assert.AreEqual(993, accounts[0].ImapPort);
        Assert.AreEqual("smtp.example.com", accounts[0].SmtpHost);
        Assert.AreEqual(587, accounts[0].SmtpPort);
        Assert.AreEqual(CredentialKind.Password, accounts[0].CredentialKind);

        var secret = await fixture.SecureStorage.RetrieveSecretAsync(accounts[0].CredentialHandle);
        Assert.AreEqual("s3cret-password", secret);
        AssertAppFolderHasNoPlaintextSecret(fixture.AppDataDirectory, "s3cret-password");
    }

    [TestMethod]
    public async Task Account_and_Credential_handle_survive_process_restart()
    {
        using var fixture = new CoreAppFixture();
        Guid accountId;
        string credentialHandle;

        await using (var app = await fixture.OpenAppAsync())
        {
            var account = await app.AddManualAccountAsync(new ManualAccountDraft(
                DisplayName: "Work",
                EmailAddress: "bob@example.com",
                ImapHost: "imap.example.com",
                ImapPort: 993,
                SmtpHost: "smtp.example.com",
                SmtpPort: 587,
                Password: "restart-secret"));
            accountId = account.Id;
            credentialHandle = account.CredentialHandle;
        }

        await using (var restarted = await fixture.OpenAppAsync())
        {
            var accounts = await restarted.ListAccountsAsync();
            Assert.HasCount(1, accounts);
            Assert.AreEqual(accountId, accounts[0].Id);
            Assert.AreEqual("Work", accounts[0].DisplayName);
            Assert.AreEqual(credentialHandle, accounts[0].CredentialHandle);
            Assert.AreEqual(CredentialKind.Password, accounts[0].CredentialKind);

            var secret = await fixture.SecureStorage.RetrieveSecretAsync(accounts[0].CredentialHandle);
            Assert.AreEqual("restart-secret", secret);
        }

        AssertAppFolderHasNoPlaintextSecret(fixture.AppDataDirectory, "restart-secret");
    }

    [TestMethod]
    public async Task Remove_Account_clears_local_data_and_Credential()
    {
        using var fixture = new CoreAppFixture();
        Guid accountId;
        string credentialHandle;

        await using (var app = await fixture.OpenAppAsync())
        {
            var account = await app.AddManualAccountAsync(new ManualAccountDraft(
                DisplayName: "Temp",
                EmailAddress: "temp@example.com",
                ImapHost: "imap.example.com",
                ImapPort: 993,
                SmtpHost: "smtp.example.com",
                SmtpPort: 587,
                Password: "remove-me-secret"));
            accountId = account.Id;
            credentialHandle = account.CredentialHandle;

            var partitionPath = Path.Combine(fixture.AppDataDirectory, "accounts", accountId.ToString("D"));
            Assert.IsTrue(Directory.Exists(partitionPath));
            await File.WriteAllTextAsync(Path.Combine(partitionPath, "local-marker.txt"), "account-local");

            await app.RemoveAccountAsync(accountId);

            Assert.IsEmpty(await app.ListAccountsAsync());
            Assert.IsNull(await fixture.SecureStorage.RetrieveSecretAsync(credentialHandle));
            Assert.IsFalse(Directory.Exists(partitionPath));
        }

        await using (var restarted = await fixture.OpenAppAsync())
        {
            Assert.IsEmpty(await restarted.ListAccountsAsync());
            Assert.IsNull(await fixture.SecureStorage.RetrieveSecretAsync(credentialHandle));
        }

        AssertAppFolderHasNoPlaintextSecret(fixture.AppDataDirectory, "remove-me-secret");
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
