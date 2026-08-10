namespace Mailtide.Android.Tests;

[TestClass]
public sealed class NoInAppUpdaterGuardTests
{
    private static readonly string[] ForbiddenTokens =
    [
        "IReleaseUpdateSource",
        "IUpdateLauncher",
        "UpdateChecker",
        "GitHubReleasesUpdateSource",
        "DesktopUpdateLauncher",
        "DesktopUpdateCoordinator",
        "CheckForDesktopUpdateAsync",
        "OpenDesktopUpdateAsync",
        "Velopack",
        "Squirrel",
    ];

    [TestMethod]
    public void Android_host_sources_do_not_wire_desktop_self_update()
    {
        var androidDir = FindAndroidProjectDirectory();
        var hits = new List<string>();

        foreach (var path in Directory.EnumerateFiles(androidDir, "*.*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(path);
            if (ext is not (".cs" or ".csproj" or ".xml" or ".axaml"))
            {
                continue;
            }

            var text = File.ReadAllText(path);
            foreach (var token in ForbiddenTokens)
            {
                if (text.Contains(token, StringComparison.Ordinal))
                {
                    hits.Add($"{Path.GetRelativePath(androidDir, path)}: {token}");
                }
            }
        }

        Assert.IsEmpty(
            hits,
            "Android must not include an in-app self-updater. Hits:\n" + string.Join("\n", hits));
    }

    [TestMethod]
    public void Android_host_does_not_set_desktop_update_HostBootstrap_hooks()
    {
        var androidDir = FindAndroidProjectDirectory();
        var sources = Directory
            .EnumerateFiles(androidDir, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText);

        foreach (var text in sources)
        {
            Assert.DoesNotContain(
                "HostBootstrap.CheckForDesktopUpdateAsync",
                text,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "HostBootstrap.OpenDesktopUpdateAsync",
                text,
                StringComparison.Ordinal);
        }
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
