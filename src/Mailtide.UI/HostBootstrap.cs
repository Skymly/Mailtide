using Mailtide.Core;
using Mailtide.Core.Updates;

namespace Mailtide.UI;

/// <summary>
/// Host-provided composition hook so shared UI never references Desktop/Android types.
/// </summary>
public static class HostBootstrap
{
    public static Func<CancellationToken, Task<MailtideApp>>? OpenCoreAsync { get; set; }

    public static IOpenDownloadedAttachment? OpenDownloadedAttachment { get; set; }

    /// <summary>
    /// Host port: OS notification for a newly arrived Inbox Message.
    /// </summary>
    public static INotifyInboxArrival? NotifyInboxArrival { get; set; }

    /// <summary>
    /// Hosts invoke this when the Person opens an Inbox arrival notification.
    /// </summary>
    public static Action<InboxArrivalNotification>? InboxArrivalActivated { get; set; }

    /// <summary>
    /// Desktop-only: check GitHub Releases for a newer build. Android leaves this null.
    /// Failures should resolve to <see cref="UpdateCheckStatus.Unavailable"/> rather than throw.
    /// </summary>
    public static Func<CancellationToken, Task<UpdateCheckResult>>? CheckForDesktopUpdateAsync { get; set; }

    /// <summary>
    /// Desktop-only: open the Person's update download / release page.
    /// </summary>
    public static Func<UpdateCheckResult, CancellationToken, Task>? OpenDesktopUpdateAsync { get; set; }

    /// <summary>
    /// Hosts report process foreground/background so Core self-drive stays foreground-only.
    /// </summary>
    public static Action<bool>? SetAppForeground { get; set; }
}
