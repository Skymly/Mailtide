namespace Mailtide.Build.Tests;

[TestClass]
public sealed class NukeAndroidRestoreGuardTests
{
    [TestMethod]
    public void Restore_and_Compile_do_not_restore_or_build_the_Android_host()
    {
        var build = File.ReadAllText(Path.Combine(FindRepoRoot(), "build", "Build.cs"));

        var restore = Slice(build, "Target Restore", "Target Compile");
        Assert.DoesNotContain("RestoreAndroidHost", restore, StringComparison.Ordinal);

        var compile = Slice(build, "Target Compile", "Target Test");
        Assert.DoesNotContain("BuildAndroidHost", compile, StringComparison.Ordinal);
        Assert.DoesNotContain("RestoreAndroidHost", compile, StringComparison.Ordinal);
    }

    [TestMethod]
    public void CompileAndroid_restores_and_builds_the_Android_host()
    {
        var build = File.ReadAllText(Path.Combine(FindRepoRoot(), "build", "Build.cs"));
        var compileAndroid = Slice(build, "Target CompileAndroid", "Target PublishDesktopWindows");

        Assert.Contains("RestoreAndroidHost", compileAndroid, StringComparison.Ordinal);
        Assert.Contains("BuildAndroidHost", compileAndroid, StringComparison.Ordinal);
    }

    [TestMethod]
    public void Readme_does_not_require_Android_workload_for_Test()
    {
        var readme = File.ReadAllText(Path.Combine(FindRepoRoot(), "README.md"));

        Assert.Contains(".\\build.ps1 Test", readme, StringComparison.Ordinal);
        Assert.Contains("CompileAndroid", readme, StringComparison.Ordinal);
        Assert.Contains("dotnet workload install android", readme, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "when building the Android host or running the full Nuke `Test` / `Compile` pipeline",
            readme,
            StringComparison.Ordinal);
    }

    [TestMethod]
    public void Ci_test_job_does_not_install_Android_workload()
    {
        var ci = File.ReadAllText(Path.Combine(FindRepoRoot(), ".github", "workflows", "ci.yml"));
        var testJob = Slice(ci, "  test:", "  android:");
        var androidJob = ci[ci.IndexOf("  android:", StringComparison.Ordinal)..];

        Assert.DoesNotContain("workload install android", testJob, StringComparison.Ordinal);
        Assert.Contains("workload install android", androidJob, StringComparison.Ordinal);
        Assert.Contains("CompileAndroid", androidJob, StringComparison.Ordinal);
        Assert.Contains("build.sh Test", testJob, StringComparison.Ordinal);
    }

    private static string Slice(string text, string start, string end)
    {
        var startIndex = text.IndexOf(start, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, startIndex, $"Missing '{start}'");
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
