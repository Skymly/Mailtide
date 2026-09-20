namespace Mailtide.Build.Tests;

[TestClass]
public sealed class InnoInstallDirectoryTests
{
    [TestMethod]
    public void Inno_installs_binaries_beside_the_runtime_store()
    {
        var root = FindRepoRoot();
        var iss = File.ReadAllText(Path.Combine(root, "packaging", "windows", "Mailtide.iss"));
        var composition = File.ReadAllText(Path.Combine(root, "src", "Mailtide.Desktop", "DesktopComposition.cs"));

        Assert.Contains(@"DefaultDirName={localappdata}\Mailtide\app", iss, StringComparison.Ordinal);
        Assert.IsFalse(
            System.Text.RegularExpressions.Regex.IsMatch(iss, @"DefaultDirName=\{localappdata\}\\Mailtide\s"),
            "DefaultDirName must not be the runtime store directory {localappdata}\\Mailtide.");
        Assert.Contains("UsePreviousAppDir=no", iss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"Name: ""{localappdata}\Mailtide""; Flags: uninsneveruninstall", iss, StringComparison.Ordinal);
        Assert.DoesNotContain("[UninstallDelete]", iss, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("SpecialFolder.LocalApplicationData", composition, StringComparison.Ordinal);
        Assert.Contains("\"Mailtide\"", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Mailtide\\\\app\"", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Mailtide/app\"", composition, StringComparison.Ordinal);
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