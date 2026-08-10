using System.Xml.Linq;

namespace Mailtide.Android.Tests;

[TestClass]
public sealed class ApkPackagingGuardTests
{
    [TestMethod]
    public void Android_csproj_ships_sideload_apk_not_aab_or_play_store()
    {
        var csprojPath = Path.Combine(FindAndroidProjectDirectory(), "Mailtide.Android.csproj");
        var document = XDocument.Load(csprojPath);
        var text = File.ReadAllText(csprojPath);

        var packageFormat = document.Descendants("AndroidPackageFormat").FirstOrDefault()?.Value;
        Assert.AreEqual("apk", packageFormat, "v1 ships a sideload APK via GitHub Releases.");

        Assert.DoesNotContain("AndroidPackageFormat>aab", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PlayStore", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GooglePlay", text, StringComparison.OrdinalIgnoreCase);
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
}
