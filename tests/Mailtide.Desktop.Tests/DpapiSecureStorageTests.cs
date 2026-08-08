using System.Runtime.Versioning;
using System.Text;
using Mailtide.Desktop.Host;

namespace Mailtide.Desktop.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class DpapiSecureStorageTests
{
    [TestMethod]
    public async Task Store_then_Retrieve_returns_the_Credential_secret()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("DPAPI secure storage is Windows-only.");
        }

        using var fixture = new DpapiStorageFixture();
        var storage = fixture.CreateStorage();

        await storage.StoreSecretAsync("account:test:credential", "s3cret-password");

        var secret = await storage.RetrieveSecretAsync("account:test:credential");
        Assert.AreEqual("s3cret-password", secret);
    }

    [TestMethod]
    public async Task Store_writes_ciphertext_not_plaintext_in_app_data()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("DPAPI secure storage is Windows-only.");
        }

        using var fixture = new DpapiStorageFixture();
        var storage = fixture.CreateStorage();
        const string secret = "plaintext-must-not-appear";

        await storage.StoreSecretAsync("account:cipher:credential", secret);

        AssertAppFolderHasNoPlaintextSecret(fixture.AppDataDirectory, secret);
        Assert.IsTrue(
            Directory.EnumerateFiles(fixture.AppDataDirectory, "*", SearchOption.AllDirectories).Any(),
            "Expected DPAPI ciphertext to be persisted under app data.");
    }

    [TestMethod]
    public async Task Delete_removes_the_Credential_so_Retrieve_returns_null()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("DPAPI secure storage is Windows-only.");
        }

        using var fixture = new DpapiStorageFixture();
        var storage = fixture.CreateStorage();

        await storage.StoreSecretAsync("account:delete:credential", "remove-me");
        await storage.DeleteSecretAsync("account:delete:credential");

        Assert.IsNull(await storage.RetrieveSecretAsync("account:delete:credential"));
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
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var text = reader.ReadToEnd();
            Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
        }
    }

    private sealed class DpapiStorageFixture : IDisposable
    {
        public string AppDataDirectory { get; } =
            Path.Combine(Path.GetTempPath(), "mailtide-dpapi-tests", Guid.NewGuid().ToString("N"));

        public DpapiSecureStorage CreateStorage() => new(AppDataDirectory);

        public void Dispose()
        {
            if (Directory.Exists(AppDataDirectory))
            {
                Directory.Delete(AppDataDirectory, recursive: true);
            }
        }
    }
}
