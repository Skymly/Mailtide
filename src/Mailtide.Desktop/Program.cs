using Avalonia;
using Mailtide.Desktop.Host;
using Mailtide.UI;
using System;

namespace Mailtide.Desktop;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and things might break.
    [STAThread]
    public static void Main(string[] args)
    {
        HostBootstrap.OpenCoreAsync = DesktopComposition.OpenCoreAsync;
        HostBootstrap.OpenDownloadedAttachment = new DesktopOpenDownloadedAttachment();

        var updateSource = new GitHubReleasesUpdateSource(GitHubReleasesUpdateSource.DetectPlatform());
        var updateCoordinator = new DesktopUpdateCoordinator(updateSource);
        var updateLauncher = new DesktopUpdateLauncher();
        HostBootstrap.CheckForDesktopUpdateAsync = updateCoordinator.CheckAsync;
        HostBootstrap.OpenDesktopUpdateAsync = updateLauncher.OpenAsync;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .LogToTrace();
}
