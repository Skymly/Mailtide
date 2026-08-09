namespace Mailtide.Android.Tests;

[TestClass]
public sealed class KeystoreSecureStorageContractTests
{
    [TestMethod]
    public void KeystoreSecureStorage_source_stores_ciphertext_under_credentials_directory()
    {
        var source = File.ReadAllText(Path.Combine(FindAndroidHostDirectory(), "KeystoreSecureStorage.cs"));

        Assert.Contains("Path.Combine(appDataDirectory, \"credentials\")", source, StringComparison.Ordinal);
        Assert.Contains("SHA256.HashData", source, StringComparison.Ordinal);
        Assert.Contains("File.WriteAllBytes", source, StringComparison.Ordinal);
        Assert.Contains("DeleteSecretAsync", source, StringComparison.Ordinal);
        Assert.Contains("SecureStorageException", source, StringComparison.Ordinal);
        Assert.DoesNotContain("File.WriteAllText", source, StringComparison.Ordinal);
    }

    private static string FindAndroidHostDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Mailtide.Android", "Host");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        Assert.Fail("Could not locate Mailtide.Android Host from the test output directory.");
        return null!;
    }
}
