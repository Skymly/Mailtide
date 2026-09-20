using System.Xml.Linq;

namespace Mailtide.Android.Tests;

[TestClass]
public sealed class AndroidGoogleRedirectUriGuardTests
{
    [TestMethod]
    public void Android_Google_RedirectUri_scheme_contains_a_period_and_matches_package()
    {
        var projectDir = FindAndroidProjectDirectory();
        var csproj = XDocument.Load(Path.Combine(projectDir, "Mailtide.Android.csproj"));
        var applicationId = csproj.Descendants("ApplicationId").FirstOrDefault()?.Value;
        Assert.IsFalse(string.IsNullOrWhiteSpace(applicationId));
        Assert.Contains(".", applicationId, StringComparison.Ordinal);

        var browser = File.ReadAllText(Path.Combine(projectDir, "Host", "IntentSystemBrowser.cs"));
        var activity = File.ReadAllText(Path.Combine(projectDir, "MainActivity.cs"));
        var expectedUri = $"{applicationId}://oauth/callback";

        Assert.Contains($"RedirectUri = \"{expectedUri}\"", browser, StringComparison.Ordinal);
        Assert.DoesNotContain("\"mailtide://oauth/callback\"", browser, StringComparison.Ordinal);
        Assert.DoesNotContain("\"mailtide://oauth/callback\"", activity, StringComparison.Ordinal);
        Assert.Contains($"DataScheme = \"{applicationId}\"", activity, StringComparison.Ordinal);
        Assert.Contains($"data.Scheme, \"{applicationId}\"", activity, StringComparison.Ordinal);
    }

    [TestMethod]
    public void Desktop_loopback_redirect_stays_on_127_0_0_1()
    {
        var root = FindRepoRoot();
        var loopback = File.ReadAllText(
            Path.Combine(root, "src", "Mailtide.Desktop", "Host", "LoopbackSystemBrowser.cs"));
        var client = File.ReadAllText(
            Path.Combine(root, "src", "Mailtide.Desktop", "Host", "DesktopOidcOAuthClient.cs"));

        Assert.Contains("http://127.0.0.1:{port}/", loopback, StringComparison.Ordinal);
        Assert.DoesNotContain("\"mailtide://oauth/callback\"", loopback, StringComparison.Ordinal);
        Assert.DoesNotContain("\"mailtide://oauth/callback\"", client, StringComparison.Ordinal);
    }

    private static string FindAndroidProjectDirectory() =>
        Path.Combine(FindRepoRoot(), "src", "Mailtide.Android");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Mailtide.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        Assert.Fail("Could not locate repo root from the test output directory.");
        return null!;
    }
}
