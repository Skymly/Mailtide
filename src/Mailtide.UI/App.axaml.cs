using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
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

        browse.AccountRemovalConfirmation = new AvaloniaConfirmAccountRemoval(() => _shell);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow(_shell);
            desktop.MainWindow = mainWindow;

            mainWindow.Opened += async (_, _) =>
            {
                await _shell.InitializeBrowseAsync().ConfigureAwait(true);
                StartForegroundSync();
                await _shell.CheckDesktopUpdateAsync().ConfigureAwait(true);
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

    private async Task InitializeShellWhenAttachedAsync(MailShellView shell)
    {
        // Defer until the control is in a visual tree so bindings/layout settle.
        await Task.Yield();
        await shell.InitializeBrowseAsync().ConfigureAwait(true);
        StartForegroundSync();
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e) => DisposeCore();

    private void StartForegroundSync()
    {
        if (_core is null)
        {
            return;
        }

        _core.AccountWorkCompleted -= OnAccountWorkCompleted;
        _core.AccountWorkCompleted += OnAccountWorkCompleted;
        HostBootstrap.SetAppForeground = OnHostForegroundChanged;
        _core.StartForegroundSync();
    }

    private void OnHostForegroundChanged(bool isForeground)
    {
        if (_core is null)
        {
            return;
        }

        if (isForeground)
        {
            _core.StartForegroundSync();
            return;
        }

        _ = _core.StopForegroundSyncAsync();
    }

    private void OnAccountWorkCompleted(object? sender, Guid accountId)
    {
        _ = accountId;
        var shell = _shell;
        if (shell is null)
        {
            return;
        }

        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await shell.RefreshAfterAccountWorkAsync().ConfigureAwait(true);
        });
    }

    private void DisposeCore()
    {
        if (_core is null)
        {
            return;
        }

        _core.AccountWorkCompleted -= OnAccountWorkCompleted;
        HostBootstrap.SetAppForeground = null;
        _core.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _core = null;
    }
}
