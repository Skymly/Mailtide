using Android.App;
using Android.Content;
using Mailtide.UI;

namespace Mailtide.Android;

/// <summary>
/// Android Host: status notification that focuses MainActivity and selects the Message.
/// </summary>
internal sealed class AndroidNotifyInboxArrival : INotifyInboxArrival
{
    internal const string ExtraMessageId = "mailtide.inbox_arrival.message_id";
    internal const string ExtraAccountId = "mailtide.inbox_arrival.account_id";
    internal const string ExtraMailboxId = "mailtide.inbox_arrival.mailbox_id";
    private const string ChannelId = "mailtide.inbox";

    private readonly Context _context;

    public AndroidNotifyInboxArrival(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context.ApplicationContext ?? context;
    }

    public Task ShowAsync(
        InboxArrivalNotification notification,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            EnsureChannel();
            var intent = new Intent(_context, typeof(MainActivity));
            intent.AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
            intent.PutExtra(ExtraMessageId, notification.MessageId.ToString());
            intent.PutExtra(ExtraAccountId, notification.AccountId.ToString());
            intent.PutExtra(ExtraMailboxId, notification.MailboxId.ToString());

            var pending = PendingIntent.GetActivity(
                _context,
                notification.MessageId.GetHashCode(),
                intent,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

            var title = string.IsNullOrWhiteSpace(notification.Title)
                ? "(no subject)"
                : notification.Title;
            var body = notification.Body;
            var builder = CreateBuilder()
                .SetContentTitle(title)
                .SetContentText(body)
                .SetSmallIcon(global::Android.Resource.Drawable.StatNotifyMore)
                .SetAutoCancel(true)
                .SetContentIntent(pending);

            var manager = NotificationManager.FromContext(_context);
            manager?.Notify(notification.MessageId.GetHashCode(), builder.Build());
        }
        catch
        {
            // Status notification is best-effort; browsing must not be blocked.
        }

        return Task.CompletedTask;
    }

    private Notification.Builder CreateBuilder()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return new Notification.Builder(_context, ChannelId);
        }

#pragma warning disable CS0618
        return new Notification.Builder(_context);
#pragma warning restore CS0618
    }

    private void EnsureChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return;
        }

        var manager = NotificationManager.FromContext(_context);
        if (manager is null || manager.GetNotificationChannel(ChannelId) is not null)
        {
            return;
        }

        var channel = new NotificationChannel(
            ChannelId,
            "Inbox",
            NotificationImportance.Default);
        manager.CreateNotificationChannel(channel);
    }
}