using Avalonia.Controls;
using Avalonia.Interactivity;
using Mailtide.Core;
using Mailtide.Core.Updates;

namespace Mailtide.UI;

public partial class MailShellView : UserControl
{
    private readonly BrowseShell? _browse;
    private readonly ComposeOutboxShell? _compose;
    private bool _suppressSelectionHandlers;
    private UpdateCheckResult? _pendingUpdate;

    /// <summary>Designer / XAML loader entry point.</summary>
    public MailShellView()
    {
        InitializeComponent();
    }

    public MailShellView(BrowseShell browse, ComposeOutboxShell compose)
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

    /// <summary>
    /// Desktop hosts may wire <see cref="HostBootstrap.CheckForDesktopUpdateAsync"/>.
    /// Failures stay silent so mail browsing is never blocked.
    /// </summary>
    public async Task CheckDesktopUpdateAsync()
    {
        var check = HostBootstrap.CheckForDesktopUpdateAsync;
        if (check is null)
        {
            HideUpdateBanner();
            return;
        }

        try
        {
            var result = await check(CancellationToken.None).ConfigureAwait(true);
            if (result.Status == UpdateCheckStatus.UpdateAvailable && result.Remote is not null)
            {
                ShowUpdateAvailable(result);
            }
            else
            {
                HideUpdateBanner();
            }
        }
        catch
        {
            HideUpdateBanner();
        }
    }

    public void ShowUpdateAvailable(UpdateCheckResult result)
    {
        _pendingUpdate = result;
        var remoteTag = result.Remote?.TagName ?? "a newer release";
        UpdateBannerText.Text =
            $"A newer Mailtide release ({remoteTag}) is available. Current install: {result.CurrentVersion}.";
        UpdateBanner.IsVisible = true;
    }

    private async void OnUpdateNowClick(object? sender, RoutedEventArgs e)
    {
        var open = HostBootstrap.OpenDesktopUpdateAsync;
        var update = _pendingUpdate;
        if (open is null || update is null)
        {
            return;
        }

        try
        {
            await open(update, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AccountActionStatus.Text = ex.Message;
        }
    }

    private void OnDismissUpdateClick(object? sender, RoutedEventArgs e) => HideUpdateBanner();

    private void HideUpdateBanner()
    {
        _pendingUpdate = null;
        if (UpdateBanner is not null)
        {
            UpdateBanner.IsVisible = false;
        }
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
        await browse.LoadAccountsAsync().ConfigureAwait(true);
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

        var browse = RequireBrowse();
        await browse.LoadAccountsAsync().ConfigureAwait(true);
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

    private async void OnAddAccountClick(object? sender, RoutedEventArgs e)
    {
        var browse = RequireBrowse();
        AccountActionStatus.Text = string.Empty;
        var dialog = new AddAccountDialog(browse);
        bool added;
        try
        {
            added = await AvaloniaOverlayDialog.ShowAsync(this, dialog, dialog.Completion).ConfigureAwait(true);
        }
        catch (InvalidOperationException)
        {
            AccountActionStatus.Text = "Unable to open Add Account dialog.";
            return;
        }

        if (!added || dialog.CreatedAccount is null)
        {
            return;
        }

        try
        {
            await browse.LoadAccountsAsync().ConfigureAwait(true);
            await browse.SelectAccountAsync(dialog.CreatedAccount.Id).ConfigureAwait(true);
            await RequireCompose().SelectAccountAsync(dialog.CreatedAccount.Id).ConfigureAwait(true);
            BindLists();
        }
        catch (Exception ex)
        {
            await browse.LoadAccountsAsync().ConfigureAwait(true);
            BindLists();
            AccountActionStatus.Text = ex.Message;
        }
    }

    private async void OnRemoveAccountClick(object? sender, RoutedEventArgs e)
    {
        var browse = RequireBrowse();
        AccountActionStatus.Text = string.Empty;
        if (AccountsList.SelectedItem is not AccountStatusRow row)
        {
            AccountActionStatus.Text = "Select an Account to remove.";
            return;
        }

        try
        {
            var removed = await browse.RemoveAccountAsync(row.Account.Id).ConfigureAwait(true);
            if (!removed)
            {
                return;
            }

            if (browse.SelectedAccountId is { } accountId)
            {
                await RequireCompose().SelectAccountAsync(accountId).ConfigureAwait(true);
            }
            else
            {
                RequireCompose().ClearSelection();
            }

            BindLists();
        }
        catch (Exception ex)
        {
            await browse.LoadAccountsAsync().ConfigureAwait(true);
            BindLists();
            AccountActionStatus.Text = ex.Message;
        }
    }

    private async void OnAccountSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionHandlers || _browse is null)
        {
            return;
        }

        if (AccountsList.SelectedItem is not AccountStatusRow row)
        {
            return;
        }

        await _browse.SelectAccountAsync(row.Account.Id).ConfigureAwait(true);
        await RequireCompose().SelectAccountAsync(row.Account.Id).ConfigureAwait(true);
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

    private async void OnMessageSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionHandlers || _browse is null)
        {
            return;
        }

        if (MessagesList.SelectedItem is not MessageInfo message)
        {
            return;
        }

        await _browse.SelectMessageAsync(message.Id).ConfigureAwait(true);
        BindLists();
    }

    private async void OnOpenAttachmentClick(object? sender, RoutedEventArgs e)
    {
        var browse = RequireBrowse();
        if (AttachmentsList.SelectedItem is not AttachmentInfo attachment)
        {
            return;
        }

        await browse.OpenAttachmentAsync(attachment.Id).ConfigureAwait(true);
        BindLists();
    }

    private static async Task ReloadBrowseSelectionAsync(BrowseShell browse, Guid accountId)
    {
        var mailboxId = browse.SelectedMailboxId;
        var messageId = browse.SelectedMessageId;
        await browse.SelectAccountAsync(accountId).ConfigureAwait(true);
        if (mailboxId is { } selectedMailboxId)
        {
            await browse.SelectMailboxAsync(selectedMailboxId).ConfigureAwait(true);
        }

        if (messageId is { } selectedMessageId
            && browse.Messages.Any(m => m.Id == selectedMessageId))
        {
            await browse.SelectMessageAsync(selectedMessageId).ConfigureAwait(true);
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
            AccountsList.ItemsSource = browse.AccountStatuses;
            MailboxesList.ItemsSource = browse.Mailboxes;
            MessagesList.ItemsSource = browse.Messages;
            AttachmentsList.ItemsSource = browse.Attachments;
            DraftsList.ItemsSource = compose.Drafts;
            OutboxList.ItemsSource = compose.OutboxItems;

            AccountsList.SelectedItem = browse.SelectedAccountId is { } accountId
                ? browse.AccountStatuses.FirstOrDefault(a => a.Account.Id == accountId)
                : null;

            MailboxesList.SelectedItem = browse.SelectedMailboxId is { } mailboxId
                ? browse.Mailboxes.FirstOrDefault(m => m.Id == mailboxId)
                : null;

            MessagesList.SelectedItem = browse.SelectedMessageId is { } messageId
                ? browse.Messages.FirstOrDefault(m => m.Id == messageId)
                : null;

            MessagesHeader.Text = browse.ShowingUnifiedInbox ? "Unified Inbox" : "Messages";
            BodyUnavailableText.IsVisible = browse.BodyUnavailable;
            MessageBodyBox.Text = browse.BodyUnavailable ? string.Empty : browse.BodyText ?? string.Empty;
            AttachmentOpenErrorText.Text = browse.AttachmentOpenError ?? string.Empty;
        }
        finally
        {
            _suppressSelectionHandlers = false;
        }
    }

    private BrowseShell RequireBrowse() =>
        _browse ?? throw new InvalidOperationException("BrowseShell was not attached to MailShellView.");

    private ComposeOutboxShell RequireCompose() =>
        _compose ?? throw new InvalidOperationException("ComposeOutboxShell was not attached to MailShellView.");
}
