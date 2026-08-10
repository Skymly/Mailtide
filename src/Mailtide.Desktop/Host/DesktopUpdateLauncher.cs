using System.Diagnostics;
using System.Runtime.InteropServices;
using Mailtide.Core.Updates;

namespace Mailtide.Desktop.Host;

/// <summary>
/// Opens the Person's update flow in the system browser / download handler.
/// </summary>
public sealed class DesktopUpdateLauncher : IUpdateLauncher
{
    private readonly Action<string> _openUrl;

    public DesktopUpdateLauncher(Action<string>? openUrl = null)
    {
        _openUrl = openUrl ?? OpenSystemUrl;
    }

    public Task OpenAsync(
        UpdateCheckResult update,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (update.Status != UpdateCheckStatus.UpdateAvailable || update.Remote is null)
        {
            return Task.CompletedTask;
        }

        var url = update.Remote.PlatformAsset?.DownloadUrl ?? update.Remote.HtmlUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return Task.CompletedTask;
        }

        _openUrl(url);
        return Task.CompletedTask;
    }

    internal static void OpenSystemUrl(string url)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Process.Start("xdg-open", url);
            return;
        }

        throw new PlatformNotSupportedException(
            "Opening update URLs is only supported on Windows and Linux.");
    }
}
