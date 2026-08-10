using Mailtide.Core.Updates;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class UpdateCheckerTests
{
    [TestMethod]
    public void Normalize_strips_leading_v_like_ReleaseArtifacts()
    {
        Assert.AreEqual("1.2.3", ReleaseVersion.Normalize("v1.2.3"));
        Assert.AreEqual("1.2.3", ReleaseVersion.Normalize("V1.2.3"));
        Assert.AreEqual("0.1.0", ReleaseVersion.Normalize("0.1.0"));
    }

    [TestMethod]
    public void Normalize_defaults_local_when_missing()
    {
        Assert.AreEqual(ReleaseVersion.LocalFallback, ReleaseVersion.Normalize(null));
        Assert.AreEqual(ReleaseVersion.LocalFallback, ReleaseVersion.Normalize(""));
        Assert.AreEqual(ReleaseVersion.LocalFallback, ReleaseVersion.Normalize("   "));
    }

    [TestMethod]
    public void Evaluate_reports_up_to_date_when_versions_match()
    {
        var remote = new RemoteReleaseInfo(
            TagName: "v0.1.0",
            HtmlUrl: "https://github.com/Skymly/Mailtide/releases/tag/v0.1.0",
            PlatformAsset: new ReleaseAsset(
                "Mailtide-0.1.0-win-x64-setup.exe",
                "https://example.test/setup.exe"));

        var result = UpdateChecker.Evaluate("0.1.0", remote);

        Assert.AreEqual(UpdateCheckStatus.UpToDate, result.Status);
        Assert.AreEqual("0.1.0", result.CurrentVersion);
    }

    [TestMethod]
    public void Evaluate_reports_update_available_when_remote_is_newer()
    {
        var remote = new RemoteReleaseInfo(
            TagName: "v0.2.0",
            HtmlUrl: "https://github.com/Skymly/Mailtide/releases/tag/v0.2.0",
            PlatformAsset: new ReleaseAsset(
                "Mailtide-0.2.0-win-x64-setup.exe",
                "https://example.test/setup.exe"));

        var result = UpdateChecker.Evaluate("0.1.0", remote);

        Assert.AreEqual(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.AreSame(remote, result.Remote);
    }

    [TestMethod]
    public void Evaluate_reports_up_to_date_when_remote_is_older()
    {
        var remote = new RemoteReleaseInfo(
            TagName: "v0.1.0",
            HtmlUrl: "https://github.com/Skymly/Mailtide/releases/tag/v0.1.0",
            PlatformAsset: null);

        var result = UpdateChecker.Evaluate("0.2.0", remote);

        Assert.AreEqual(UpdateCheckStatus.UpToDate, result.Status);
    }

    [TestMethod]
    public void Evaluate_treats_local_build_as_outdated_when_remote_exists()
    {
        var remote = new RemoteReleaseInfo(
            TagName: "v0.1.0",
            HtmlUrl: "https://github.com/Skymly/Mailtide/releases/tag/v0.1.0",
            PlatformAsset: new ReleaseAsset(
                "Mailtide-0.1.0-linux-x64.AppImage",
                "https://example.test/app.AppImage"));

        var result = UpdateChecker.Evaluate("0.0.0-local", remote);

        Assert.AreEqual(UpdateCheckStatus.UpdateAvailable, result.Status);
    }

    [TestMethod]
    public void Evaluate_reports_unavailable_when_remote_is_missing()
    {
        var result = UpdateChecker.Evaluate("0.1.0", remote: null);

        Assert.AreEqual(UpdateCheckStatus.Unavailable, result.Status);
        Assert.IsNull(result.Remote);
    }

    [TestMethod]
    public void Evaluate_still_offers_update_when_platform_asset_is_missing()
    {
        var remote = new RemoteReleaseInfo(
            TagName: "v1.0.0",
            HtmlUrl: "https://github.com/Skymly/Mailtide/releases/tag/v1.0.0",
            PlatformAsset: null);

        var result = UpdateChecker.Evaluate("0.9.0", remote);

        Assert.AreEqual(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.IsNull(result.Remote!.PlatformAsset);
    }
}
