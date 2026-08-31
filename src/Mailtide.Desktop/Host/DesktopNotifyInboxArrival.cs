using System.Diagnostics;
using System.Runtime.InteropServices;
using Mailtide.UI;

namespace Mailtide.Desktop;

/// <summary>
/// Desktop Host: Windows toast / Linux Freedesktop Notifications.
/// Show and activate boundaries are injectable for tests.
/// </summary>
internal sealed class DesktopNotifyInboxArrival : INotifyInboxArrival
{
    private readonly Action<InboxArrivalNotification> _show;
    private readonly Action<InboxArrivalNotification>? _onActivated;

    public DesktopNotifyInboxArrival(
        Action<InboxArrivalNotification>? show = null,
        Action<InboxArrivalNotification>? onActivated = null)
    {
        _onActivated = onActivated;
        _show = show ?? ShowWithOs;
    }

    public Task ShowAsync(
        InboxArrivalNotification notification,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _show(notification);
        return Task.CompletedTask;
    }

    /// <summary>Test / host-activation seam when the Person opens the OS notification.</summary>
    public void Activate(InboxArrivalNotification notification) =>
        _onActivated?.Invoke(notification);

    internal static ProcessStartInfo CreateLinuxNotifyStartInfo(string title, string body)
    {
        var startInfo = new ProcessStartInfo("notify-send") { UseShellExecute = false };
        startInfo.ArgumentList.Add("--app-name=Mailtide");
        startInfo.ArgumentList.Add(title);
        startInfo.ArgumentList.Add(body);
        return startInfo;
    }

    internal static ProcessStartInfo CreateWindowsToastStartInfo(string title, string body)
    {
        var xml = "<toast><visual><binding template=\"ToastGeneric\">"
            + "<text>" + XmlEscape(title) + "</text>"
            + "<text>" + XmlEscape(body) + "</text>"
            + "</binding></visual></toast>";
        var script =
            "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null; "
            + "[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom, ContentType = WindowsRuntime] | Out-Null; "
            + "$xml = New-Object Windows.Data.Xml.Dom.XmlDocument; "
            + "$xml.LoadXml('" + EscapeForPowerShellSingleQuote(xml) + "'); "
            + "$toast = [Windows.UI.Notifications.ToastNotification]::new($xml); "
            + "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Mailtide').Show($toast)";
        var startInfo = new ProcessStartInfo("powershell")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(script);
        return startInfo;
    }

    private void ShowWithOs(InboxArrivalNotification notification)
    {
        var title = string.IsNullOrWhiteSpace(notification.Title) ? "(no subject)" : notification.Title;
        var body = notification.Body;
        if (!string.IsNullOrWhiteSpace(notification.AccountDisplayName))
        {
            body = string.IsNullOrEmpty(body)
                ? notification.AccountDisplayName
                : notification.AccountDisplayName + " · " + body;
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(CreateWindowsToastStartInfo(title, body));
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                _ = WaitForLinuxActionAsync(notification, title, body);
                return;
            }
        }
        catch
        {
            // OS notification is best-effort; browsing must not be blocked.
        }
    }

    private async Task WaitForLinuxActionAsync(
        InboxArrivalNotification notification,
        string title,
        string body)
    {
        try
        {
            var startInfo = CreateLinuxNotifyStartInfo(title, body);
            startInfo.ArgumentList.Insert(1, "default=Open");
            startInfo.ArgumentList.Insert(1, "--action");
            startInfo.ArgumentList.Insert(1, "--wait");
            startInfo.RedirectStandardOutput = true;
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return;
            }

            var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);
            if (output.Contains("default", StringComparison.OrdinalIgnoreCase))
            {
                Activate(notification);
            }
        }
        catch
        {
            // notify-send may be missing; ignore.
        }
    }

    private static string XmlEscape(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);

    private static string EscapeForPowerShellSingleQuote(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);
}