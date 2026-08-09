namespace Mailtide.Desktop.Tests;

[TestClass]
public sealed class OAuthCompositionGuardTests
{
    [TestMethod]
    public void DesktopComposition_wires_DesktopOidcOAuthClient()
    {
        var compositionPath = Path.Combine(FindDesktopProjectDirectory(), "DesktopComposition.cs");
        var source = File.ReadAllText(compositionPath);

        Assert.Contains("DesktopOidcOAuthClient", source, StringComparison.Ordinal);
        Assert.Contains("DesktopOAuthOptions.FromEnvironment", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UnsupportedOAuthClient", source, StringComparison.Ordinal);
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
}
