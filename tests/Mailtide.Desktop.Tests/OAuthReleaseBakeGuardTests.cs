namespace Mailtide.Desktop.Tests;

[TestClass]
public sealed class OAuthReleaseBakeGuardTests
{
    [TestMethod]
    public void Desktop_csproj_bakes_OAuth_client_ids_via_AssemblyMetadata()
    {
        var csproj = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Mailtide.Desktop", "Mailtide.Desktop.csproj"));

        Assert.Contains("AssemblyMetadataAttribute", csproj, StringComparison.Ordinal);
        Assert.Contains("MAILTIDE_GOOGLE_OAUTH_CLIENT_ID", csproj, StringComparison.Ordinal);
        Assert.Contains("MAILTIDE_MICROSOFT_OAUTH_CLIENT_ID", csproj, StringComparison.Ordinal);
    }

    [TestMethod]
    public void Android_csproj_still_uses_RuntimeEnvironmentVariable()
    {
        var csproj = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Mailtide.Android", "Mailtide.Android.csproj"));

        Assert.Contains("RuntimeEnvironmentVariable", csproj, StringComparison.Ordinal);
        Assert.Contains("MAILTIDE_GOOGLE_OAUTH_CLIENT_ID", csproj, StringComparison.Ordinal);
        Assert.Contains("MAILTIDE_MICROSOFT_OAUTH_CLIENT_ID", csproj, StringComparison.Ordinal);
    }

    [TestMethod]
    public void Release_workflow_reads_OAuth_client_id_secrets()
    {
        var yaml = File.ReadAllText(Path.Combine(FindRepoRoot(), ".github", "workflows", "release.yml"));

        Assert.Contains("secrets.MAILTIDE_GOOGLE_OAUTH_CLIENT_ID", yaml, StringComparison.Ordinal);
        Assert.Contains("secrets.MAILTIDE_MICROSOFT_OAUTH_CLIENT_ID", yaml, StringComparison.Ordinal);
    }

    [TestMethod]
    public void Nuke_applies_OAuth_client_id_publish_properties()
    {
        var build = File.ReadAllText(Path.Combine(FindRepoRoot(), "build", "Build.cs"));

        Assert.Contains("OAuthClientIdPublish", build, StringComparison.Ordinal);
        Assert.Contains("ApplyOAuthClientIdProperties", build, StringComparison.Ordinal);
        Assert.Contains("PublishDesktopWindows", build, StringComparison.Ordinal);
        Assert.Contains("PublishDesktopLinux", build, StringComparison.Ordinal);
        Assert.Contains("PublishAndroidApk", build, StringComparison.Ordinal);
    }

    [TestMethod]
    public void Program_applies_baked_OAuth_env_before_UI()
    {
        var program = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Mailtide.Desktop", "Program.cs"));

        Assert.Contains("BakedOAuthEnvironment.ApplyToProcessEnvironment", program, StringComparison.Ordinal);
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
