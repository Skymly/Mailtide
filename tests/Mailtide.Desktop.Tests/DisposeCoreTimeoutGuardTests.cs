namespace Mailtide.Desktop.Tests;

[TestClass]
public sealed class DisposeCoreTimeoutGuardTests
{
    [TestMethod]
    public void DisposeCore_does_not_GetResult_unbounded_on_core_dispose()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Mailtide.UI", "App.axaml.cs"));
        Assert.Contains("Wait(TimeSpan.FromSeconds(3))", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DisposeAsync().AsTask().GetAwaiter().GetResult()",
            source,
            StringComparison.Ordinal);
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

        Assert.Fail("Could not locate repo root.");
        return null!;
    }
}
