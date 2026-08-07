using Avalonia.Controls;
using Avalonia.Interactivity;
using Mailtide.Core;

namespace Mailtide.Desktop;

public partial class MainWindow : Window
{
    private readonly BrowseShell? _browse;
    private readonly ComposeOutboxShell? _compose;
    private bool _suppressSelectionHandlers;

    /// <summary>Designer / XAML loader entry point.</summary>
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(BrowseShell browse, ComposeOutboxShell compose)
    {
        ArgumentNullException.ThrowIfNull(browse);
        ArgumentNullException.ThrowIfNull(compose);
        _browse = browse;
        _compose = compose;
        InitializeComponent();
    }

    public async Task InitializeBrowseAsync()
    {
        var browse = RequireBrowse();
        await browse.LoadAccountsAsync().ConfigureAwait(true);
        await browse.ShowUnifiedInboxAsync().ConfigureAwait(true);
        BindLists();
    }

    private async void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        var browse = RequireBrowse();
        await browse.LoadAccountsAsync().ConfigureAwait(true);
        if (browse.ShowingUnifiedInbox)
        {
            await browse.ShowUnifiedInboxAsync().ConfigureAwait(true);
        }
        else if (browse.SelectedAccountId is { } accountId)
        {
            await ReloadBrowseSelectionAsync(browse, accountId).ConfigureAwait(true);
            await RequireCompose().SelectAccountAsync(accountId).ConfigureAwait(true);
        }

        BindLists();
    }

    private async void OnUnifiedInboxClick(object? sender, RoutedEventArgs e)
    {
        await RequireBrowse().ShowUnifiedInboxAsync().ConfigureAwait(true);
        BindLists();
    }

    private async void OnSyncNowClick(object? sender, RoutedEventArgs e)
    {
        var compose = RequireCompose();
        if (compose.SelectedAccountId is null)
        {
            return;
        }

        await compose.SyncNowAsync().ConfigureAwait(true);

        var browse = RequireBrowse();
        if (browse.SelectedAccountId is { } accountId)
        {
            await ReloadBrowseSelectionAsync(browse, accountId).ConfigureAwait(true);
        }

        BindLists();
    }

    private async void OnSendNowClick(object? sender, RoutedEventArgs e)
    {
        var compose = RequireCompose();
        if (compose.SelectedAccountId is null)
        {
            return;
        }

        await compose.SendNowAsync().ConfigureAwait(true);
        BindLists();
    }

    private async void OnSaveDraftClick(object? sender, RoutedEventArgs e)
    {
        var compose = RequireCompose();
        if (compose.SelectedAccountId is null)
        {
            return;
        }

        await compose
            .SaveDraftAsync(ComposeToBox.Text ?? string.Empty, ComposeSubjectBox.Text ?? string.Empty, ComposeBodyBox.Text ?? string.Empty)
            .ConfigureAwait(true);
        BindLists();
        DraftsList.SelectedItem = compose.Drafts.FirstOrDefault();
    }

    private async void OnSendDraftClick(object? sender, RoutedEventArgs e)
    {
        var compose = RequireCompose();
        if (compose.SelectedAccountId is null)
        {
            return;
        }

        var draft = DraftsList.SelectedItem as DraftInfo;
        if (draft is null)
        {
            await compose
                .SaveDraftAsync(ComposeToBox.Text ?? string.Empty, ComposeSubjectBox.Text ?? string.Empty, ComposeBodyBox.Text ?? string.Empty)
                .ConfigureAwait(true);
            draft = compose.Drafts.FirstOrDefault();
            if (draft is null)
            {
                return;
            }
        }

        await compose.SendAsync(draft.Id).ConfigureAwait(true);
        ClearComposeFields();
        BindLists();
    }

    private async void OnRetryOutboxClick(object? sender, RoutedEventArgs e)
    {
        var compose = RequireCompose();
        if (OutboxList.SelectedItem is not OutboxItemInfo item || item.State != OutboxItemState.Failed)
        {
            return;
        }

        await compose.RetryOutboxItemAsync(item.Id).ConfigureAwait(true);
        BindLists();
    }

    private async void OnDiscardOutboxClick(object? sender, RoutedEventArgs e)
    {
        var compose = RequireCompose();
        if (OutboxList.SelectedItem is not OutboxItemInfo item)
        {
            return;
        }

        await compose.DiscardOutboxItemAsync(item.Id).ConfigureAwait(true);
        BindLists();
    }

    private async void OnAccountSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionHandlers || _browse is null)
        {
            return;
        }

        if (AccountsList.SelectedItem is not AccountInfo account)
        {
            return;
        }

        await _browse.SelectAccountAsync(account.Id).ConfigureAwait(true);
        await RequireCompose().SelectAccountAsync(account.Id).ConfigureAwait(true);
        BindLists();
    }

    private async void OnMailboxSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionHandlers || _browse is null)
        {
            return;
        }

        if (MailboxesList.SelectedItem is not MailboxInfo mailbox)
        {
            return;
        }

        await _browse.SelectMailboxAsync(mailbox.Id).ConfigureAwait(true);
        BindLists();
    }

    private static async Task ReloadBrowseSelectionAsync(BrowseShell browse, Guid accountId)
    {
        var mailboxId = browse.SelectedMailboxId;
        await browse.SelectAccountAsync(accountId).ConfigureAwait(true);
        if (mailboxId is { } selectedMailboxId)
        {
            await browse.SelectMailboxAsync(selectedMailboxId).ConfigureAwait(true);
        }
    }

    private void ClearComposeFields()
    {
        ComposeToBox.Text = string.Empty;
        ComposeSubjectBox.Text = string.Empty;
        ComposeBodyBox.Text = string.Empty;
    }

    private void BindLists()
    {
        var browse = RequireBrowse();
        var compose = RequireCompose();
        _suppressSelectionHandlers = true;
        try
        {
            AccountsList.ItemsSource = browse.Accounts;
            MailboxesList.ItemsSource = browse.Mailboxes;
            MessagesList.ItemsSource = browse.Messages;
            DraftsList.ItemsSource = compose.Drafts;
            OutboxList.ItemsSource = compose.OutboxItems;

            AccountsList.SelectedItem = browse.SelectedAccountId is { } accountId
                ? browse.Accounts.FirstOrDefault(a => a.Id == accountId)
                : null;

            MailboxesList.SelectedItem = browse.SelectedMailboxId is { } mailboxId
                ? browse.Mailboxes.FirstOrDefault(m => m.Id == mailboxId)
                : null;

            MessagesHeader.Text = browse.ShowingUnifiedInbox ? "Unified Inbox" : "Messages";
        }
        finally
        {
            _suppressSelectionHandlers = false;
        }
    }

    private BrowseShell RequireBrowse() =>
        _browse ?? throw new InvalidOperationException("BrowseShell was not attached to MainWindow.");

    private ComposeOutboxShell RequireCompose() =>
        _compose ?? throw new InvalidOperationException("ComposeOutboxShell was not attached to MainWindow.");
}
