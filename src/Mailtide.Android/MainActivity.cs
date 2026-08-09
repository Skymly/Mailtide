using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;
using Mailtide.Android.Host;

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
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleOauthIntent(intent);
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
}
