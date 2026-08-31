namespace Mailtide.UI;

/// <summary>
/// Host port: show an OS notification for a newly arrived Inbox Message.
/// </summary>
public interface INotifyInboxArrival
{
    Task ShowAsync(
        InboxArrivalNotification notification,
        CancellationToken cancellationToken = default);
}