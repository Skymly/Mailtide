using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Mailtide.Core;

namespace Mailtide.Desktop;

public partial class App : Application
{
    private MailtideApp? _core;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _core = DesktopComposition.OpenCoreAsync().GetAwaiter().GetResult();
            var browse = new BrowseShell(_core);
            var compose = new ComposeOutboxShell(_core);
            var mainWindow = new MainWindow(browse, compose);
            desktop.MainWindow = mainWindow;
            desktop.Exit += OnExit;

            mainWindow.Opened += async (_, _) =>
            {
                await mainWindow.InitializeBrowseAsync().ConfigureAwait(true);
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        if (_core is null)
        {
            return;
        }

        _core.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _core = null;
    }
}
