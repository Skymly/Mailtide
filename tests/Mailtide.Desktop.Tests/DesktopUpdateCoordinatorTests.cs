using Mailtide.Core.Updates;
using Mailtide.Desktop.Host;

namespace Mailtide.Desktop.Tests;

[TestClass]
public sealed class DesktopUpdateCoordinatorTests
{
    [TestMethod]
    public async Task CheckAsync_reports_update_when_remote_is_newer()
    {
        var remote = new RemoteReleaseInfo(
            "v0.2.0",
            "https://github.com/Skymly/Mailtide/releases/tag/v0.2.0",
            new ReleaseAsset("setup.exe", "https://example.test/setup.exe"));
        var coordinator = new DesktopUpdateCoordinator(
            new FixedSource(remote),
            currentVersion: () => "0.1.0");

        var result = await coordinator.CheckAsync();

        Assert.AreEqual(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.AreEqual("0.1.0", result.CurrentVersion);
        Assert.AreSame(remote, result.Remote);
    }

    [TestMethod]
    public async Task CheckAsync_reports_unavailable_when_source_returns_null()
    {
        var coordinator = new DesktopUpdateCoordinator(
            new FixedSource(null),
            currentVersion: () => "0.1.0");

        var result = await coordinator.CheckAsync();

        Assert.AreEqual(UpdateCheckStatus.Unavailable, result.Status);
    }

    private sealed class FixedSource(RemoteReleaseInfo? remote) : IReleaseUpdateSource
    {
        public Task<RemoteReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(remote);
    }
}
