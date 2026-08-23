using Avalonia.Controls;
using Avalonia.Input;
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

    public async Task RefreshAfterAccountWorkAsync()
    {
        var browse = RequireBrowse();
        await browse.RefreshAfterAccountWorkAsync().ConfigureAwait(true);
        var compose = RequireCompose();
        if (compose.SelectedAccountId is { } composeAccountId)
        {
            await compose.SelectAccountAsync(composeAccountId).ConfigureAwait(true);
        }

        BindLists();
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

    private void OnNewDraftClick(object? sender, RoutedEventArgs e)
    {
        var compose = RequireCompose();
        if (compose.SelectedAccountId is null)
        {
            return;
        }

        compose.StartNewDraft();
        ClearComposeFields();
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
            .SaveDraftAsync(ComposeToBox.Text ?? string.Empty, ComposeSubjectBox.Text ?? string.Empty, ComposeBodyBox.Text ?? string.Empty, ComposeCcBox.Text ?? string.Empty, ComposeBccBox.Text ?? string.Empty)
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

        await compose
            .SaveDraftAsync(ComposeToBox.Text ?? string.Empty, ComposeSubjectBox.Text ?? string.Empty, ComposeBodyBox.Text ?? string.Empty, ComposeCcBox.Text ?? string.Empty, ComposeBccBox.Text ?? string.Empty)
            .ConfigureAwait(true);
        if (compose.SelectedDraftId is not { } draftId)
        {
            return;
        }

        await compose.SendAsync(draftId).ConfigureAwait(true);
        ClearComposeFields();
        BindLists();
    }

    private async void OnDiscardDraftClick(object? sender, RoutedEventArgs e)
    {
        var compose = RequireCompose();
        if (compose.SelectedDraftId is not { } draftId)
        {
            if (DraftsList.SelectedItem is DraftInfo selected)
            {
                draftId = selected.Id;
            }
            else
            {
                return;
            }
        }

        await compose.DiscardDraftAsync(draftId).ConfigureAwait(true);
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

    private async void OnDraftSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionHandlers)
        {
            return;
        }

        if (DraftsList.SelectedItem is not DraftInfo draft)
        {
            return;
        }

        var compose = RequireCompose();
        var loaded = await compose.SelectDraftAsync(draft.Id).ConfigureAwait(true);
        ComposeToBox.Text = string.Join(", ", loaded.ToAddresses);
        ComposeCcBox.Text = string.Join(", ", loaded.CcAddresses);
        ComposeBccBox.Text = string.Join(", ", loaded.BccAddresses);
        ComposeSubjectBox.Text = loaded.Subject;
        ComposeBodyBox.Text = loaded.BodyText;
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

    private async void OnMessageSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        var browse = RequireBrowse();
        await browse.SearchAsync(MessageSearchBox.Text ?? string.Empty).ConfigureAwait(true);
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

    private async void OnReplyAllClick(object? sender, RoutedEventArgs e)
    {
        var browse = RequireBrowse();
        if (browse.SelectedMessageId is not { } messageId)
        {
            return;
        }

        var message = browse.Messages.FirstOrDefault(m => m.Id == messageId);
        if (message is null)
        {
            return;
        }

        var compose = RequireCompose();
        var draft = await compose
            .StartReplyAllAsync(message.AccountId, messageId)
            .ConfigureAwait(true);

        ComposeToBox.Text = string.Join(", ", draft.ToAddresses);
        ComposeCcBox.Text = string.Join(", ", draft.CcAddresses);
        ComposeBccBox.Text = string.Join(", ", draft.BccAddresses);
        ComposeSubjectBox.Text = draft.Subject;
        ComposeBodyBox.Text = draft.BodyText;
        BindLists();
        DraftsList.SelectedItem = compose.Drafts.FirstOrDefault(d => d.Id == draft.Id);
    }
    private async void OnReplyClick(object? sender, RoutedEventArgs e)
    {
        var browse = RequireBrowse();
        if (browse.SelectedMessageId is not { } messageId)
        {
            return;
        }

        var message = browse.Messages.FirstOrDefault(m => m.Id == messageId);
        if (message is null)
        {
            return;
        }

        var compose = RequireCompose();
        var draft = await compose
            .StartReplyAsync(message.AccountId, messageId)
            .ConfigureAwait(true);

        ComposeToBox.Text = string.Join(", ", draft.ToAddresses);
        ComposeCcBox.Text = string.Join(", ", draft.CcAddresses);
        ComposeBccBox.Text = string.Join(", ", draft.BccAddresses);
        ComposeSubjectBox.Text = draft.Subject;
        ComposeBodyBox.Text = draft.BodyText;
        BindLists();
        DraftsList.SelectedItem = compose.Drafts.FirstOrDefault(d => d.Id == draft.Id);
    }
    private async void OnForwardClick(object? sender, RoutedEventArgs e)
    {
        var browse = RequireBrowse();
        if (browse.SelectedMessageId is not { } messageId)
        {
            return;
        }

        var message = browse.Messages.FirstOrDefault(m => m.Id == messageId);
        if (message is null)
        {
            return;
        }

        var compose = RequireCompose();
        var draft = await compose
            .StartForwardAsync(message.AccountId, messageId)
            .ConfigureAwait(true);

        ComposeToBox.Text = string.Join(", ", draft.ToAddresses);
        ComposeCcBox.Text = string.Join(", ", draft.CcAddresses);
        ComposeBccBox.Text = string.Join(", ", draft.BccAddresses);
        ComposeSubjectBox.Text = draft.Subject;
        ComposeBodyBox.Text = draft.BodyText;
        BindLists();
        DraftsList.SelectedItem = compose.Drafts.FirstOrDefault(d => d.Id == draft.Id);
    }
    private async void OnMarkUnreadClick(object? sender, RoutedEventArgs e)
    {
        var browse = RequireBrowse();
        if (browse.SelectedMessageId is null)
        {
            return;
        }

        await browse.MarkSelectedUnreadAsync().ConfigureAwait(true);
        BindLists();
    }

    private async void OnFlagClick(object? sender, RoutedEventArgs e)
    {
        var browse = RequireBrowse();
        if (browse.SelectedMessageId is null)
        {
            return;
        }

        await browse.ToggleSelectedFlagAsync().ConfigureAwait(true);
        BindLists();
    }

    private async void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        var browse = RequireBrowse();
        if (browse.SelectedMessageId is null)
        {
            return;
        }

        await browse.MoveSelectedToTrashAsync().ConfigureAwait(true);
        BindLists();
    }

    private async void OnRestoreClick(object? sender, RoutedEventArgs e)
    {
        var browse = RequireBrowse();
        if (browse.SelectedMessageId is null)
        {
            return;
        }

        await browse.RestoreSelectedFromTrashAsync().ConfigureAwait(true);
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
        ComposeCcBox.Text = string.Empty;
        ComposeBccBox.Text = string.Empty;
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
            DraftsList.SelectedItem = compose.SelectedDraftId is { } selectedDraftId
                ? compose.Drafts.FirstOrDefault(d => d.Id == selectedDraftId)
                : null;

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
            BindMessageBody(browse);
            AttachmentOpenErrorText.Text = browse.AttachmentOpenError ?? string.Empty;
        }
        finally
        {
            _suppressSelectionHandlers = false;
        }
    }

    private void BindMessageBody(BrowseShell browse)
    {
        var html = browse.BodyHtml;
        var showHtml = !browse.BodyUnavailable && !string.IsNullOrEmpty(html);
        MessageHtmlView.IsVisible = showHtml;
        MessageBodyBox.IsVisible = !showHtml;
        MessageBodyBox.Text = browse.BodyUnavailable ? string.Empty : browse.BodyText ?? string.Empty;
        if (!showHtml)
        {
            return;
        }

        try
        {
            MessageHtmlView.NavigateToString(HtmlRemoteContentPolicy.WrapForOfflineRender(html!));
        }
        catch
        {
            MessageHtmlView.IsVisible = false;
            MessageBodyBox.IsVisible = true;
        }
    }

    private void OnMessageHtmlNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
    {
        if (!HtmlRemoteContentPolicy.IsAllowed(e.Request))
        {
            e.Cancel = true;
        }
    }

    private void OnMessageHtmlNewWindowRequested(object? sender, WebViewNewWindowRequestedEventArgs e)
    {
        if (!HtmlRemoteContentPolicy.IsAllowed(e.Request))
        {
            e.Handled = true;
        }
    }

    private BrowseShell RequireBrowse() =>
        _browse ?? throw new InvalidOperationException("BrowseShell was not attached to MailShellView.");

    private ComposeOutboxShell RequireCompose() =>
        _compose ?? throw new InvalidOperationException("ComposeOutboxShell was not attached to MailShellView.");
}
