using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Mailtide.UI;

namespace Mailtide.Android;

[Application]
public class MailtideAndroidApplication : AvaloniaAndroidApplication<App>
{
    protected MailtideAndroidApplication(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    public override void OnCreate()
    {
        HostBootstrap.OpenCoreAsync = ct => AndroidComposition.OpenCoreAsync(this, ct);
        base.OnCreate();
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder).WithInterFont();
}
