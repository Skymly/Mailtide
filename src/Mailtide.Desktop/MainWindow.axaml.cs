using Avalonia.Controls;
using Avalonia.Interactivity;
using Mailtide.Core;

namespace Mailtide.Desktop;

public partial class MainWindow : Window
{
    private readonly BrowseShell? _shell;
    private bool _suppressSelectionHandlers;

    /// <summary>Designer / XAML loader entry point.</summary>
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(BrowseShell shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _shell = shell;
        InitializeComponent();
    }

    public async Task InitializeBrowseAsync()
    {
        var shell = RequireShell();
        await shell.LoadAccountsAsync().ConfigureAwait(true);
        await shell.ShowUnifiedInboxAsync().ConfigureAwait(true);
        BindLists();
    }

    private async void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        var shell = RequireShell();
        await shell.LoadAccountsAsync().ConfigureAwait(true);
        if (shell.ShowingUnifiedInbox)
        {
            await shell.ShowUnifiedInboxAsync().ConfigureAwait(true);
        }
        else if (shell.SelectedAccountId is { } accountId)
        {
            await shell.SelectAccountAsync(accountId).ConfigureAwait(true);
            if (shell.SelectedMailboxId is { } mailboxId)
            {
                await shell.SelectMailboxAsync(mailboxId).ConfigureAwait(true);
            }
        }

        BindLists();
    }

    private async void OnUnifiedInboxClick(object? sender, RoutedEventArgs e)
    {
        await RequireShell().ShowUnifiedInboxAsync().ConfigureAwait(true);
        BindLists();
    }

    private async void OnAccountSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionHandlers || _shell is null)
        {
            return;
        }

        if (AccountsList.SelectedItem is not AccountInfo account)
        {
            return;
        }

        await _shell.SelectAccountAsync(account.Id).ConfigureAwait(true);
        BindLists();
    }

    private async void OnMailboxSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionHandlers || _shell is null)
        {
            return;
        }

        if (MailboxesList.SelectedItem is not MailboxInfo mailbox)
        {
            return;
        }

        await _shell.SelectMailboxAsync(mailbox.Id).ConfigureAwait(true);
        BindLists();
    }

    private void BindLists()
    {
        var shell = RequireShell();
        _suppressSelectionHandlers = true;
        try
        {
            AccountsList.ItemsSource = shell.Accounts;
            MailboxesList.ItemsSource = shell.Mailboxes;
            MessagesList.ItemsSource = shell.Messages;

            AccountsList.SelectedItem = shell.SelectedAccountId is { } accountId
                ? shell.Accounts.FirstOrDefault(a => a.Id == accountId)
                : null;

            MailboxesList.SelectedItem = shell.SelectedMailboxId is { } mailboxId
                ? shell.Mailboxes.FirstOrDefault(m => m.Id == mailboxId)
                : null;

            MessagesHeader.Text = shell.ShowingUnifiedInbox ? "Unified Inbox" : "Messages";
        }
        finally
        {
            _suppressSelectionHandlers = false;
        }
    }

    private BrowseShell RequireShell() =>
        _shell ?? throw new InvalidOperationException("BrowseShell was not attached to MainWindow.");
}
