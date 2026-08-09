using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Mailtide.Core;

namespace Mailtide.UI;

public partial class App : Application
{
    private MailtideApp? _core;
    private MailShellView? _shell;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var openCore = HostBootstrap.OpenCoreAsync
            ?? throw new InvalidOperationException("HostBootstrap.OpenCoreAsync was not set by the Host.");

        _core = openCore(CancellationToken.None).GetAwaiter().GetResult();
        var browse = new BrowseShell(_core);
        var compose = new ComposeOutboxShell(_core);
        _shell = new MailShellView(browse, compose);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow(_shell);
            desktop.MainWindow = mainWindow;

            mainWindow.Opened += async (_, _) =>
            {
                await _shell.InitializeBrowseAsync().ConfigureAwait(true);
            };
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime activityLifetime)
        {
            activityLifetime.MainViewFactory = () =>
            {
                _ = InitializeShellWhenAttachedAsync(_shell);
                return _shell;
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = _shell;
            _ = InitializeShellWhenAttachedAsync(_shell);
        }

        if (ApplicationLifetime is IControlledApplicationLifetime controlled)
        {
            controlled.Exit += OnExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task InitializeShellWhenAttachedAsync(MailShellView shell)
    {
        // Defer until the control is in a visual tree so bindings/layout settle.
        await Task.Yield();
        await shell.InitializeBrowseAsync().ConfigureAwait(true);
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e) => DisposeCore();

    private void DisposeCore()
    {
        if (_core is null)
        {
            return;
        }

        _core.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _core = null;
    }
}
