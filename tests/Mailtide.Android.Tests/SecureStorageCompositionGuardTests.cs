using System.Xml.Linq;

namespace Mailtide.Android.Tests;

[TestClass]
public sealed class SecureStorageCompositionGuardTests
{
    [TestMethod]
    public void Android_Host_has_no_InMemorySecureStorage_plaintext_fallback()
    {
        var hostDir = FindAndroidHostDirectory();
        var leftover = Directory
            .EnumerateFiles(hostDir, "*InMemorySecureStorage*", SearchOption.AllDirectories)
            .ToList();

        Assert.IsEmpty(leftover, "Plaintext InMemorySecureStorage must not remain in the Android Host.");
    }

    [TestMethod]
    public void AndroidComposition_wires_Keystore_secure_storage_factory()
    {
        var compositionPath = Path.Combine(FindAndroidProjectDirectory(), "AndroidComposition.cs");
        var source = File.ReadAllText(compositionPath);

        Assert.Contains("AndroidSecureStorageFactory.Create", source, StringComparison.Ordinal);
        Assert.Contains("Keystore", File.ReadAllText(Path.Combine(FindAndroidHostDirectory(), "AndroidSecureStorageFactory.cs")), StringComparison.Ordinal);
        Assert.DoesNotContain("InMemorySecureStorage", source, StringComparison.Ordinal);
    }

    [TestMethod]
    public void AndroidComposition_wires_AndroidOidcOAuthClient()
    {
        var compositionPath = Path.Combine(FindAndroidProjectDirectory(), "AndroidComposition.cs");
        var source = File.ReadAllText(compositionPath);

        Assert.Contains("AndroidOidcOAuthClient", source, StringComparison.Ordinal);
        Assert.Contains("AndroidOAuthOptions.FromEnvironment", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UnsupportedOAuthClient", source, StringComparison.Ordinal);
    }

    [TestMethod]
    public void Android_csproj_uses_trimmed_Mono_AOT_path_not_PublishAot()
    {
        var csprojPath = Path.Combine(FindAndroidProjectDirectory(), "Mailtide.Android.csproj");
        var document = XDocument.Load(csprojPath);
        var text = File.ReadAllText(csprojPath);

        Assert.DoesNotContain("<PublishAot>", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PublishAot>true", text, StringComparison.OrdinalIgnoreCase);

        var publishTrimmed = document.Descendants("PublishTrimmed").FirstOrDefault()?.Value;
        Assert.AreEqual("true", publishTrimmed, "Android must enable PublishTrimmed for the trimmed APK path.");

        Assert.Contains("RunAOTCompilation", text, StringComparison.Ordinal);
        Assert.Contains("AndroidEnableProfiledAot", text, StringComparison.Ordinal);
    }

    [TestMethod]
    public void KeystoreSecureStorage_uses_AndroidKeyStore_not_KeyChain_or_EncryptedSharedPreferences()
    {
        var source = File.ReadAllText(Path.Combine(FindAndroidHostDirectory(), "KeystoreSecureStorage.cs"));

        Assert.Contains("AndroidKeyStore", source, StringComparison.Ordinal);
        Assert.Contains("org.mailtide.credentials", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EncryptedSharedPreferences", source, StringComparison.Ordinal);
        Assert.DoesNotContain("KeyChain", source, StringComparison.Ordinal);
        Assert.DoesNotContain("InMemorySecureStorage", source, StringComparison.Ordinal);
    }

    private static string FindAndroidProjectDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Mailtide.Android");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        Assert.Fail("Could not locate Mailtide.Android from the test output directory.");
        return null!;
    }

    private static string FindAndroidHostDirectory() =>
        Path.Combine(FindAndroidProjectDirectory(), "Host");
}
