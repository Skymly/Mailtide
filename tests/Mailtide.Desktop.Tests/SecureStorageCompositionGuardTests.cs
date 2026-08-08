namespace Mailtide.Desktop.Tests;

[TestClass]
public sealed class SecureStorageCompositionGuardTests
{
    [TestMethod]
    public void Desktop_Host_has_no_InMemorySecureStorage_plaintext_fallback()
    {
        var hostDir = FindDesktopHostDirectory();
        var leftover = Directory
            .EnumerateFiles(hostDir, "*InMemorySecureStorage*", SearchOption.AllDirectories)
            .ToList();

        Assert.IsEmpty(leftover, "Plaintext InMemorySecureStorage must not remain in the Desktop Host.");
    }

    [TestMethod]
    public void DesktopComposition_wires_DesktopSecureStorage_factory()
    {
        var compositionPath = Path.Combine(FindDesktopProjectDirectory(), "DesktopComposition.cs");
        var source = File.ReadAllText(compositionPath);

        Assert.Contains("DesktopSecureStorageFactory.Create", source, StringComparison.Ordinal);
        Assert.DoesNotContain("InMemorySecureStorage", source, StringComparison.Ordinal);
    }

    private static string FindDesktopProjectDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Mailtide.Desktop");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        Assert.Fail("Could not locate Mailtide.Desktop from the test output directory.");
        return null!;
    }

    private static string FindDesktopHostDirectory() =>
        Path.Combine(FindDesktopProjectDirectory(), "Host");
}
