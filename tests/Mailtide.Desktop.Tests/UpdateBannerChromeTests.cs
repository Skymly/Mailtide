using Mailtide.UI;

namespace Mailtide.Desktop.Tests;

[TestClass]
public sealed class UpdateBannerChromeTests
{
    [TestMethod]
    public void ShowUpdateStrip_requires_an_Account_and_stays_hidden_after_session_dismiss()
    {
        Assert.IsFalse(MailShellFormatting.ShowUpdateStrip(hasAccounts: false, dismissedThisSession: false, updateAvailable: true));
        Assert.IsFalse(MailShellFormatting.ShowUpdateStrip(hasAccounts: true, dismissedThisSession: true, updateAvailable: true));
        Assert.IsFalse(MailShellFormatting.ShowUpdateStrip(hasAccounts: true, dismissedThisSession: false, updateAvailable: false));
        Assert.IsTrue(MailShellFormatting.ShowUpdateStrip(hasAccounts: true, dismissedThisSession: false, updateAvailable: true));
    }

    [TestMethod]
    public void Auth_failure_banner_is_docked_above_the_update_strip()
    {
        var axaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Mailtide.UI", "MailShellView.axaml"));
        var commandBar = axaml.IndexOf("Name=\"ShellCommandBar\"", StringComparison.Ordinal);
        var auth = axaml.IndexOf("Name=\"AuthFailureBanner\"", StringComparison.Ordinal);
        var update = axaml.IndexOf("Name=\"UpdateBanner\"", StringComparison.Ordinal);
        Assert.IsTrue(commandBar >= 0 && auth > commandBar && update > auth);
    }

    [TestMethod]
    public void Dismiss_hides_the_update_strip_for_this_session_without_a_stored_skip()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Mailtide.UI", "MailShellView.axaml.cs"));
        Assert.Contains("_updateDismissed = true;", source, StringComparison.Ordinal);
        Assert.Contains("ApplyUpdateBannerVisibility();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("update.dismissed", source, StringComparison.Ordinal);
        Assert.DoesNotContain("skippedVersion", source, StringComparison.Ordinal);
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
