using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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

    public async Task OpenArrivedMessageAsync(
        Guid accountId,
        Guid mailboxId,
        Guid messageId)
    {
        var browse = RequireBrowse();
        await browse.OpenArrivedMessageAsync(accountId, mailboxId, messageId).ConfigureAwait(true);
        var compose = RequireCompose();
        await compose.SelectAccountAsync(accountId).ConfigureAwait(true);
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

    private void BindAuthFailureBanner()
    {
        if (AuthFailureBanner is null)
        {
            return;
        }

        var prompt = _browse?.AuthenticationFailurePrompt;
        if (prompt is null)
        {
            AuthFailureBanner.IsVisible = false;
            return;
        }

        AuthFailureBannerText.Text =
            $"Authentication failed for {prompt.DisplayName}. Sign in again.";
        AuthFailureActionButton.Content = prompt.Action == AuthenticationFailureAction.Reauthorize
            ? "Reauthorize"
            : "Edit";
        AuthFailureBanner.IsVisible = true;
    }

    private async void OnAuthFailureActionClick(object? sender, RoutedEventArgs e)
    {
        var browse = RequireBrowse();
        var prompt = browse.AuthenticationFailurePrompt;
        if (prompt is null)
        {
            BindAuthFailureBanner();
            return;
        }

        AccountActionStatus.Text = string.Empty;
        if (prompt.Action == AuthenticationFailureAction.EditAccount)
        {
            var account = browse.AccountStatuses
                .FirstOrDefault(row => row.Account.Id == prompt.AccountId)
                ?.Account;
            if (account is null)
            {
                BindAuthFailureBanner();
                return;
            }

            await EditAccountAsync(account).ConfigureAwait(true);
            return;
        }

        try
        {
            await browse.ReauthorizeAccountAsync(prompt.AccountId).ConfigureAwait(true);
            await RefreshAfterAccountWorkAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AccountActionStatus.Text = ex.Message;
            BindLists();
        }
    }

    private void OnDismissAuthFailureClick(object? sender, RoutedEventArgs e)
    {
        RequireBrowse().DismissAuthenticationFailurePrompt();
        BindAuthFailureBanner();
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
            .SaveDraftAsync(ComposeToBox.Text ?? string.Empty, ComposeSubjectBox.Text ?? string.Empty, ComposeBodyBox.Text ?? string.Empty, ComposeCcBox.Text ?? string.Empty, ComposeBccBox.Text ?? string.Empty, string.IsNullOrWhiteSpace(ComposeHtmlBox.Text) ? null : ComposeHtmlBox.Text)
            .ConfigureAwait(true);
        BindLists();
        DraftsList.SelectedItem = compose.Drafts.FirstOrDefault();
    }

    private async void OnAttachDraftClick(object? sender, RoutedEventArgs e)
    {
        var compose = RequireCompose();
        if (compose.SelectedAccountId is null)
        {
            return;
        }

        if (compose.SelectedDraftId is null)
        {
            await compose
                .SaveDraftAsync(ComposeToBox.Text ?? string.Empty, ComposeSubjectBox.Text ?? string.Empty, ComposeBodyBox.Text ?? string.Empty, ComposeCcBox.Text ?? string.Empty, ComposeBccBox.Text ?? string.Empty, string.IsNullOrWhiteSpace(ComposeHtmlBox.Text) ? null : ComposeHtmlBox.Text)
                .ConfigureAwait(true);
        }

        var top = TopLevel.GetTopLevel(this);
        if (top is null)
        {
            return;
        }

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Attach file",
        }).ConfigureAwait(true);
        var file = files.FirstOrDefault();
        if (file is null)
        {
            return;
        }

        await using var stream = await file.OpenReadAsync().ConfigureAwait(true);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory).ConfigureAwait(true);
        await compose
            .AddDraftAttachmentAsync(file.Name, "application/octet-stream", memory.ToArray())
            .ConfigureAwait(true);
        BindLists();
    }

    private async void OnRemoveDraftAttachmentClick(object? sender, RoutedEventArgs e)
    {
        var compose = RequireCompose();
        if (DraftAttachmentsList.SelectedItem is not DraftAttachmentInfo attachment)
        {
            return;
        }

        await compose.RemoveDraftAttachmentAsync(attachment.Id).ConfigureAwait(true);
        BindLists();
    }

    private async void OnSendDraftClick(object? sender, RoutedEventArgs e)
    {
        var compose = RequireCompose();
        if (compose.SelectedAccountId is null)
        {
            return;
        }

        await compose
            .SaveDraftAsync(ComposeToBox.Text ?? string.Empty, ComposeSubjectBox.Text ?? string.Empty, ComposeBodyBox.Text ?? string.Empty, ComposeCcBox.Text ?? string.Empty, ComposeBccBox.Text ?? string.Empty, string.IsNullOrWhiteSpace(ComposeHtmlBox.Text) ? null : ComposeHtmlBox.Text)
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

    private async void OnEditAccountClick(object? sender, RoutedEventArgs e)
    {
        AccountActionStatus.Text = string.Empty;
        if (AccountsList.SelectedItem is not AccountStatusRow row)
        {
            AccountActionStatus.Text = "Select an Account to edit.";
            return;
        }

        await EditAccountAsync(row.Account).ConfigureAwait(true);
    }

    private async Task EditAccountAsync(AccountInfo account)
    {
        var browse = RequireBrowse();
        AccountActionStatus.Text = string.Empty;
        var dialog = new EditAccountDialog(browse, account);
        bool edited;
        try
        {
            edited = await AvaloniaOverlayDialog.ShowAsync(this, dialog, dialog.Completion).ConfigureAwait(true);
        }
        catch (InvalidOperationException)
        {
            AccountActionStatus.Text = "Unable to open Edit Account dialog.";
            return;
        }

        if (!edited)
        {
            return;
        }

        try
        {
            await browse.LoadAccountsAsync().ConfigureAwait(true);
            if (browse.SelectedAccountId is { } selectedId)
            {
                await browse.SelectAccountAsync(selectedId).ConfigureAwait(true);
                await RequireCompose().SelectAccountAsync(selectedId).ConfigureAwait(true);
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
        ComposeHtmlBox.Text = loaded.BodyHtml ?? string.Empty;
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

    private void OnShellKeyDown(object? sender, KeyEventArgs e)
    {
        var shortcut = MailShellShortcuts.FromKey(e.Key, e.Source is TextBox);
        if (shortcut == MailShellShortcut.None)
        {
            return;
        }

        e.Handled = true;
        switch (shortcut)
        {
            case MailShellShortcut.Reply:
                OnReplyClick(sender, e);
                return;
            case MailShellShortcut.ReplyAll:
                OnReplyAllClick(sender, e);
                return;
            case MailShellShortcut.Forward:
                OnForwardClick(sender, e);
                return;
            case MailShellShortcut.MarkUnread:
                OnMarkUnreadClick(sender, e);
                return;
            case MailShellShortcut.Flag:
                OnFlagClick(sender, e);
                return;
            case MailShellShortcut.Delete:
                OnDeleteClick(sender, e);
                return;
            case MailShellShortcut.FocusSearch:
                MessageSearchBox.Focus();
                return;
            case MailShellShortcut.NewDraft:
                OnNewDraftClick(sender, e);
                return;
            case MailShellShortcut.NextUnread:
                OnNextUnreadClick(sender, e);
                return;
            case MailShellShortcut.PreviousUnread:
                OnPreviousUnreadClick(sender, e);
                return;
        }
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
        ComposeHtmlBox.Text = draft.BodyHtml ?? string.Empty;
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
        ComposeHtmlBox.Text = draft.BodyHtml ?? string.Empty;
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
        ComposeHtmlBox.Text = draft.BodyHtml ?? string.Empty;
        BindLists();
        DraftsList.SelectedItem = compose.Drafts.FirstOrDefault(d => d.Id == draft.Id);
    }
    private async void OnMarkMailboxReadClick(object? sender, RoutedEventArgs e)
    {
        var browse = RequireBrowse();
        await browse.MarkCurrentReadAsync().ConfigureAwait(true);
        BindLists();
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

    private async void OnMoveClick(object? sender, RoutedEventArgs e)
    {
        var browse = RequireBrowse();
        AccountActionStatus.Text = string.Empty;
        if (browse.SelectedMessageId is null)
        {
            return;
        }

        try
        {
            var destinations = await browse.ListMoveDestinationsAsync().ConfigureAwait(true);
            var currentMailboxId = browse.Messages
                .FirstOrDefault(m => m.Id == browse.SelectedMessageId)?.MailboxId;
            var dialog = new MoveMailboxDialog(destinations, currentMailboxId);
            var chosen = await AvaloniaOverlayDialog.ShowAsync(this, dialog, dialog.Completion)
                .ConfigureAwait(true);
            if (chosen is not { } destinationId)
            {
                return;
            }

            await browse.MoveSelectedToMailboxAsync(destinationId).ConfigureAwait(true);
            BindLists();
        }
        catch (Exception ex)
        {
            AccountActionStatus.Text = ex.Message;
        }
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

    private async void OnEmptyTrashClick(object? sender, RoutedEventArgs e)
    {
        var browse = RequireBrowse();
        if (browse.SelectedAccountId is null)
        {
            return;
        }

        await browse.EmptyTrashAsync().ConfigureAwait(true);
        BindLists();
    }

    private async void OnNextUnreadClick(object? sender, RoutedEventArgs e)
    {
        var browse = RequireBrowse();
        await browse.SelectNextUnreadAsync().ConfigureAwait(true);
        BindLists();
    }

    private async void OnPreviousUnreadClick(object? sender, RoutedEventArgs e)
    {
        var browse = RequireBrowse();
        await browse.SelectPreviousUnreadAsync().ConfigureAwait(true);
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
        ComposeHtmlBox.Text = string.Empty;
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
            DraftAttachmentsList.ItemsSource = compose.DraftAttachments;
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
            MessageToText.Text = browse.SelectedToAddresses.Count == 0
                ? string.Empty
                : "To: " + string.Join(", ", browse.SelectedToAddresses);
            MessageToText.IsVisible = browse.SelectedToAddresses.Count > 0;
            MessageCcText.Text = browse.SelectedCcAddresses.Count == 0
                ? string.Empty
                : "Cc: " + string.Join(", ", browse.SelectedCcAddresses);
            MessageCcText.IsVisible = browse.SelectedCcAddresses.Count > 0;
            BodyUnavailableText.IsVisible = browse.BodyUnavailable;
            BindMessageBody(browse);
            AttachmentOpenErrorText.Text = browse.AttachmentOpenError ?? string.Empty;
            BindAuthFailureBanner();
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
