namespace Mailtide.Build.Tests;

[TestClass]
public sealed class AppImageToolPinTests
{
    [TestMethod]
    public void EnsureAppImageTool_pins_a_release_tag_and_verifies_sha256()
    {
        var build = File.ReadAllText(Path.Combine(FindRepoRoot(), "build", "Build.cs"));
        var ensure = Slice(build, "AbsolutePath EnsureAppImageTool()", null);

        Assert.DoesNotContain("/continuous/", ensure, StringComparison.Ordinal);
        Assert.Contains(
            "https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-x86_64.AppImage",
            ensure,
            StringComparison.Ordinal);
        Assert.Contains(
            "ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0",
            ensure,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SHA256", ensure, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void PackAppImage_still_uses_EnsureAppImageTool()
    {
        var build = File.ReadAllText(Path.Combine(FindRepoRoot(), "build", "Build.cs"));
        var pack = Slice(build, "Target PackAppImage", "Target PublishAndroidApk");

        Assert.Contains("EnsureAppImageTool()", pack, StringComparison.Ordinal);
    }

    private static string Slice(string text, string start, string? end)
    {
        var startIndex = text.IndexOf(start, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, startIndex, $"Missing '{start}'");
        if (end is null)
        {
            return text[startIndex..];
        }

        var endIndex = text.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.IsGreaterThan(startIndex, endIndex, $"Missing '{end}' after '{start}'");
        return text[startIndex..endIndex];
    }

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