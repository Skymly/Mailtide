using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;
using Mailtide.Android.Host;
using Mailtide.UI;

namespace Mailtide.Android;

[Activity(
    Label = "Mailtide",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode,
    Exported = true)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "mailtide",
    DataHost = "oauth",
    DataPathPrefix = "/callback")]
public class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        HandleOauthIntent(Intent);
        HandleInboxArrivalIntent(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleOauthIntent(intent);
        HandleInboxArrivalIntent(intent);
    }


    protected override void OnResume()
    {
        base.OnResume();
        HostBootstrap.SetAppForeground?.Invoke(true);
    }

    protected override void OnPause()
    {
        HostBootstrap.SetAppForeground?.Invoke(false);
        base.OnPause();
    }

    private static void HandleOauthIntent(Intent? intent)
    {
        var data = intent?.Data;
        if (data is null)
        {
            return;
        }

        if (!string.Equals(data.Scheme, "mailtide", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        IntentSystemBrowser.TryComplete(new System.Uri(data.ToString()!));
    }

    private static void HandleInboxArrivalIntent(Intent? intent)
    {
        var messageRaw = intent?.GetStringExtra(AndroidNotifyInboxArrival.ExtraMessageId);
        var accountRaw = intent?.GetStringExtra(AndroidNotifyInboxArrival.ExtraAccountId);
        var mailboxRaw = intent?.GetStringExtra(AndroidNotifyInboxArrival.ExtraMailboxId);
        if (messageRaw is null || accountRaw is null || mailboxRaw is null)
        {
            return;
        }

        if (!Guid.TryParse(messageRaw, out var messageId)
            || !Guid.TryParse(accountRaw, out var accountId)
            || !Guid.TryParse(mailboxRaw, out var mailboxId))
        {
            return;
        }

        HostBootstrap.InboxArrivalActivated?.Invoke(
            new InboxArrivalNotification(
                messageId,
                accountId,
                mailboxId,
                Title: string.Empty,
                Body: string.Empty,
                AccountDisplayName: null));
    }
}
