using System.Net;
using System.Text;
using Mailtide.Core.Updates;
using Mailtide.Desktop.Host;

namespace Mailtide.Desktop.Tests;

[TestClass]
public sealed class GitHubReleasesUpdateSourceTests
{
    private const string SampleReleaseJson = """
        {
          "tag_name": "v0.2.0",
          "html_url": "https://github.com/Skymly/Mailtide/releases/tag/v0.2.0",
          "assets": [
            {
              "name": "Mailtide-0.2.0-win-x64-setup.exe",
              "browser_download_url": "https://github.com/Skymly/Mailtide/releases/download/v0.2.0/Mailtide-0.2.0-win-x64-setup.exe"
            },
            {
              "name": "Mailtide-0.2.0-linux-x64.AppImage",
              "browser_download_url": "https://github.com/Skymly/Mailtide/releases/download/v0.2.0/Mailtide-0.2.0-linux-x64.AppImage"
            },
            {
              "name": "Mailtide-0.2.0-android.apk",
              "browser_download_url": "https://github.com/Skymly/Mailtide/releases/download/v0.2.0/Mailtide-0.2.0-android.apk"
            }
          ]
        }
        """;

    [TestMethod]
    public async Task GetLatestAsync_selects_windows_installer_asset()
    {
        using var handler = new FixedJsonHandler(SampleReleaseJson);
        using var source = new GitHubReleasesUpdateSource(
            DesktopReleasePlatform.WindowsX64,
            handler);

        var remote = await source.GetLatestAsync();

        Assert.IsNotNull(remote);
        Assert.AreEqual("v0.2.0", remote!.TagName);
        Assert.AreEqual(
            "https://github.com/Skymly/Mailtide/releases/tag/v0.2.0",
            remote.HtmlUrl);
        Assert.AreEqual("Mailtide-0.2.0-win-x64-setup.exe", remote.PlatformAsset!.Name);
        Assert.Contains("win-x64-setup.exe", remote.PlatformAsset.DownloadUrl, StringComparison.Ordinal);
        Assert.IsNotNull(handler.LastUserAgent);
        Assert.Contains("Mailtide", handler.LastUserAgent, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task GetLatestAsync_selects_linux_AppImage_asset()
    {
        using var handler = new FixedJsonHandler(SampleReleaseJson);
        using var source = new GitHubReleasesUpdateSource(
            DesktopReleasePlatform.LinuxX64,
            handler);

        var remote = await source.GetLatestAsync();

        Assert.IsNotNull(remote);
        Assert.AreEqual("Mailtide-0.2.0-linux-x64.AppImage", remote!.PlatformAsset!.Name);
        Assert.Contains("linux-x64.AppImage", remote.PlatformAsset.DownloadUrl, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task GetLatestAsync_returns_null_on_http_failure()
    {
        using var handler = new FixedJsonHandler(SampleReleaseJson, HttpStatusCode.NotFound);
        using var source = new GitHubReleasesUpdateSource(
            DesktopReleasePlatform.WindowsX64,
            handler);

        var remote = await source.GetLatestAsync();

        Assert.IsNull(remote);
    }

    [TestMethod]
    public async Task GetLatestAsync_allows_missing_platform_asset()
    {
        const string json = """
            {
              "tag_name": "v0.3.0",
              "html_url": "https://github.com/Skymly/Mailtide/releases/tag/v0.3.0",
              "assets": [
                {
                  "name": "Mailtide-0.3.0-android.apk",
                  "browser_download_url": "https://example.test/apk"
                }
              ]
            }
            """;
        using var handler = new FixedJsonHandler(json);
        using var source = new GitHubReleasesUpdateSource(
            DesktopReleasePlatform.WindowsX64,
            handler);

        var remote = await source.GetLatestAsync();

        Assert.IsNotNull(remote);
        Assert.AreEqual("v0.3.0", remote!.TagName);
        Assert.IsNull(remote.PlatformAsset);
    }

    private sealed class FixedJsonHandler : HttpMessageHandler
    {
        private readonly string _json;
        private readonly HttpStatusCode _status;

        public FixedJsonHandler(string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            _json = json;
            _status = status;
        }

        public string? LastUserAgent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastUserAgent = request.Headers.UserAgent.ToString();
            var response = new HttpResponseMessage(_status)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}

[TestClass]
public sealed class DesktopUpdateLauncherTests
{
    [TestMethod]
    public async Task OpenAsync_prefers_platform_asset_download_url()
    {
        string? opened = null;
        var launcher = new DesktopUpdateLauncher(url => opened = url);
        var update = new UpdateCheckResult(
            UpdateCheckStatus.UpdateAvailable,
            "0.1.0",
            new RemoteReleaseInfo(
                "v0.2.0",
                "https://github.com/Skymly/Mailtide/releases/tag/v0.2.0",
                new ReleaseAsset("setup.exe", "https://example.test/setup.exe")));

        await launcher.OpenAsync(update);

        Assert.AreEqual("https://example.test/setup.exe", opened);
    }

    [TestMethod]
    public async Task OpenAsync_falls_back_to_release_html_url()
    {
        string? opened = null;
        var launcher = new DesktopUpdateLauncher(url => opened = url);
        var update = new UpdateCheckResult(
            UpdateCheckStatus.UpdateAvailable,
            "0.1.0",
            new RemoteReleaseInfo(
                "v0.2.0",
                "https://github.com/Skymly/Mailtide/releases/tag/v0.2.0",
                PlatformAsset: null));

        await launcher.OpenAsync(update);

        Assert.AreEqual("https://github.com/Skymly/Mailtide/releases/tag/v0.2.0", opened);
    }

    [TestMethod]
    public async Task OpenAsync_noops_when_not_update_available()
    {
        string? opened = null;
        var launcher = new DesktopUpdateLauncher(url => opened = url);
        var update = new UpdateCheckResult(UpdateCheckStatus.UpToDate, "0.2.0");

        await launcher.OpenAsync(update);

        Assert.IsNull(opened);
    }
}
