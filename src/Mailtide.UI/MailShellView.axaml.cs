using System.ComponentModel;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mailtide.Core;
using Mailtide.Core.Updates;

namespace Mailtide.UI;

public partial class MailShellView : UserControl
{
    private readonly BrowseShell? _browse;
    private readonly ComposeOutboxShell? _compose;
    private readonly DispatcherTimer _autosaveTimer;
    private readonly DispatcherTimer _searchTimer;
    private readonly DispatcherTimer _listTypeaheadTimer;
    private readonly DispatcherTimer _navTypeaheadTimer;
    private string _listTypeahead = string.Empty;
    private string _navTypeahead = string.Empty;
    private bool _suppressSelectionHandlers;
    private bool _composeDirty;
    private bool _showCcBccPreferred;
    private ShellNavItem? _navMenuItem;
    private UpdateCheckResult? _pendingUpdate;
    private TextBox? _addressSuggestBox;
    private static readonly DataFormat<ThreadRow> ThreadDragFormat =
        DataFormat.CreateInProcessFormat<ThreadRow>("mailtide.thread");
    private static readonly DataFormat<FavoriteDrag> FavoriteDragFormat =
        DataFormat.CreateInProcessFormat<FavoriteDrag>("mailtide.favorite");
    private int _findHitIndex = -1;
    private string _findQuery = string.Empty;
    private bool _quotedExpanded;
    private Guid? _quotedForMessageId;
    private PointerPressedEventArgs? _pendingThreadDrag;
    private Point _threadDragStart;
    private ThreadRow? _threadDragRow;
    private PointerPressedEventArgs? _pendingFavoriteDrag;
    private Point _favoriteDragStart;
    private Guid? _favoriteDragId;
    private PointerPressedEventArgs? _pendingAttachmentDrag;
    private Point _attachmentDragStart;
    private AttachmentInfo? _attachmentDragItem;
    private Guid? _readingScrollForThreadId;
    private Guid? _readingScrollForMessageId;

    public MailShellView()
    {
        InitializeComponent();
        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _autosaveTimer.Tick += OnAutosaveTick;
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
        _searchTimer.Tick += OnSearchTick;
        _listTypeaheadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _listTypeaheadTimer.Tick += OnListTypeaheadTick;
        _navTypeaheadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _navTypeaheadTimer.Tick += OnNavTypeaheadTick;
        AttachShellHandlers();
    }

    public MailShellView(BrowseShell browse, ComposeOutboxShell compose)
    {
        ArgumentNullException.ThrowIfNull(browse);
        ArgumentNullException.ThrowIfNull(compose);
        _browse = browse;
        _compose = compose;
        InitializeComponent();
        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _autosaveTimer.Tick += OnAutosaveTick;
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
        _searchTimer.Tick += OnSearchTick;
        _listTypeaheadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _listTypeaheadTimer.Tick += OnListTypeaheadTick;
        _navTypeaheadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _navTypeaheadTimer.Tick += OnNavTypeaheadTick;
        AttachShellHandlers();
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
        if (browse.SelectedMessageId is not null)
        {
            await browse.RefreshConversationAsync().ConfigureAwait(true);
        }

        BindLists();
        ShowReadSurface();
    }

    public async Task InitializeBrowseAsync()
    {
        var browse = RequireBrowse();
        await browse.LoadAccountsAsync().ConfigureAwait(true);
        if (browse.Accounts.Count > 0)
        {
            await browse.RestoreBrowseAsync().ConfigureAwait(true);
            var compose = RequireCompose();
            if (browse.SelectedAccountId is { } accountId)
            {
                await compose.SelectAccountAsync(accountId).ConfigureAwait(true);
            }
            else if (await compose.GetLastFromAsync().ConfigureAwait(true) is { } lastFrom
                && browse.Accounts.Any(account => account.Id == lastFrom))
            {
                await compose.SelectAccountAsync(lastFrom).ConfigureAwait(true);
            }

            _showCcBccPreferred = await compose.GetShowCcBccAsync().ConfigureAwait(true);
        }

        BindLists();
        await ApplyPaneLayoutAsync().ConfigureAwait(true);
    }

    public async Task PersistChromeAsync()
    {
        if (DailyShell is null || _browse is null)
        {
            return;
        }

        var nav = DailyShell.ColumnDefinitions[0].Width.Value;
        var list = DailyShell.ColumnDefinitions[2].Width.Value;
        await _browse
            .SetPreferenceAsync(PaneLayout.PreferenceKey, PaneLayout.Encode(nav, list))
            .ConfigureAwait(true);
    }

    private async Task ApplyPaneLayoutAsync()
    {
        if (DailyShell is null || _browse is null)
        {
            return;
        }

        var raw = await _browse.GetPreferenceAsync(PaneLayout.PreferenceKey).ConfigureAwait(true);
        if (!PaneLayout.TryParse(raw, out var nav, out var list))
        {
            return;
        }

        DailyShell.ColumnDefinitions[0].Width = new GridLength(nav);
        DailyShell.ColumnDefinitions[2].Width = new GridLength(list);
    }

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

    private void AttachShellHandlers()
    {
        AddHandler(KeyDownEvent, OnShellKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        if (ComposeToBox is null)
        {
            return;
        }

        ComposeToBox.TextChanged += OnComposeTextChanged;
        ComposeCcBox.TextChanged += OnComposeTextChanged;
        ComposeBccBox.TextChanged += OnComposeTextChanged;
        ComposeSubjectBox.TextChanged += OnComposeTextChanged;
        ComposeBodyBox.TextChanged += OnComposeTextChanged;
        ComposeHtmlBox.TextChanged += OnComposeTextChanged;
        if (MessageSearchBox is not null)
        {
            MessageSearchBox.TextChanged += OnSearchTextChanged;
        }

        if (ComposeSurface is not null)
        {
            ComposeSurface.AddHandler(DragDrop.DragOverEvent, OnComposeDragOver);
            ComposeSurface.AddHandler(DragDrop.DropEvent, OnComposeDrop);
        }

        if (ThreadsList is not null)
        {
            ThreadsList.AddHandler(PointerPressedEvent, OnThreadListPointerPressed);
            ThreadsList.AddHandler(PointerMovedEvent, OnThreadListPointerMoved);
            ThreadsList.AddHandler(PointerReleasedEvent, OnThreadListPointerReleased);
            ThreadsList.AddHandler(PointerCaptureLostEvent, OnThreadListPointerCaptureLost);
        }

        WireAttachmentDrag(ThreadAttachmentsList);
        WireAttachmentDrag(ConversationBeforeList);
        WireAttachmentDrag(SelectedConversationHost);
        WireAttachmentDrag(ConversationAfterList);

        if (NavTree is not null)
        {
            NavTree.AddHandler(PointerPressedEvent, OnNavPointerPressed);
            NavTree.AddHandler(PointerMovedEvent, OnNavPointerMoved);
            NavTree.AddHandler(PointerReleasedEvent, OnNavPointerReleased);
            NavTree.AddHandler(PointerCaptureLostEvent, OnNavPointerCaptureLost);
            NavTree.AddHandler(DragDrop.DragOverEvent, OnNavDragOver);
            NavTree.AddHandler(DragDrop.DropEvent, OnNavDrop);
            NavTree.AddHandler(TreeViewItem.ExpandedEvent, OnNavExpandChanged);
            NavTree.AddHandler(TreeViewItem.CollapsedEvent, OnNavExpandChanged);
        }
    }

    private async void OnNavExpandChanged(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not TreeViewItem { DataContext: ShellNavItem item } node || _browse is null)
        {
            return;
        }

        try
        {
            await _browse.SetNavExpandedAsync(item, node.IsExpanded).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
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
            SetStatus(ex.Message);
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

        SetStatus(string.Empty);
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
            SetStatus(ex.Message);
            BindLists();
        }
    }

    private void OnDismissAuthFailureClick(object? sender, RoutedEventArgs e)
    {
        RequireBrowse().DismissAuthenticationFailurePrompt();
        BindAuthFailureBanner();
    }

    private async void OnSyncNowClick(object? sender, RoutedEventArgs e)
    {
        var browse = RequireBrowse();
        if (browse.Accounts.Count == 0)
        {
            return;
        }

        try
        {
            await browse.SyncCurrentScopeAsync().ConfigureAwait(true);
            var compose = RequireCompose();
            if (compose.SelectedAccountId is { } accountId)
            {
                await compose.SelectAccountAsync(accountId).ConfigureAwait(true);
            }

            BindLists();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
            BindLists();
        }
    }

    private async void OnComposeClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await MaybeDiscardBlankDraftAsync().ConfigureAwait(true);
            if (!await EnsureComposeAccountAsync().ConfigureAwait(true))
            {
                SetStatus("Add an Account before composing.");
                return;
            }

            RequireCompose().StartNewDraft();
            await RequireCompose().LoadRecentAddressesAsync().ConfigureAwait(true);
            ClearComposeFields();
            ApplyComposeSignature();
            ShowComposeSurface();
            BindLists();
            ComposeToBox.Focus();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnAutosaveTick(object? sender, EventArgs e)
    {
        _autosaveTimer.Stop();
        await AutosaveComposeAsync().ConfigureAwait(true);
    }

    private void UpdateAddressSuggestions(TextBox box)
    {
        _addressSuggestBox = box;
        var (start, length, token) = AddressCompletion.CurrentToken(box.Text, box.CaretIndex);
        string? Sibling(TextBox? other) =>
            other is null || ReferenceEquals(other, box) ? null : other.Text;
        var suggestions = AddressCompletion.Suggest(
            token,
            RequireCompose().RecentAddresses,
            AddressCompletion.RecipientsToExclude(
                box.Text,
                start,
                length,
                Sibling(ComposeToBox),
                Sibling(ComposeCcBox),
                Sibling(ComposeBccBox)));
        if (AddressSuggestList is not null)
        {
            AddressSuggestList.ItemsSource = suggestions;
            AddressSuggestList.SelectedIndex = suggestions.Count > 0 ? 0 : -1;
        }

        if (AddressSuggestPopup is null)
        {
            return;
        }

        AddressSuggestPopup.PlacementTarget = box;
        AddressSuggestPopup.IsOpen = suggestions.Count > 0;
    }

    private void HideAddressSuggestions()
    {
        if (AddressSuggestPopup is not null)
        {
            AddressSuggestPopup.IsOpen = false;
        }

        _addressSuggestBox = null;
    }

    private bool TryHandleAddressSuggestKey(KeyEventArgs e)
    {
        if (AddressSuggestPopup?.IsOpen != true)
        {
            return false;
        }

        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control)
        {
            return false;
        }

        switch (e.Key)
        {
            case Key.Escape:
                HideAddressSuggestions();
                e.Handled = true;
                return true;
            case Key.Down:
                MoveAddressSuggest(1);
                e.Handled = true;
                return true;
            case Key.Up:
                MoveAddressSuggest(-1);
                e.Handled = true;
                return true;
            case Key.Enter:
                if (AcceptAddressSuggest())
                {
                    e.Handled = true;
                    return true;
                }

                return false;
            case Key.Tab:
                AcceptAddressSuggest();
                return false;
            default:
                return false;
        }
    }

    private void MoveAddressSuggest(int delta)
    {
        if (AddressSuggestList is null || AddressSuggestList.ItemCount == 0)
        {
            return;
        }

        var index = AddressSuggestList.SelectedIndex;
        if (index < 0)
        {
            index = 0;
        }

        index = Math.Clamp(index + delta, 0, AddressSuggestList.ItemCount - 1);
        AddressSuggestList.SelectedIndex = index;
    }

    private bool AcceptAddressSuggest()
    {
        if (_addressSuggestBox is null
            || AddressSuggestList?.SelectedItem is not string suggestion
            || string.IsNullOrWhiteSpace(suggestion))
        {
            HideAddressSuggestions();
            return false;
        }

        var box = _addressSuggestBox;
        var replaced = AddressCompletion.ReplaceCurrentToken(
            box.Text,
            box.CaretIndex,
            suggestion,
            out var caret,
            appendSeparator: true);
        box.Text = replaced;
        box.CaretIndex = caret;
        HideAddressSuggestions();
        return true;
    }

    private void OnAddressSuggestPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (AddressSuggestList?.SelectedItem is string)
        {
            AcceptAddressSuggest();
        }
    }

    private void OnComposeTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppressSelectionHandlers)
        {
            return;
        }

        _composeDirty = true;
        _autosaveTimer.Stop();
        _autosaveTimer.Start();
        if (sender is TextBox box
            && (box == ComposeToBox || box == ComposeCcBox || box == ComposeBccBox))
        {
            UpdateAddressSuggestions(box);
        }
        else
        {
            HideAddressSuggestions();
        }

        if (sender == ComposeSubjectBox)
        {
            BindWindowTitle(RequireBrowse());
        }
    }

    private async Task AutosaveComposeAsync()
    {
        if (!_composeDirty || ComposeSurface is null || !ComposeSurface.IsVisible)
        {
            return;
        }

        if (IsComposeBlank() && RequireCompose().SelectedDraftId is null)
        {
            return;
        }

        try
        {
            await SaveComposeDraftAsync().ConfigureAwait(true);
            RefreshComposeChrome();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async Task SaveComposeDraftAsync()
    {
        var compose = RequireCompose();
        if (compose.SelectedAccountId is null)
        {
            return;
        }

        await compose
            .SaveDraftAsync(
                ComposeToBox.Text ?? string.Empty,
                ComposeSubjectBox.Text ?? string.Empty,
                ComposeBodyBox.Text ?? string.Empty,
                ComposeCcBox.Text ?? string.Empty,
                ComposeBccBox.Text ?? string.Empty,
                string.IsNullOrWhiteSpace(ComposeHtmlBox.Text) ? null : ComposeHtmlBox.Text)
            .ConfigureAwait(true);
        _composeDirty = false;
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
            await SaveComposeDraftAsync().ConfigureAwait(true);
        }

        var top = TopLevel.GetTopLevel(this);
        if (top is null)
        {
            return;
        }

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = "Attach file",
        }).ConfigureAwait(true);
        try
        {
            await AttachStorageFilesAsync(files).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private void OnComposeDragOver(object? sender, DragEventArgs e)
    {
        var allowFiles = e.DataTransfer.Contains(DataFormat.File)
            && MailShellFormatting.ClassifyComposePaste(
                IsComposeHeaderField(e.Source),
                hasFiles: true,
                hasText: false,
                hasBitmap: false) == ComposePasteKind.AttachFiles;
        e.DragEffects = allowFiles ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnComposeDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (ComposeSurface?.IsVisible != true
            || MailShellFormatting.ClassifyComposePaste(
                IsComposeHeaderField(e.Source),
                hasFiles: true,
                hasText: false,
                hasBitmap: false) != ComposePasteKind.AttachFiles)
        {
            return;
        }

        var dropped = e.DataTransfer.TryGetFiles()?
            .OfType<IStorageFile>()
            .ToList();
        if (dropped is null || dropped.Count == 0)
        {
            return;
        }

        try
        {
            await AttachStorageFilesAsync(dropped).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async Task AttachStorageFilesAsync(IReadOnlyList<IStorageFile> files)
    {
        if (files.Count == 0)
        {
            return;
        }

        var compose = RequireCompose();
        if (compose.SelectedAccountId is null)
        {
            return;
        }

        if (compose.SelectedDraftId is null)
        {
            await SaveComposeDraftAsync().ConfigureAwait(true);
        }

        foreach (var file in files)
        {
            await using var stream = await file.OpenReadAsync().ConfigureAwait(true);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory).ConfigureAwait(true);
            await compose
                .AddDraftAttachmentAsync(
                    file.Name,
                    AttachmentContentType.FromFileName(file.Name),
                    memory.ToArray())
                .ConfigureAwait(true);
        }

        BindLists();
    }

    private async void OnComposePaste(object? source)
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            var clipboard = top?.Clipboard;
            if (clipboard is null)
            {
                return;
            }

            var textBox = FindContainingTextBox(source);
            var headerField = IsComposeHeaderField(source);
            var files = (await clipboard.TryGetFilesAsync().ConfigureAwait(true))?
                .OfType<IStorageFile>()
                .ToList();
            var text = await clipboard.TryGetTextAsync().ConfigureAwait(true);
            var kind = MailShellFormatting.ClassifyComposePaste(
                headerField,
                files is { Count: > 0 },
                !string.IsNullOrEmpty(text),
                hasBitmap: true);
            if (kind == ComposePasteKind.AttachFiles && files is { Count: > 0 })
            {
                await AttachStorageFilesAsync(files).ConfigureAwait(true);
                return;
            }

            if (kind == ComposePasteKind.Text)
            {
                textBox?.Paste();
                return;
            }

            var bitmap = await clipboard.TryGetBitmapAsync().ConfigureAwait(true);
            if (bitmap is null)
            {
                textBox?.Paste();
                return;
            }

            try
            {
                using var memory = new MemoryStream();
                bitmap.Save(memory, PngBitmapEncoderOptions.Default);
                var name = $"image-{DateTime.Now:yyyyMMdd-HHmmss}.png";
                var compose = RequireCompose();
                if (compose.SelectedAccountId is null)
                {
                    return;
                }

                if (compose.SelectedDraftId is null)
                {
                    await SaveComposeDraftAsync().ConfigureAwait(true);
                }

                await compose
                    .AddDraftAttachmentAsync(name, AttachmentContentType.FromFileName(name), memory.ToArray())
                    .ConfigureAwait(true);
                BindLists();
            }
            finally
            {
                bitmap.Dispose();
            }
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private bool IsInsideCompose(object? source)
    {
        if (ComposeSurface is null || source is not Visual visual)
        {
            return false;
        }

        for (Visual? current = visual; current is not null; current = current.GetVisualParent())
        {
            if (ReferenceEquals(current, ComposeSurface))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsComposeHeaderField(object? source)
    {
        var box = FindContainingTextBox(source);
        return box is not null
            && (box == ComposeToBox
                || box == ComposeCcBox
                || box == ComposeBccBox
                || box == ComposeSubjectBox);
    }

    private static TextBox? FindContainingTextBox(object? source)
    {
        for (Visual? current = source as Visual; current is not null; current = current.GetVisualParent())
        {
            if (current is TextBox box)
            {
                return box;
            }
        }

        return null;
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
        try
        {
            await SaveComposeDraftAsync().ConfigureAwait(true);
            var compose = RequireCompose();
            if (compose.SelectedDraftId is not { } draftId)
            {
                return;
            }

            var recipientError = MailShellFormatting.RecipientError(
                ComposeToBox.Text,
                ComposeCcBox.Text,
                ComposeBccBox.Text);
            if (recipientError.Length > 0)
            {
                SetStatus(recipientError);
                ComposeToBox?.Focus();
                return;
            }

            if (MailShellFormatting.NeedsEmptySubjectConfirm(ComposeSubjectBox.Text)
                && !await ConfirmAsync(
                        "No subject",
                        "Send this message without a subject?",
                        "Send").ConfigureAwait(true))
            {
                return;
            }

            if (MailShellFormatting.NeedsEmptyBodyConfirm(
                    ComposeBodyBox.Text,
                    CurrentComposeSignature(),
                    compose.DraftAttachments.Count,
                    ComposeHtmlBox.Text)
                && !await ConfirmAsync(
                        "No message",
                        "Send this message without a body?",
                        "Send").ConfigureAwait(true))
            {
                return;
            }

            await compose.SendAsync(draftId).ConfigureAwait(true);
            await RequireBrowse().LoadAccountsAsync().ConfigureAwait(true);
            ClearComposeFields();
            ShowReadSurface();
            BindLists();
            SetStatus("Queued in Outbox. Sync to send.");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnComposeFromChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionHandlers)
        {
            return;
        }

        if (ComposeFromBox.SelectedItem is not AccountInfo account)
        {
            return;
        }

        var compose = RequireCompose();
        if (compose.SelectedAccountId == account.Id)
        {
            return;
        }

        try
        {
            var previousSignature = CurrentComposeSignature();
            if (_composeDirty && !IsComposeBlank())
            {
                await SaveComposeDraftAsync().ConfigureAwait(true);
            }

            await compose.SelectAccountAsync(account.Id).ConfigureAwait(true);
            await compose.RememberLastFromAsync(account.Id).ConfigureAwait(true);
            if (ComposeBodyBox is not null)
            {
                ComposeBodyBox.Text = MailSignature.Replace(
                    ComposeBodyBox.Text,
                    previousSignature,
                    account.Signature);
            }

            _composeDirty = !IsComposeBlank();
            BindLists();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnDiscardDraftClick(object? sender, RoutedEventArgs e)
    {
        if (!IsComposeBlank()
            && !await ConfirmAsync(
                    "Discard Draft",
                    "Discard this Draft? Unsent changes will be lost.",
                    "Discard").ConfigureAwait(true))
        {
            return;
        }

        var compose = RequireCompose();
        if (compose.SelectedDraftId is { } draftId)
        {
            await compose.DiscardDraftAsync(draftId).ConfigureAwait(true);
        }

        ClearComposeFields();
        ShowReadSurface();
        BindLists();
    }

    private async void OnRetryOutboxClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await RetrySelectedOutboxAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async Task RetrySelectedOutboxAsync()
    {
        var compose = RequireCompose();
        if (SelectedOutboxItem() is { State: OutboxItemState.Failed } item)
        {
            await compose.RetryOutboxItemAsync(item.Id).ConfigureAwait(true);
            await RequireBrowse().LoadAccountsAsync().ConfigureAwait(true);
            BindLists();
            SetStatus("Queued in Outbox. Sync to send.");
            return;
        }

        var count = await compose.RetryFailedOutboxAsync().ConfigureAwait(true);
        if (count == 0)
        {
            return;
        }

        await RequireBrowse().LoadAccountsAsync().ConfigureAwait(true);
        BindLists();
        SetStatus(
            count == 1
                ? "Retried 1 message. Sync to send."
                : "Retried " + count + " messages. Sync to send.");
    }

    private async void OnDiscardOutboxClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await DiscardSelectedOutboxAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async Task DiscardSelectedOutboxAsync()
    {
        if (SelectedOutboxItem() is not { } item)
        {
            return;
        }

        await RequireCompose().DiscardOutboxItemAsync(item.Id).ConfigureAwait(true);
        await RequireBrowse().LoadAccountsAsync().ConfigureAwait(true);
        BindLists();
    }

    private async void OnNewMailboxClick(object? sender, RoutedEventArgs e)
    {
        var browse = RequireBrowse();
        SetStatus(string.Empty);
        if (!await EnsureAccountContextAsync().ConfigureAwait(true))
        {
            SetStatus("Select an Account before creating a Mailbox.");
            return;
        }

        try
        {
            var dialog = new NewMailboxDialog();
            var name = await AvaloniaOverlayDialog.ShowAsync(this, dialog, dialog.Completion)
                .ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            await browse.CreateMailboxAsync(name).ConfigureAwait(true);
            BindLists();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnRenameMailboxClick(object? sender, RoutedEventArgs e)
    {
        if (!IsNavFocused() && sender is not MenuItem)
        {
            return;
        }

        var browse = RequireBrowse();
        SetStatus(string.Empty);
        await EnsureAccountContextAsync().ConfigureAwait(true);
        if (browse.SelectedMailboxId is null)
        {
            SetStatus("Select a Mailbox before renaming it.");
            return;
        }

        try
        {
            var dialog = new RenameMailboxDialog();
            var name = await AvaloniaOverlayDialog.ShowAsync(this, dialog, dialog.Completion)
                .ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            await browse.RenameMailboxAsync(name).ConfigureAwait(true);
            BindLists();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnDeleteMailboxClick(object? sender, RoutedEventArgs e)
    {
        var browse = RequireBrowse();
        SetStatus(string.Empty);
        await EnsureAccountContextAsync().ConfigureAwait(true);
        if (browse.SelectedMailboxId is null)
        {
            SetStatus("Select a Mailbox before deleting it.");
            return;
        }

        if (!await ConfirmAsync(
                "Delete Mailbox",
                "Delete this Mailbox and its local Messages? This cannot be undone.",
                "Delete").ConfigureAwait(true))
        {
            return;
        }

        try
        {
            await browse.DeleteMailboxAsync().ConfigureAwait(true);
            BindLists();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }
    private async void OnAddAccountClick(object? sender, RoutedEventArgs e)
    {
        var browse = RequireBrowse();
        SetStatus(string.Empty);
        var dialog = new AddAccountDialog(browse);
        bool added;
        try
        {
            added = await AvaloniaOverlayDialog.ShowAsync(this, dialog, dialog.Completion).ConfigureAwait(true);
        }
        catch (InvalidOperationException)
        {
            SetStatus("Unable to open Add Account dialog.");
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
            var inbox = browse.Mailboxes.FirstOrDefault(mailbox => mailbox.Role == MailboxRole.Inbox);
            if (inbox is not null)
            {
                await browse.SelectMailboxAsync(inbox.Id).ConfigureAwait(true);
            }

            BindLists();
        }
        catch (Exception ex)
        {
            await browse.LoadAccountsAsync().ConfigureAwait(true);
            BindLists();
            SetStatus(ex.Message);
        }
    }

    private async void OnEditAccountClick(object? sender, RoutedEventArgs e)
    {
        SetStatus(string.Empty);
        var account = CurrentAccount();
        if (account is null)
        {
            SetStatus("Select an Account to edit.");
            return;
        }

        await EditAccountAsync(account).ConfigureAwait(true);
    }

    private async Task EditAccountAsync(AccountInfo account)
    {
        var browse = RequireBrowse();
        SetStatus(string.Empty);
        var dialog = new EditAccountDialog(browse, account);
        bool edited;
        try
        {
            edited = await AvaloniaOverlayDialog.ShowAsync(this, dialog, dialog.Completion).ConfigureAwait(true);
        }
        catch (InvalidOperationException)
        {
            SetStatus("Unable to open Edit Account dialog.");
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
            SetStatus(ex.Message);
        }
    }

    private async void OnRemoveAccountClick(object? sender, RoutedEventArgs e)
    {
        var browse = RequireBrowse();
        SetStatus(string.Empty);
        var account = CurrentAccount();
        if (account is null)
        {
            SetStatus("Select an Account to remove.");
            return;
        }

        try
        {
            var removed = await browse.RemoveAccountAsync(account.Id).ConfigureAwait(true);
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
            SetStatus(ex.Message);
        }
    }

    private async void OnDraftSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionHandlers)
        {
            return;
        }

        if (DraftsCombo.SelectedItem is not DraftInfo draft)
        {
            return;
        }

        await MaybeDiscardBlankDraftAsync().ConfigureAwait(true);
        var loaded = await RequireCompose().SelectDraftAsync(draft.Id).ConfigureAwait(true);
        FillComposeFromDraft(loaded);
        BindLists();
        ComposeBodyBox.Focus();
    }
    private async void OnNavSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionHandlers || _browse is null)
        {
            return;
        }

        if (NavTree.SelectedItem is not ShellNavItem item)
        {
            return;
        }

        SetStatus(string.Empty);
        try
        {
            var inboxId = _browse.Mailboxes.FirstOrDefault(mailbox => mailbox.Role == MailboxRole.Inbox)?.Id
                ?? _browse.AllMailboxes.FirstOrDefault(mailbox =>
                    mailbox.AccountId == item.AccountId && mailbox.Role == MailboxRole.Inbox)?.Id;
            if (MailShellFormatting.IsCurrentNav(
                    item,
                    _browse.ShowingUnifiedInbox,
                    _browse.ShowingOutbox,
                    _browse.SelectedAccountId,
                    _browse.SelectedMailboxId,
                    inboxId))
            {
                BindLists(rebindCompose: ComposeSurface?.IsVisible != true);
                return;
            }

            await MaybeDiscardBlankDraftAsync().ConfigureAwait(true);
            var compose = RequireCompose();
            switch (item.Kind)
            {
                case ShellNavKind.UnifiedInbox:
                    await _browse.ShowUnifiedInboxAsync().ConfigureAwait(true);
                    break;
                case ShellNavKind.Account when item.AccountId is { } accountId:
                    await _browse.SelectAccountAsync(accountId).ConfigureAwait(true);
                    await compose.SelectAccountAsync(accountId).ConfigureAwait(true);
                    var inbox = _browse.Mailboxes.FirstOrDefault(mailbox => mailbox.Role == MailboxRole.Inbox);
                    if (inbox is not null)
                    {
                        await _browse.SelectMailboxAsync(inbox.Id).ConfigureAwait(true);
                    }

                    break;
                case ShellNavKind.Mailbox when item.AccountId is { } mailboxAccountId && item.MailboxId is { } mailboxId:
                    if (_browse.SelectedAccountId != mailboxAccountId)
                    {
                        await _browse.SelectAccountAsync(mailboxAccountId).ConfigureAwait(true);
                    }

                    await compose.SelectAccountAsync(mailboxAccountId).ConfigureAwait(true);
                    await _browse.SelectMailboxAsync(mailboxId).ConfigureAwait(true);
                    break;
                case ShellNavKind.Outbox when item.AccountId is { } outboxAccountId:
                    await _browse.ShowOutboxAsync(outboxAccountId).ConfigureAwait(true);
                    await compose.SelectAccountAsync(outboxAccountId).ConfigureAwait(true);
                    break;
            }

            ShowReadSurface();
            BindLists();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private void OnNavContextMenuOpening(object? sender, CancelEventArgs e)
    {
        if (sender is not ContextMenu menu)
        {
            return;
        }

        var nav = (menu.PlacementTarget as Control)?.DataContext as ShellNavItem
            ?? menu.DataContext as ShellNavItem;
        if (nav is null)
        {
            e.Cancel = true;
            return;
        }

        _navMenuItem = nav;
        foreach (var obj in menu.Items)
        {
            if (obj is not MenuItem item || item.Header is not string header)
            {
                continue;
            }

            item.IsVisible = header switch
            {
                "Edit Account" or "Remove Account" or "New Mailbox" =>
                    nav.Kind is ShellNavKind.Account or ShellNavKind.Mailbox or ShellNavKind.Outbox,
                "Rename Mailbox" or "Delete Mailbox" or "Mark Mailbox Read" =>
                    nav.Kind == ShellNavKind.Mailbox,
                "Pin to Favorites" =>
                    nav.Kind == ShellNavKind.Mailbox
                    && nav.MailboxId is { } pinId
                    && _browse is not null
                    && !_browse.IsFavorite(pinId),
                "Unpin from Favorites" =>
                    nav.Kind == ShellNavKind.Mailbox
                    && nav.MailboxId is { } unpinId
                    && _browse is not null
                    && _browse.IsFavorite(unpinId),
                "Empty Trash" => nav.Kind == ShellNavKind.Mailbox && nav.Role == MailboxRole.Trash,
                "Empty Junk" => nav.Kind == ShellNavKind.Mailbox && nav.Role == MailboxRole.Junk,
                _ => true,
            };
        }
    }

    private void OnThreadMenuOpening(object? sender, CancelEventArgs e)
    {
        if (sender is ContextMenu menu)
        {
            ApplyObjectMenu(menu.Items);
            RebuildMailboxTargetMenu(ThreadMoveItem, OnRecentMoveClick, OnMoveClick);
            RebuildMailboxTargetMenu(ThreadCopyItem, OnRecentCopyClick, OnCopyClick);
        }
    }

    private void OnReadingMenuOpening(object? sender, EventArgs e)
    {
        if (sender is MenuFlyout flyout)
        {
            ApplyObjectMenu(flyout.Items);
            RebuildMailboxTargetMenu(ReadMoveItem, OnRecentMoveClick, OnMoveClick);
            RebuildMailboxTargetMenu(ReadCopyItem, OnRecentCopyClick, OnCopyClick);
        }
    }

    private void RebuildMailboxTargetMenu(
        MenuItem? menuItem,
        EventHandler<RoutedEventArgs> recentClick,
        EventHandler<RoutedEventArgs> chooseClick)
    {
        if (menuItem is null)
        {
            return;
        }

        menuItem.Items.Clear();
        if (_browse is null || _browse.ShowingOutbox)
        {
            return;
        }

        var ownerId = _browse.SelectedAccountId
            ?? _browse.Threads.FirstOrDefault(item => item.Latest.Id == _browse.SelectedThreadId)?.Latest.AccountId
            ?? _browse.Messages.FirstOrDefault(item => item.Id == _browse.SelectedMessageId)?.AccountId;
        if (ownerId is null)
        {
            return;
        }

        var currentMailboxId = _browse.SelectedMailboxId
            ?? _browse.Messages.FirstOrDefault(item => item.Id == _browse.SelectedMessageId)?.MailboxId;
        var destinations = _browse.AllMailboxes
            .Where(mailbox => mailbox.AccountId == ownerId)
            .ToList();
        var recents = MailShellFormatting.RecentMoveTargets(
            destinations,
            _browse.RecentMoveMailboxIds,
            currentMailboxId);
        if (recents.Count == 0)
        {
            return;
        }

        foreach (var (destinationId, label) in recents)
        {
            var item = new MenuItem
            {
                Header = label,
                Tag = destinationId,
            };
            item.Click += recentClick;
            menuItem.Items.Add(item);
        }

        menuItem.Items.Add(new Separator());
        var choose = new MenuItem { Header = "Choose Mailbox..." };
        choose.Click += chooseClick;
        menuItem.Items.Add(choose);
    }

    private void ApplyObjectMenu(System.Collections.IEnumerable items)
    {
        var outbox = _browse?.ShowingOutbox == true;
        var canExpand = _browse is { ShowingOutbox: false, Conversation.Count: > 1 };
        var expanded = _browse?.ConversationExpanded == true;
        foreach (var obj in items)
        {
            if (obj is not MenuItem item)
            {
                continue;
            }

            var header = item.Header?.ToString();
            var visible = MailShellFormatting.ObjectMenuConversationOverride(header, canExpand, expanded)
                ?? MailShellFormatting.ObjectMenuOutboxOverride(header, outbox);
            if (visible is { } value)
            {
                item.IsVisible = value;
            }
        }
    }

    private void OnShellKeyDown(object? sender, KeyEventArgs e)
    {
        var composeSurface = ComposeSurface?.IsVisible == true;
        if (TryHandleAddressSuggestKey(e))
        {
            return;
        }

        if (TryHandleComposeTab(e))
        {
            e.Handled = true;
            return;
        }

        if (FindInMessageBar?.IsVisible == true
            && e.Key == Key.Escape
            && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            HideFindInMessageBar();
            return;
        }

        if (!composeSurface
            && e.Key == Key.Escape
            && e.KeyModifiers == KeyModifiers.None
            && e.Source is not MenuItem
            && e.Source is not Button
            && e.Source is not ToggleButton
            && (e.Source is not TextBox
                || e.Source == MessageBodyBox
                || e.Source == QuotedBodyBox)
            && (IsReadingFocused()
                || (ReadSurface is { } reading && IsInside(reading, e.Source))
                || (OutboxSurface is { } outbox && IsInside(outbox, e.Source))))
        {
            e.Handled = true;
            OnFocusListClick();
            return;
        }

        if (!composeSurface
            && e.Key == Key.Escape
            && e.KeyModifiers == KeyModifiers.None
            && e.Source is not TextBox
            && e.Source is not MenuItem
            && ThreadsList is not null
            && (ThreadsList.IsFocused || IsInside(ThreadsList, e.Source))
            && _browse is not null
            && !string.IsNullOrEmpty(_browse.SearchQuery))
        {
            e.Handled = true;
            _ = ClearSearchAsync(focusList: true);
            return;
        }

        if (!composeSurface
            && e.KeyModifiers == KeyModifiers.None
            && e.Key is Key.OemBackslash or Key.OemPipe
            && (e.Source == MessageBodyBox || e.Source == QuotedBodyBox))
        {
            e.Handled = true;
            OnToggleQuotedClick(sender, e);
            return;
        }

        if (!composeSurface
            && e.Key == Key.Enter
            && e.KeyModifiers == KeyModifiers.Control
            && (e.Source == MessageBodyBox || e.Source == QuotedBodyBox))
        {
            if (TryOpenPlainLinkAtCaret(e.Source as TextBox))
            {
                e.Handled = true;
            }

            return;
        }

        if (composeSurface
            && e.Key == Key.V
            && e.KeyModifiers == KeyModifiers.Control
            && IsInsideCompose(e.Source))
        {
            e.Handled = true;
            OnComposePaste(e.Source);
            return;
        }

        if (MailShellShortcuts.AccountShortcutIndex(
                e.Key,
                e.KeyModifiers,
                e.Source is TextBox,
                composeSurface) is { } accountIndex)
        {
            e.Handled = true;
            if (accountIndex < 0)
            {
                OnGoToUnifiedInboxClick();
            }
            else
            {
                OnGoToAccountIndexClick(accountIndex);
            }

            return;
        }

        var listFocused = ThreadsList is not null
            && (ThreadsList.IsFocused || IsInside(ThreadsList, e.Source));
        var boundary = MailShellShortcuts.ListBoundaryShortcut(
            e.Key,
            e.KeyModifiers,
            listFocused,
            e.Source is TextBox,
            composeSurface);
        if (boundary != MailShellShortcut.None)
        {
            e.Handled = true;
            switch (boundary)
            {
                case MailShellShortcut.FirstThread:
                    OnFirstThreadClick(sender, e);
                    break;
                case MailShellShortcut.LastThread:
                    OnLastThreadClick(sender, e);
                    break;
                case MailShellShortcut.NextThread:
                    OnNextThreadClick(sender, e);
                    break;
                case MailShellShortcut.PreviousThread:
                    OnPreviousThreadClick(sender, e);
                    break;
                case MailShellShortcut.PageThread:
                    OnPageThreadClick(forward: true);
                    break;
                case MailShellShortcut.PageThreadUp:
                    OnPageThreadClick(forward: false);
                    break;
                case MailShellShortcut.FocusReading:
                    OpenDraftOrFocusReading();
                    break;
                case MailShellShortcut.ExpandConversation:
                    OnExpandConversationClick(sender, e);
                    break;
                case MailShellShortcut.CollapseConversation:
                    OnCollapseConversationClick(sender, e);
                    break;
            }

            return;
        }

        var shortcut = MailShellShortcuts.FromKey(
            e.Key,
            e.KeyModifiers,
            e.Source is TextBox,
            composeSurface);
        if (shortcut == MailShellShortcut.None)
        {
            if (TryHandleListTypeahead(e, composeSurface)
                || TryHandleNavTypeahead(e, composeSurface))
            {
                e.Handled = true;
            }

            return;
        }

        if (shortcut is MailShellShortcut.PageReading
            or MailShellShortcut.PageReadingUp
            or MailShellShortcut.PageViewport
            or MailShellShortcut.PageViewportUp)
        {
            var attachmentFocused = ThreadAttachmentsList is { IsVisible: true }
                && (ThreadAttachmentsList.IsFocused || IsInside(ThreadAttachmentsList, e.Source));
            if (IsNavFocused()
                || e.Source is Button or ToggleButton or MenuItem
                || MailShellFormatting.ShouldDeferPagingToAttachmentStrip(
                    ThreadAttachmentsList?.IsVisible == true,
                    attachmentFocused))
            {
                return;
            }

            var down = shortcut is MailShellShortcut.PageReading or MailShellShortcut.PageViewport;
            if (TryPageReading(down))
            {
                e.Handled = true;
                return;
            }

            var htmlFocused = MessageHtmlView is { IsVisible: true }
                && (MessageHtmlView.IsFocused || IsInside(MessageHtmlView, e.Source));
            if (MailShellFormatting.ShouldDeferPagingToHtml(
                    MessageHtmlView?.IsVisible == true,
                    htmlFocused,
                    canPageOuter: false))
            {
                return;
            }

            if (shortcut is MailShellShortcut.PageReading or MailShellShortcut.PageReadingUp)
            {
                e.Handled = true;
                if (down)
                {
                    OnNextUnreadClick(sender, e);
                }
                else
                {
                    OnPreviousUnreadClick(sender, e);
                }

                return;
            }

            e.Handled = true;
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
            case MailShellShortcut.ForwardAsAttachment:
                OnForwardAsAttachmentClick(sender, e);
                return;
            case MailShellShortcut.MarkUnread:
                OnMarkUnreadShortcut(sender, e);
                return;
            case MailShellShortcut.MarkRead:
                OnMarkReadClick(sender, e);
                return;
            case MailShellShortcut.Flag:
                OnFlagClick(sender, e);
                return;
            case MailShellShortcut.Delete:
                OnDeleteClick(sender, e);
                return;
            case MailShellShortcut.PermanentlyDelete:
                OnPermanentlyDeleteClick(sender, e);
                return;
            case MailShellShortcut.FocusSearch:
                if (DailyShell?.IsVisible != true || MessageSearchBox is null)
                {
                    return;
                }

                MessageSearchBox.Focus();
                MessageSearchBox.SelectAll();
                ShowSearchHistory();
                return;
            case MailShellShortcut.NewDraft:
                OnComposeClick(sender, e);
                return;
            case MailShellShortcut.NextUnread:
                OnNextUnreadClick(sender, e);
                return;
            case MailShellShortcut.PreviousUnread:
                OnPreviousUnreadClick(sender, e);
                return;
            case MailShellShortcut.NextFlagged:
                OnNextFlaggedClick(sender, e);
                return;
            case MailShellShortcut.PreviousFlagged:
                OnPreviousFlaggedClick(sender, e);
                return;
            case MailShellShortcut.NextThread:
                OnNextThreadClick(sender, e);
                return;
            case MailShellShortcut.PreviousThread:
                OnPreviousThreadClick(sender, e);
                return;
            case MailShellShortcut.FirstThread:
                OnFirstThreadClick(sender, e);
                return;
            case MailShellShortcut.LastThread:
                OnLastThreadClick(sender, e);
                return;
            case MailShellShortcut.NextInConversation:
                OnNextInConversationClick(sender, e);
                return;
            case MailShellShortcut.PreviousInConversation:
                OnPreviousInConversationClick(sender, e);
                return;
            case MailShellShortcut.FirstInConversation:
                OnFirstInConversationClick(sender, e);
                return;
            case MailShellShortcut.LastInConversation:
                OnLastInConversationClick(sender, e);
                return;
            case MailShellShortcut.Sync:
                OnSyncNowClick(sender, e);
                return;
            case MailShellShortcut.Move:
                OnMoveClick(sender, e);
                return;
            case MailShellShortcut.CopyToMailbox:
                OnCopyClick(sender, e);
                return;
            case MailShellShortcut.Archive:
                OnArchiveClick(sender, e);
                return;
            case MailShellShortcut.Send:
                OnSendDraftClick(sender, e);
                return;
            case MailShellShortcut.SaveDraft:
                OnSaveDraftShortcutClick(sender, e);
                return;
            case MailShellShortcut.SaveMessage:
                OnSaveMessageEmlClick(sender, e);
                return;
            case MailShellShortcut.Attach:
                OnAttachDraftClick(sender, e);
                return;
            case MailShellShortcut.FocusCc:
                OnFocusCcClick();
                return;
            case MailShellShortcut.FocusBcc:
                OnFocusBccClick();
                return;
            case MailShellShortcut.FocusHtml:
                OnFocusHtmlClick();
                return;
            case MailShellShortcut.DiscardDraft:
                OnDiscardDraftClick(sender, e);
                return;
            case MailShellShortcut.UndoTrash:
                OnUndoTrashClick(sender, e);
                return;
            case MailShellShortcut.FindInMessage:
                OnFindInMessageClick(sender, e);
                return;
            case MailShellShortcut.FindPreviousInMessage:
                OnFindPreviousClick(sender, e);
                return;
            case MailShellShortcut.PageReading:
                OnPageReadingClick(down: true);
                return;
            case MailShellShortcut.PageReadingUp:
                OnPageReadingClick(down: false);
                return;
            case MailShellShortcut.PageViewport:
                OnPageViewportClick(down: true);
                return;
            case MailShellShortcut.PageViewportUp:
                OnPageViewportClick(down: false);
                return;
            case MailShellShortcut.PageThread:
                OnPageThreadClick(forward: true);
                return;
            case MailShellShortcut.PageThreadUp:
                OnPageThreadClick(forward: false);
                return;
            case MailShellShortcut.NextMailbox:
                OnNextMailboxClick();
                return;
            case MailShellShortcut.PreviousMailbox:
                OnPreviousMailboxClick();
                return;
            case MailShellShortcut.FocusNextPane:
                CyclePane(1);
                return;
            case MailShellShortcut.FocusPreviousPane:
                CyclePane(-1);
                return;
            case MailShellShortcut.FocusReading:
                OpenDraftOrFocusReading();
                return;
            case MailShellShortcut.FocusList:
                OnFocusListClick();
                return;
            case MailShellShortcut.ExpandConversation:
                OnExpandConversationClick(sender, e);
                return;
            case MailShellShortcut.CollapseConversation:
                OnCollapseConversationClick(sender, e);
                return;
            case MailShellShortcut.ToggleQuoted:
                OnToggleQuotedClick(sender, e);
                return;
            case MailShellShortcut.GoToMailbox:
                OnGoToMailboxClick(sender, e);
                return;
            case MailShellShortcut.GoToInbox:
                OnGoToInboxClick(sender, e);
                return;
            case MailShellShortcut.GoToOutbox:
                OnGoToOutboxClick(sender, e);
                return;
            case MailShellShortcut.GoToSent:
                OnGoToSentClick(sender, e);
                return;
            case MailShellShortcut.GoToDrafts:
                OnGoToDraftsClick(sender, e);
                return;
            case MailShellShortcut.GoToTrash:
                OnGoToTrashClick(sender, e);
                return;
            case MailShellShortcut.GoToJunk:
                OnGoToJunkClick(sender, e);
                return;
            case MailShellShortcut.NewMailbox:
                OnNewMailboxClick(sender, e);
                return;
            case MailShellShortcut.RenameMailbox:
                OnRenameMailboxClick(sender, e);
                return;
        }
    }

    private void OnThreadListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ComposeSurface?.IsVisible == true)
        {
            return;
        }

        OpenDraftOrFocusReading();
    }

    private void OpenDraftOrFocusReading()
    {
        if (IsDraftsScope(RequireBrowse()))
        {
            OnEditAsNewClick(this, new RoutedEventArgs());
            return;
        }

        OnFocusReadingClick();
    }

    private void OnFocusReadingClick()
    {
        if (ComposeSurface?.IsVisible == true)
        {
            return;
        }

        if (OutboxSurface?.IsVisible == true)
        {
            OutboxSurface.Focus();
            return;
        }

        if (ReadSurface?.IsVisible != true)
        {
            return;
        }

        if (MessageBodyBox is { IsVisible: true })
        {
            MessageBodyBox.Focus();
            return;
        }

        if (MessageHtmlView is { IsVisible: true })
        {
            MessageHtmlView.Focus();
            return;
        }

        ReadingScroll?.Focus();
    }

    private void OnFocusListClick()
    {
        if (ComposeSurface?.IsVisible == true)
        {
            return;
        }

        ThreadsList?.Focus();
    }

    private bool IsReadingFocused()
    {
        if (ReadSurface?.IsVisible != true)
        {
            return false;
        }

        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        return ReadSurface.IsFocused || IsInside(ReadSurface, focused);
    }

    private void CyclePane(int delta)
    {
        var panes = new List<Control>();
        if (NavTree is { IsVisible: true })
        {
            panes.Add(NavTree);
        }

        if (ThreadsList is { IsVisible: true })
        {
            panes.Add(ThreadsList);
        }

        if (ComposeSurface?.IsVisible == true)
        {
            if (ComposeBodyBox is { IsVisible: true })
            {
                panes.Add(ComposeBodyBox);
            }
            else if (ComposeToBox is { IsVisible: true })
            {
                panes.Add(ComposeToBox);
            }
        }
        else
        {
            if (ReadSurface?.IsVisible == true && ThreadAttachmentsList is { IsVisible: true })
            {
                panes.Add(ThreadAttachmentsList);
            }

            if (FindInMessageBar?.IsVisible == true && FindInMessageBox is { IsVisible: true })
            {
                panes.Add(FindInMessageBox);
            }
            else if (MessageBodyBox is { IsVisible: true })
            {
                panes.Add(MessageBodyBox);
            }
            else if (MessageHtmlView is { IsVisible: true })
            {
                panes.Add(MessageHtmlView);
            }
        }

        if (panes.Count == 0)
        {
            return;
        }

        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        var current = panes.FindIndex(pane => pane.IsFocused || IsInside(pane, focused));
        var next = MailShellFormatting.NextFindIndex(panes.Count, current, delta >= 0);
        if (next >= 0)
        {
            panes[next].Focus();
        }
    }

    private void OnListTypeaheadTick(object? sender, EventArgs e)
    {
        _listTypeaheadTimer.Stop();
        _listTypeahead = string.Empty;
    }

    private void OnNavTypeaheadTick(object? sender, EventArgs e)
    {
        _navTypeaheadTimer.Stop();
        _navTypeahead = string.Empty;
    }

    private bool TryHandleListTypeahead(KeyEventArgs e, bool composeSurface)
    {
        if (composeSurface
            || e.KeyModifiers != KeyModifiers.None
            || e.Source is TextBox
            || ThreadsList is null)
        {
            return false;
        }

        if (!IsInside(ThreadsList, e.Source) && ThreadsList.IsFocused != true)
        {
            return false;
        }

        if (MailShellShortcuts.TypeaheadChar(e.Key) is not { } ch)
        {
            return false;
        }

        _listTypeaheadTimer.Stop();
        var listed = (ThreadsList.ItemsSource as IEnumerable<ThreadRow>)?.ToList() ?? [];
        var current = ThreadsList.SelectedItem as ThreadRow;
        var advance = MailShellFormatting.AdvanceTypeahead(
            listed,
            _listTypeahead,
            ch,
            current,
            RequireBrowse().ListSort);
        _listTypeahead = advance.Prefix;
        _listTypeaheadTimer.Start();
        if (advance.Row is null)
        {
            return true;
        }

        ThreadsList.SelectedItem = advance.Row;
        ScrollSelectedThreadIntoView();
        _ = OpenListedThreadRowAsync(advance.Row);
        return true;
    }

    private bool TryHandleNavTypeahead(KeyEventArgs e, bool composeSurface)
    {
        if (composeSurface
            || e.KeyModifiers != KeyModifiers.None
            || e.Source is TextBox
            || NavTree is null)
        {
            return false;
        }

        if (!IsInside(NavTree, e.Source) && NavTree.IsFocused != true)
        {
            return false;
        }

        if (MailShellShortcuts.TypeaheadChar(e.Key) is not { } ch)
        {
            return false;
        }

        _navTypeaheadTimer.Stop();
        var roots = (NavTree.ItemsSource as IEnumerable<ShellNavItem>)?.ToList() ?? [];
        var listed = MailShellFormatting.FlattenNav(roots);
        var current = NavTree.SelectedItem as ShellNavItem;
        var advance = MailShellFormatting.AdvanceNavTypeahead(listed, _navTypeahead, ch, current);
        _navTypeahead = advance.Prefix;
        _navTypeaheadTimer.Start();
        if (advance.Item is null)
        {
            return true;
        }

        _ = RevealAndSelectNavAsync(advance.Item);
        return true;
    }

    private bool TryHandleComposeTab(KeyEventArgs e)
    {
        if (ComposeSurface?.IsVisible != true
            || e.Key != Key.Tab
            || e.Source is not TextBox box)
        {
            return false;
        }

        var forward = !e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (box == ComposeBodyBox)
        {
            if (forward)
            {
                return false;
            }

            if (ComposeSubjectBox is { IsVisible: true })
            {
                ComposeSubjectBox.Focus();
                return true;
            }

            return false;
        }

        if (box == ComposeHtmlBox)
        {
            if (!forward && ComposeBodyBox is { IsVisible: true })
            {
                ComposeBodyBox.Focus();
                return true;
            }

            return false;
        }

        var fields = new List<TextBox>();
        if (ComposeToBox is { IsVisible: true })
        {
            fields.Add(ComposeToBox);
        }

        if (ComposeCcBox is { IsVisible: true })
        {
            fields.Add(ComposeCcBox);
        }

        if (ComposeBccBox is { IsVisible: true })
        {
            fields.Add(ComposeBccBox);
        }

        if (ComposeSubjectBox is { IsVisible: true })
        {
            fields.Add(ComposeSubjectBox);
        }

        var index = fields.IndexOf(box);
        var next = MailShellFormatting.ComposeTabTarget(fields.Count, index, forward);
        if (next >= 0)
        {
            fields[next].Focus();
            return true;
        }

        if (forward && ComposeBodyBox is { IsVisible: true })
        {
            ComposeBodyBox.Focus();
            return true;
        }

        return false;
    }

    private void OnNavFocusedDeleteClick(object? sender, RoutedEventArgs e)
    {
        var browse = RequireBrowse();
        MailboxRole? role = null;
        if (browse.SelectedMailboxId is { } mailboxId)
        {
            role = browse.AllMailboxes.FirstOrDefault(item => item.Id == mailboxId)?.Role
                ?? browse.Mailboxes.FirstOrDefault(item => item.Id == mailboxId)?.Role;
        }

        switch (MailShellFormatting.NavFocusedDelete(browse.SelectedMailboxId, role))
        {
            case NavFocusedDeleteAction.EmptyTrash:
                OnEmptyTrashClick(sender, e);
                return;
            case NavFocusedDeleteAction.EmptyJunk:
                OnEmptyJunkClick(sender, e);
                return;
            case NavFocusedDeleteAction.DeleteMailbox:
                OnDeleteMailboxClick(sender, e);
                return;
        }
    }

    private bool IsNavFocused()
    {
        if (NavTree is null)
        {
            return false;
        }

        if (NavTree.IsFocused)
        {
            return true;
        }

        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        return focused is not null && IsInside(NavTree, focused);
    }

    private static bool IsInside(Visual root, object? source)
    {
        for (Visual? current = source as Visual; current is not null; current = current.GetVisualParent())
        {
            if (ReferenceEquals(current, root))
            {
                return true;
            }
        }

        return false;
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppressSelectionHandlers)
        {
            return;
        }

        _searchTimer.Stop();
        _searchTimer.Start();
        if (MessageSearchBox?.IsFocused == true)
        {
            ShowSearchHistory();
        }
    }

    private void OnMessageSearchGotFocus(object? sender, RoutedEventArgs e) =>
        ShowSearchHistory();

    private void ShowSearchHistory()
    {
        if (SearchHistoryPopup is null || SearchHistoryList is null || MessageSearchBox is null || _browse is null)
        {
            return;
        }

        var items = MailShellFormatting.MatchingRecentSearches(_browse.RecentSearches, MessageSearchBox.Text);
        SearchHistoryList.ItemsSource = items;
        SearchHistoryList.SelectedIndex = items.Count > 0 ? 0 : -1;
        SearchHistoryPopup.PlacementTarget = MessageSearchBox;
        SearchHistoryPopup.IsOpen = items.Count > 0;
    }

    private void HideSearchHistory()
    {
        if (SearchHistoryPopup is not null)
        {
            SearchHistoryPopup.IsOpen = false;
        }
    }

    private async Task ApplySearchHistoryAsync(string query)
    {
        if (MessageSearchBox is not null)
        {
            MessageSearchBox.Text = query;
        }

        HideSearchHistory();
        _searchTimer.Stop();
        await RequireBrowse().SearchAsync(query).ConfigureAwait(true);
        await RequireBrowse().RememberRecentSearchAsync(query).ConfigureAwait(true);
        BindAfterSearch();
    }

    private async void OnSearchHistoryPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (SearchHistoryList?.SelectedItem is not string query)
        {
            return;
        }

        try
        {
            await ApplySearchHistoryAsync(query).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private void OnSortMenuOpening(object? sender, EventArgs e)
    {
        if (sender is not MenuFlyout flyout || _browse is null)
        {
            return;
        }

        var current = MailShellFormatting.ListSortLabel(_browse.ListSort);
        foreach (var obj in flyout.Items)
        {
            if (obj is MenuItem item)
            {
                item.ToggleType = MenuItemToggleType.Radio;
                item.IsChecked = item.Header?.ToString() == current;
            }
        }
    }

    private async void OnSortMenuClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Header: string header }
            || MailShellFormatting.ParseListSortLabel(header) is not { } sort)
        {
            return;
        }

        var browse = RequireBrowse();
        if (browse.ListSort == sort)
        {
            return;
        }

        browse.ListSort = sort;
        try
        {
            await browse.PersistListSortAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }

        BindLists();
    }

    private async void OnUnreadFilterClick(object? sender, RoutedEventArgs e) =>
        await ToggleSearchOperatorAsync(
                RequireBrowse().ShowingOutbox ? MessageSearch.ToggleFailed : MessageSearch.ToggleUnread)
            .ConfigureAwait(true);

    private async void OnFlaggedFilterClick(object? sender, RoutedEventArgs e) =>
        await ToggleSearchOperatorAsync(MessageSearch.ToggleFlagged).ConfigureAwait(true);

    private async void OnAttachmentFilterClick(object? sender, RoutedEventArgs e) =>
        await ToggleSearchOperatorAsync(MessageSearch.ToggleAttachment).ConfigureAwait(true);

    private async Task ToggleSearchOperatorAsync(Func<string?, string> toggle)
    {
        var browse = RequireBrowse();
        try
        {
            _searchTimer.Stop();
            var query = toggle(browse.SearchQuery);
            await browse.SearchAsync(query).ConfigureAwait(true);
            await browse.RememberRecentSearchAsync(query).ConfigureAwait(true);
            BindAfterSearch();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnSearchTick(object? sender, EventArgs e)
    {
        _searchTimer.Stop();
        if (_browse is null || MessageSearchBox is null)
        {
            return;
        }

        var query = MessageSearchBox.Text ?? string.Empty;
        if (query == _browse.SearchQuery)
        {
            return;
        }

        try
        {
            await _browse.SearchAsync(query).ConfigureAwait(true);
            BindAfterSearch();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnMessageSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (SearchHistoryPopup?.IsOpen == true)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                HideSearchHistory();
                return;
            }

            if (e.Key == Key.Down)
            {
                e.Handled = true;
                MoveSearchHistory(1);
                return;
            }

            if (e.Key == Key.Up)
            {
                e.Handled = true;
                MoveSearchHistory(-1);
                return;
            }

            if (e.Key == Key.Enter && SearchHistoryList?.SelectedItem is string chosen)
            {
                e.Handled = true;
                try
                {
                    await ApplySearchHistoryAsync(chosen).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    SetStatus(ex.Message);
                }

                return;
            }

            if (e.Key == Key.Delete && SearchHistoryList?.SelectedItem is string forget)
            {
                e.Handled = true;
                try
                {
                    var index = SearchHistoryList.SelectedIndex;
                    await RequireBrowse().ForgetRecentSearchAsync(forget).ConfigureAwait(true);
                    ShowSearchHistory();
                    if (SearchHistoryPopup?.IsOpen == true && SearchHistoryList.ItemCount > 0)
                    {
                        SearchHistoryList.SelectedIndex = Math.Min(index, SearchHistoryList.ItemCount - 1);
                    }
                }
                catch (Exception ex)
                {
                    SetStatus(ex.Message);
                }

                return;
            }
        }
        else if (e.Key == Key.Down)
        {
            ShowSearchHistory();
            if (SearchHistoryPopup?.IsOpen == true)
            {
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Escape)
        {
            if (string.IsNullOrEmpty(MessageSearchBox.Text) && string.IsNullOrEmpty(RequireBrowse().SearchQuery))
            {
                return;
            }

            e.Handled = true;
            await ClearSearchAsync(focusList: false).ConfigureAwait(true);
            return;
        }

        if (e.Key != Key.Enter)
        {
            return;
        }

        _searchTimer.Stop();
        var query = MessageSearchBox.Text ?? string.Empty;
        await RequireBrowse().SearchAsync(query).ConfigureAwait(true);
        await RequireBrowse().RememberRecentSearchAsync(query).ConfigureAwait(true);
        HideSearchHistory();
        BindAfterSearch();
    }

    private void MoveSearchHistory(int delta)
    {
        if (SearchHistoryList is null || SearchHistoryList.ItemCount == 0)
        {
            return;
        }

        var next = SearchHistoryList.SelectedIndex + delta;
        if (next < 0)
        {
            next = SearchHistoryList.ItemCount - 1;
        }
        else if (next >= SearchHistoryList.ItemCount)
        {
            next = 0;
        }

        SearchHistoryList.SelectedIndex = next;
    }

    private async void OnThreadSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionHandlers || _browse is null)
        {
            return;
        }

        if (ThreadsList.SelectedItem is not ThreadRow row)
        {
            return;
        }

        if (row.IsGroupHeader)
        {
            var listed = (ThreadsList.ItemsSource as IEnumerable<ThreadRow>)?.ToList() ?? [];
            var previous = e.RemovedItems.OfType<ThreadRow>().FirstOrDefault(item => !item.IsGroupHeader);
            var target = MailShellFormatting.RowAfterCrossingHeader(listed, row, previous);
            _suppressSelectionHandlers = true;
            try
            {
                ThreadsList.SelectedItem = target;
            }
            finally
            {
                _suppressSelectionHandlers = false;
            }

            if (target is not null && !ReferenceEquals(target, previous))
            {
                await OpenListedThreadRowAsync(target).ConfigureAwait(true);
            }

            return;
        }

        await OpenListedThreadRowAsync(row).ConfigureAwait(true);
    }

    private async Task OpenListedThreadRowAsync(ThreadRow row)
    {
        _findHitIndex = -1;
        _quotedExpanded = false;
        _quotedForMessageId = null;
        SetStatus(string.Empty);
        try
        {
            var browse = RequireBrowse();
            if (ComposeSurface?.IsVisible == true
                && MailShellFormatting.IsCurrentListRow(row, browse.SelectedThreadId, browse.SelectedMessageId))
            {
                return;
            }

            await MaybeDiscardBlankDraftAsync().ConfigureAwait(true);
            if (row.OutboxItem is { } outbox)
            {
                ShowOutboxSurface(outbox);
                return;
            }

            if (row.Thread is { } thread)
            {
                await browse.OpenListedThreadAsync(thread.Latest.Id).ConfigureAwait(true);
                await RequireCompose().SelectAccountAsync(thread.Latest.AccountId).ConfigureAwait(true);
                ShowReadSurface();
                BindLists();
                return;
            }

            if (row.SearchHit is { } hit)
            {
                await browse.OpenSearchHitAsync(hit.Id).ConfigureAwait(true);
                await RequireCompose().SelectAccountAsync(hit.AccountId).ConfigureAwait(true);
                ShowReadSurface();
                BindLists();
            }
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }
    private async void OnConversationCardTapped(object? sender, TappedEventArgs e)
    {
        if (_suppressSelectionHandlers || _browse is null)
        {
            return;
        }

        if (sender is not Control { DataContext: ConversationCard card })
        {
            return;
        }

        e.Handled = true;

        if (card.Message.Id == _browse.SelectedMessageId)
        {
            return;
        }

        try
        {
            await _browse.SelectMessageAsync(card.Message.Id).ConfigureAwait(true);
            await _browse.RefreshConversationAsync().ConfigureAwait(true);
            BindLists();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnConversationAttachmentClipTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: ConversationCard { Attachments.Count: > 0 } card })
        {
            return;
        }

        e.Handled = true;
        try
        {
            await RequireBrowse().OpenAttachmentAsync(card.Attachments[0].Id).ConfigureAwait(true);
            BindLists();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnConversationAttachmentTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: AttachmentInfo attachment })
        {
            return;
        }

        e.Handled = true;
        try
        {
            await RequireBrowse().OpenAttachmentAsync(attachment.Id).ConfigureAwait(true);
            BindLists();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnReplyAllClick(object? sender, RoutedEventArgs e) =>
        await StartReplyAsync((compose, accountId, messageId) =>
                compose.StartReplyAllAsync(accountId, messageId, SelectedPlainQuote()))
            .ConfigureAwait(true);

    private async void OnReplyClick(object? sender, RoutedEventArgs e)
    {
        if (RequireBrowse().ShowingOutbox)
        {
            try
            {
                await RetrySelectedOutboxAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }

            return;
        }

        await StartReplyAsync((compose, accountId, messageId) =>
                compose.StartReplyAsync(accountId, messageId, SelectedPlainQuote()))
            .ConfigureAwait(true);
    }

    private async void OnForwardClick(object? sender, RoutedEventArgs e) =>
        await StartReplyAsync((compose, accountId, messageId) => compose.StartForwardAsync(accountId, messageId))
            .ConfigureAwait(true);

    private async void OnForwardAsAttachmentClick(object? sender, RoutedEventArgs e) =>
        await StartReplyAsync((compose, accountId, messageId) =>
                compose.StartForwardAsAttachmentAsync(accountId, messageId))
            .ConfigureAwait(true);

    private async void OnEditAsNewClick(object? sender, RoutedEventArgs e) =>
        await StartReplyAsync((compose, accountId, messageId) => compose.StartEditAsNewAsync(accountId, messageId))
            .ConfigureAwait(true);

    private string? SelectedPlainQuote()
    {
        if (MessageBodyBox is { IsVisible: true }
            && !string.IsNullOrWhiteSpace(MessageBodyBox.SelectedText))
        {
            return MessageBodyBox.SelectedText;
        }

        if (QuotedBodyBox is { IsVisible: true }
            && !string.IsNullOrWhiteSpace(QuotedBodyBox.SelectedText))
        {
            return QuotedBodyBox.SelectedText;
        }

        return null;
    }

    private async Task StartReplyAsync(
        Func<ComposeOutboxShell, Guid, Guid, Task<DraftInfo>> start)
    {
        if (IsNavFocused())
        {
            return;
        }

        var browse = RequireBrowse();
        var messageId = browse.SelectedMessageId
            ?? browse.Threads.FirstOrDefault(item => item.Latest.Id == browse.SelectedThreadId)?.Latest.Id
            ?? browse.Threads.FirstOrDefault(item =>
                browse.SelectedThreadId is { } threadId
                && item.Messages.Any(message => message.Id == threadId))?.Latest.Id;
        if (messageId is null)
        {
            return;
        }

        var message = browse.Messages.FirstOrDefault(item => item.Id == messageId)
            ?? browse.Conversation.Select(card => card.Message).FirstOrDefault(item => item.Id == messageId)
            ?? browse.Threads.SelectMany(item => item.Messages).FirstOrDefault(item => item.Id == messageId);
        if (message is null)
        {
            return;
        }

        var compose = RequireCompose();
        var draft = await start(compose, message.AccountId, message.Id).ConfigureAwait(true);
        await compose.LoadRecentAddressesAsync().ConfigureAwait(true);
        FillComposeFromDraft(draft);
        BindLists();
        FocusComposeAfterStart();
    }

    private void FocusComposeAfterStart()
    {
        if (MailShellFormatting.NeedsRecipient(ComposeToBox.Text, ComposeCcBox.Text, ComposeBccBox.Text))
        {
            ComposeToBox.Focus();
            return;
        }

        ComposeBodyBox.Focus();
        ComposeBodyBox.CaretIndex = 0;
    }

    private async void OnPinFavoriteClick(object? sender, RoutedEventArgs e)
    {
        if (_navMenuItem?.MailboxId is not { } mailboxId)
        {
            return;
        }

        try
        {
            await RequireBrowse().PinFavoriteAsync(mailboxId).ConfigureAwait(true);
            BindLists();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnUnpinFavoriteClick(object? sender, RoutedEventArgs e)
    {
        if (_navMenuItem?.MailboxId is not { } mailboxId)
        {
            return;
        }

        try
        {
            await RequireBrowse().UnpinFavoriteAsync(mailboxId).ConfigureAwait(true);
            BindLists();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnMarkMailboxReadClick(object? sender, RoutedEventArgs e)
    {
        await EnsureAccountContextAsync().ConfigureAwait(true);
        await RequireBrowse().MarkCurrentReadAsync().ConfigureAwait(true);
        BindLists();
    }

    private async void OnMarkUnreadClick(object? sender, RoutedEventArgs e)
    {
        if (IsNavFocused())
        {
            OnMarkMailboxReadClick(sender, e);
            return;
        }

        var browse = RequireBrowse();
        if (browse.SelectedMessageId is null && browse.SelectedThreadId is null)
        {
            return;
        }

        await browse.ToggleSelectedReadAsync().ConfigureAwait(true);
        BindLists();
    }

    private async void OnMarkUnreadShortcut(object? sender, RoutedEventArgs e)
    {
        if (IsNavFocused())
        {
            OnMarkMailboxReadClick(sender, e);
            return;
        }

        var browse = RequireBrowse();
        if (browse.SelectedMessageId is null && browse.SelectedThreadId is null)
        {
            return;
        }

        await browse.MarkSelectedUnreadAsync().ConfigureAwait(true);
        BindLists();
    }

    private async void OnMarkReadClick(object? sender, RoutedEventArgs e)
    {
        if (IsNavFocused())
        {
            OnMarkMailboxReadClick(sender, e);
            return;
        }

        var browse = RequireBrowse();
        if (browse.SelectedMessageId is null && browse.SelectedThreadId is null)
        {
            return;
        }

        await browse.MarkSelectedReadAsync().ConfigureAwait(true);
        BindLists();
    }

    private async void OnThreadAttachmentClipPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: ThreadRow { HasAttachments: true } row })
        {
            return;
        }

        e.Handled = true;
        try
        {
            await RequireBrowse().OpenFirstThreadAttachmentAsync(row.Id).ConfigureAwait(true);
            BindLists();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnThreadFlagPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: ThreadRow { CanFlag: true } row })
        {
            return;
        }

        e.Handled = true;
        try
        {
            await RequireBrowse().ToggleFlagAsync(row.Id).ConfigureAwait(true);
            BindLists();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnFlagClick(object? sender, RoutedEventArgs e)
    {
        if (IsNavFocused())
        {
            return;
        }

        var browse = RequireBrowse();
        if (browse.SelectedMessageId is null && browse.SelectedThreadId is null)
        {
            return;
        }

        await browse.ToggleSelectedFlagAsync().ConfigureAwait(true);
        BindLists();
    }

    private void OnThreadListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(ThreadsList);
        if (FindAncestorData<ThreadRow>(e.Source) is not { } row
            || !MailShellFormatting.CanSelectThreadRow(row))
        {
            return;
        }

        if (point.Properties.IsRightButtonPressed)
        {
            if (!ReferenceEquals(ThreadsList.SelectedItem, row))
            {
                ThreadsList.SelectedItem = row;
            }

            return;
        }

        if (!point.Properties.IsLeftButtonPressed
            || row.OutboxItem is not null)
        {
            return;
        }

        _pendingThreadDrag = e;
        _threadDragStart = e.GetPosition(ThreadsList);
        _threadDragRow = row;
    }

    private async void OnThreadListPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pendingThreadDrag is null || _threadDragRow is null)
        {
            return;
        }

        if (!e.GetCurrentPoint(ThreadsList).Properties.IsLeftButtonPressed)
        {
            ClearPendingThreadDrag();
            return;
        }

        var current = e.GetPosition(ThreadsList);
        if (!MailShellFormatting.ShouldStartDrag(
                current.X - _threadDragStart.X,
                current.Y - _threadDragStart.Y))
        {
            return;
        }

        var press = _pendingThreadDrag;
        var row = _threadDragRow;
        ClearPendingThreadDrag();
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(ThreadDragFormat, row));
        try
        {
            var accountId = row.Thread?.Latest.AccountId ?? row.SearchHit?.AccountId;
            var messageId = row.Thread?.Latest.Id ?? row.SearchHit?.Id;
            if (accountId is { } dragAccountId && messageId is { } dragMessageId)
            {
                var path = await RequireBrowse()
                    .MaterializeMessageEmlAsync(dragAccountId, dragMessageId, row.Subject)
                    .ConfigureAwait(true);
                var top = TopLevel.GetTopLevel(this);
                if (!string.IsNullOrWhiteSpace(path) && top is not null)
                {
                    var file = await top.StorageProvider.TryGetFileFromPathAsync(path).ConfigureAwait(true);
                    if (file is not null)
                    {
                        transfer.Add(DataTransferItem.Create(DataFormat.File, file));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }

        await DragDrop.DoDragDropAsync(press, transfer, DragDropEffects.Move | DragDropEffects.Copy)
            .ConfigureAwait(true);
    }

    private void OnThreadListPointerReleased(object? sender, PointerReleasedEventArgs e) =>
        ClearPendingThreadDrag();

    private void OnThreadListPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) =>
        ClearPendingThreadDrag();

    private void ClearPendingThreadDrag()
    {
        _pendingThreadDrag = null;
        _threadDragRow = null;
    }

    private async void OnAccountErrorPressed(object? sender, PointerPressedEventArgs e)
    {
        if (FindAncestorData<ShellNavItem>(e.Source) is not { AccountId: { } accountId })
        {
            return;
        }

        e.Handled = true;
        try
        {
            await RequireBrowse().SyncAccountAsync(accountId).ConfigureAwait(true);
            BindLists();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
            BindLists();
        }
    }

    private void OnNavPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(NavTree).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (FindAncestorData<ShellNavItem>(e.Source) is not { Kind: ShellNavKind.Mailbox, MailboxId: { } mailboxId })
        {
            return;
        }

        _pendingFavoriteDrag = e;
        _favoriteDragStart = e.GetPosition(NavTree);
        _favoriteDragId = mailboxId;
    }

    private async void OnNavPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pendingFavoriteDrag is null || _favoriteDragId is not { } mailboxId || NavTree is null)
        {
            return;
        }

        if (!e.GetCurrentPoint(NavTree).Properties.IsLeftButtonPressed)
        {
            ClearPendingFavoriteDrag();
            return;
        }

        var current = e.GetPosition(NavTree);
        if (!MailShellFormatting.ShouldStartDrag(
                current.X - _favoriteDragStart.X,
                current.Y - _favoriteDragStart.Y))
        {
            return;
        }

        var press = _pendingFavoriteDrag;
        ClearPendingFavoriteDrag();
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(FavoriteDragFormat, new FavoriteDrag(mailboxId)));
        await DragDrop.DoDragDropAsync(press, transfer, DragDropEffects.Move).ConfigureAwait(true);
    }

    private void OnNavPointerReleased(object? sender, PointerReleasedEventArgs e) =>
        ClearPendingFavoriteDrag();

    private void OnNavPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) =>
        ClearPendingFavoriteDrag();

    private void ClearPendingFavoriteDrag()
    {
        _pendingFavoriteDrag = null;
        _favoriteDragId = null;
    }

    private void OnNavDragOver(object? sender, DragEventArgs e)
    {
        if (FavoriteDropIndex(e) is not null || FavoriteUnpinDrop(e))
        {
            e.DragEffects = DragDropEffects.Move;
        }
        else
        {
            var kind = NavDropKind(e);
            e.DragEffects = kind == ThreadNavDropKind.None
                ? DragDropEffects.None
                : MailShellFormatting.ThreadNavDropCopies(kind, e.KeyModifiers.HasFlag(KeyModifiers.Control))
                    ? DragDropEffects.Copy
                    : DragDropEffects.Move;
        }

        e.Handled = true;
    }

    private async void OnNavDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.DataTransfer.TryGetValue(FavoriteDragFormat) is { } drag)
        {
            try
            {
                var browse = RequireBrowse();
                if (FavoriteDropIndex(e) is { } dest)
                {
                    if (browse.IsFavorite(drag.MailboxId))
                    {
                        await browse.MoveFavoriteAsync(drag.MailboxId, dest).ConfigureAwait(true);
                    }
                    else
                    {
                        await browse.PinFavoriteAsync(drag.MailboxId, dest).ConfigureAwait(true);
                    }
                }
                else if (FavoriteUnpinDrop(e))
                {
                    await browse.UnpinFavoriteAsync(drag.MailboxId).ConfigureAwait(true);
                }

                BindLists();
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }

            return;
        }

        var row = e.DataTransfer.TryGetValue(ThreadDragFormat);
        var target = FindAncestorData<ShellNavItem>(e.Source);
        if (row is null || target is null)
        {
            return;
        }

        var kind = MailShellFormatting.ThreadNavDrop(row, target);
        if (kind == ThreadNavDropKind.None || target.MailboxId is null)
        {
            return;
        }

        try
        {
            await OpenListedThreadRowAsync(row).ConfigureAwait(true);
            var browse = RequireBrowse();
            if (MailShellFormatting.ThreadNavDropCopies(kind, e.KeyModifiers.HasFlag(KeyModifiers.Control)))
            {
                await browse.CopySelectedToMailboxAsync(target.MailboxId.Value).ConfigureAwait(true);
                SetStatus("Copied.");
            }
            else
            {
                switch (kind)
                {
                    case ThreadNavDropKind.Trash:
                        await browse.MoveSelectedToTrashAsync().ConfigureAwait(true);
                        break;
                    case ThreadNavDropKind.Junk:
                        await browse.MoveSelectedToJunkAsync().ConfigureAwait(true);
                        break;
                    default:
                        await browse.MoveSelectedToMailboxAsync(target.MailboxId.Value).ConfigureAwait(true);
                        break;
                }
            }

            BindLists();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private bool FavoriteUnpinDrop(DragEventArgs e)
    {
        if (_browse is null || e.DataTransfer.TryGetValue(FavoriteDragFormat) is not { } drag)
        {
            return false;
        }

        var target = FindAncestorData<ShellNavItem>(e.Source);
        return MailShellFormatting.ShouldUnpinFavorite(
            _browse.IsFavorite(drag.MailboxId),
            target?.Kind == ShellNavKind.Favorites,
            target is { IsFavoritePin: true } ? target.MailboxId : null);
    }

    private int? FavoriteDropIndex(DragEventArgs e)
    {
        if (_browse is null || !e.DataTransfer.Contains(FavoriteDragFormat))
        {
            return null;
        }

        var drag = e.DataTransfer.TryGetValue(FavoriteDragFormat);
        var target = FindAncestorData<ShellNavItem>(e.Source);
        if (drag is null || target is null)
        {
            return null;
        }

        return MailShellFormatting.FavoriteDestinationIndex(
            _browse.FavoriteMailboxIds,
            drag.MailboxId,
            target.Kind == ShellNavKind.Favorites,
            target.IsFavoritePin ? target.MailboxId : null);
    }

    private ThreadNavDropKind NavDropKind(DragEventArgs e)
    {
        var row = e.DataTransfer.TryGetValue(ThreadDragFormat);
        var target = FindAncestorData<ShellNavItem>(e.Source);
        return row is null || target is null
            ? ThreadNavDropKind.None
            : MailShellFormatting.ThreadNavDrop(row, target);
    }

    private static T? FindAncestorData<T>(object? source)
        where T : class
    {
        for (Visual? current = source as Visual; current is not null; current = current.GetVisualParent())
        {
            if (current is Control { DataContext: T data })
            {
                return data;
            }
        }

        return null;
    }

    private void OnFindInMessageClick(object? sender, RoutedEventArgs e) =>
        ShowFindInMessage(forward: true);

    private void OnFindNextClick(object? sender, RoutedEventArgs e) =>
        ShowFindInMessage(forward: true);

    private void OnFindPreviousClick(object? sender, RoutedEventArgs e) =>
        ShowFindInMessage(forward: false);

    private void ShowFindInMessage(bool forward)
    {
        if (ReadSurface?.IsVisible != true || FindInMessageBar is null || FindInMessageBox is null)
        {
            return;
        }

        FindInMessageBar.IsVisible = true;
        if (string.IsNullOrWhiteSpace(FindInMessageBox.Text))
        {
            var needle = MailShellFormatting.SearchHighlightNeedle(_browse?.SearchQuery);
            if (!string.IsNullOrEmpty(needle))
            {
                FindInMessageBox.Text = needle;
            }

            FindInMessageBox.Focus();
            FindInMessageBox.SelectAll();
            if (_browse is not null)
            {
                BindMessageBody(_browse);
            }

            return;
        }

        _ = FindInConversationAsync(forward);
    }

    private void OnCloseFindInMessageClick(object? sender, RoutedEventArgs e) =>
        HideFindInMessageBar();

    private void OnFindInMessageKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = FindInConversationAsync(forward: e.KeyModifiers != KeyModifiers.Shift);
        }
    }

    private void HideFindInMessageBar()
    {
        if (FindInMessageBar is not null)
        {
            FindInMessageBar.IsVisible = false;
        }

        if (FindInMessageStatus is not null)
        {
            FindInMessageStatus.Text = string.Empty;
        }

        _findHitIndex = -1;
        _findQuery = string.Empty;
        if (_browse is not null)
        {
            BindMessageBody(_browse);
        }
    }

    private async Task FindInConversationAsync(bool forward)
    {
        if (FindInMessageBox is null)
        {
            return;
        }

        var browse = RequireBrowse();
        var query = FindInMessageBox.Text ?? string.Empty;
        var hits = MailShellFormatting.FindInConversation(browse.Conversation, query);
        if (hits.Count == 0)
        {
            _findHitIndex = -1;
            _findQuery = query;
            if (FindInMessageStatus is not null)
            {
                FindInMessageStatus.Text = string.IsNullOrWhiteSpace(query) ? string.Empty : "No matches";
            }

            return;
        }

        if (!string.Equals(query, _findQuery, StringComparison.Ordinal))
        {
            _findQuery = query;
            _findHitIndex = -1;
        }

        _findHitIndex = MailShellFormatting.NextFindIndex(hits.Count, _findHitIndex, forward);
        var hit = hits[_findHitIndex];
        if (FindInMessageStatus is not null)
        {
            FindInMessageStatus.Text = (_findHitIndex + 1) + " of " + hits.Count;
        }

        var card = browse.Conversation[hit.CardIndex];
        await browse.SelectMessageAsync(card.Message.Id).ConfigureAwait(true);
        await browse.RefreshConversationAsync().ConfigureAwait(true);
        BindLists();
        if (hit.InBody)
        {
            var split = MailShellFormatting.SplitQuoted(card.BodyText);
            if ((split.HasQuoted && hit.Offset >= split.Visible.Length)
                || HtmlRemoteContentPolicy.QuoteCollapsedHidesFind(card.BodyHtml, query))
            {
                _quotedExpanded = true;
                _quotedForMessageId = card.Message.Id;
            }

            BindMessageBody(browse);
            if (MessageBodyBox is not null && MessageBodyBox.IsVisible)
            {
                var start = Math.Min(hit.Offset, MessageBodyBox.Text?.Length ?? 0);
                MessageBodyBox.SelectionStart = start;
                MessageBodyBox.SelectionEnd = Math.Min(start + hit.Length, MessageBodyBox.Text?.Length ?? 0);
                MessageBodyBox.Focus();
            }
        }
    }

    private async void OnRevealInMailboxClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await RequireBrowse().RevealInMailboxAsync().ConfigureAwait(true);
            BindLists();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnMoveClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { ItemCount: > 0 })
        {
            return;
        }

        if (IsNavFocused())
        {
            return;
        }

        var browse = RequireBrowse();
        SetStatus(string.Empty);
        if (browse.SelectedMessageId is null && browse.SelectedThreadId is null)
        {
            return;
        }

        try
        {
            var destinations = await browse.ListMoveDestinationsAsync().ConfigureAwait(true);
            var currentMailboxId = browse.SelectedMailboxId
                ?? browse.Messages.FirstOrDefault(m => m.Id == browse.SelectedMessageId)?.MailboxId;
            var dialog = new MoveMailboxDialog(
                destinations,
                currentMailboxId,
                browse.RecentMoveMailboxIds);
            var chosen = await AvaloniaOverlayDialog.ShowAsync(this, dialog, dialog.Completion)
                .ConfigureAwait(true);
            if (chosen is not { } destinationId)
            {
                return;
            }

            await MoveSelectedToDestinationAsync(destinationId).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnRecentMoveClick(object? sender, RoutedEventArgs e)
    {
        if (IsNavFocused())
        {
            return;
        }

        if (sender is not MenuItem { Tag: Guid destinationId })
        {
            return;
        }

        try
        {
            await MoveSelectedToDestinationAsync(destinationId).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async Task MoveSelectedToDestinationAsync(Guid destinationId)
    {
        var browse = RequireBrowse();
        SetStatus(string.Empty);
        if (browse.SelectedMessageId is null && browse.SelectedThreadId is null)
        {
            return;
        }

        await browse.MoveSelectedToMailboxAsync(destinationId).ConfigureAwait(true);
        BindLists();
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { ItemCount: > 0 })
        {
            return;
        }

        if (IsNavFocused())
        {
            return;
        }

        var browse = RequireBrowse();
        SetStatus(string.Empty);
        if (browse.SelectedMessageId is null && browse.SelectedThreadId is null)
        {
            return;
        }

        try
        {
            var destinations = await browse.ListMoveDestinationsAsync().ConfigureAwait(true);
            var currentMailboxId = browse.SelectedMailboxId
                ?? browse.Messages.FirstOrDefault(m => m.Id == browse.SelectedMessageId)?.MailboxId;
            var dialog = new MoveMailboxDialog(
                destinations,
                currentMailboxId,
                browse.RecentMoveMailboxIds,
                "Copy to Mailbox");
            var chosen = await AvaloniaOverlayDialog.ShowAsync(this, dialog, dialog.Completion)
                .ConfigureAwait(true);
            if (chosen is not { } destinationId)
            {
                return;
            }

            await CopySelectedToDestinationAsync(destinationId).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnRecentCopyClick(object? sender, RoutedEventArgs e)
    {
        if (IsNavFocused())
        {
            return;
        }

        if (sender is not MenuItem { Tag: Guid destinationId })
        {
            return;
        }

        try
        {
            await CopySelectedToDestinationAsync(destinationId).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async Task CopySelectedToDestinationAsync(Guid destinationId)
    {
        var browse = RequireBrowse();
        SetStatus(string.Empty);
        if (browse.SelectedMessageId is null && browse.SelectedThreadId is null)
        {
            return;
        }

        await browse.CopySelectedToMailboxAsync(destinationId).ConfigureAwait(true);
        BindLists();
        SetStatus("Copied.");
    }

    private async void OnGoToMailboxClick(object? sender, RoutedEventArgs e)
    {
        var browse = RequireBrowse();
        if (browse.AllMailboxes.Count == 0)
        {
            return;
        }

        try
        {
            var names = browse.Accounts.ToDictionary(account => account.Id, account => account.DisplayName);
            var manyAccounts = browse.Accounts.Count > 1;
            var items = browse.AllMailboxes
                .Select(mailbox => (
                    mailbox.Id,
                    MailShellFormatting.MailboxPickerLabel(
                        mailbox,
                        manyAccounts ? names.GetValueOrDefault(mailbox.AccountId) : null)))
                .ToList();
            var dialog = new MoveMailboxDialog(items, browse.SelectedMailboxId, "Go to Mailbox");
            var chosen = await AvaloniaOverlayDialog.ShowAsync(this, dialog, dialog.Completion)
                .ConfigureAwait(true);
            if (chosen is not { } mailboxId)
            {
                return;
            }

            if (mailboxId == browse.SelectedMailboxId
                && !browse.ShowingUnifiedInbox
                && !browse.ShowingOutbox)
            {
                return;
            }

            await MaybeDiscardBlankDraftAsync().ConfigureAwait(true);
            await browse.GoToMailboxAsync(mailboxId).ConfigureAwait(true);
            ShowReadSurface();
            BindLists();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnGoToUnifiedInboxClick()
    {
        try
        {
            var browse = RequireBrowse();
            if (browse.ShowingUnifiedInbox)
            {
                return;
            }

            await MaybeDiscardBlankDraftAsync().ConfigureAwait(true);
            await browse.GoToUnifiedInboxAsync().ConfigureAwait(true);
            ShowReadSurface();
            BindLists();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnGoToAccountIndexClick(int index)
    {
        try
        {
            var browse = RequireBrowse();
            if (index < 0 || index >= browse.Accounts.Count)
            {
                return;
            }

            var account = browse.Accounts[index];
            var inbox = browse.AllMailboxes.FirstOrDefault(item =>
                item.AccountId == account.Id && item.Role == MailboxRole.Inbox);
            if (inbox is not null
                && browse.SelectedAccountId == account.Id
                && browse.SelectedMailboxId == inbox.Id
                && !browse.ShowingUnifiedInbox
                && !browse.ShowingOutbox)
            {
                return;
            }

            await MaybeDiscardBlankDraftAsync().ConfigureAwait(true);
            await browse.GoToAccountIndexAsync(index).ConfigureAwait(true);
            ShowReadSurface();
            BindLists();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnGoToInboxClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var browse = RequireBrowse();
            if (browse.ShowingUnifiedInbox || browse.IsOnAccountInbox())
            {
                return;
            }

            await MaybeDiscardBlankDraftAsync().ConfigureAwait(true);
            await browse.GoToInboxAsync().ConfigureAwait(true);
            ShowReadSurface();
            BindLists();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnGoToOutboxClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var browse = RequireBrowse();
            if (browse.ShowingOutbox)
            {
                return;
            }

            await MaybeDiscardBlankDraftAsync().ConfigureAwait(true);
            await browse.GoToAccountOutboxAsync().ConfigureAwait(true);
            ShowReadSurface();
            BindLists();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnGoToSentClick(object? sender, RoutedEventArgs e) =>
        await GoToMailboxRoleAsync(MailboxRole.Sent, "No Sent Mailbox.").ConfigureAwait(true);

    private async void OnGoToDraftsClick(object? sender, RoutedEventArgs e) =>
        await GoToMailboxRoleAsync(MailboxRole.Drafts, "No Drafts Mailbox.").ConfigureAwait(true);

    private async void OnGoToTrashClick(object? sender, RoutedEventArgs e) =>
        await GoToMailboxRoleAsync(MailboxRole.Trash, "No Trash Mailbox.").ConfigureAwait(true);

    private async void OnGoToJunkClick(object? sender, RoutedEventArgs e) =>
        await GoToMailboxRoleAsync(MailboxRole.Junk, "No Junk Mailbox.").ConfigureAwait(true);

    private async Task GoToMailboxRoleAsync(MailboxRole role, string missingStatus)
    {
        try
        {
            var browse = RequireBrowse();
            if (browse.IsOnAccountRole(role))
            {
                return;
            }

            if (browse.AccountMailbox(role) is null)
            {
                SetStatus(missingStatus);
                return;
            }

            await MaybeDiscardBlankDraftAsync().ConfigureAwait(true);
            await browse.GoToMailboxRoleAsync(role).ConfigureAwait(true);
            ShowReadSurface();
            BindLists();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnUndoTrashClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var browse = RequireBrowse();
            if (await browse.UndoLastTrashAsync().ConfigureAwait(true))
            {
                SetStatus(browse.LastRelocateUndoStatus ?? "Undone.");
                BindLists();
            }
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (IsNavFocused())
        {
            OnNavFocusedDeleteClick(sender, e);
            return;
        }

        try
        {
            var browse = RequireBrowse();
            if (browse.ShowingOutbox)
            {
                await DiscardSelectedOutboxAsync().ConfigureAwait(true);
                return;
            }

            if (browse.SelectedMessageId is null && browse.SelectedThreadId is null)
            {
                return;
            }

            if (IsTrashScope(browse))
            {
                if (!await ConfirmPermanentDeleteAsync().ConfigureAwait(true))
                {
                    return;
                }

                await browse.PermanentlyDeleteSelectedAsync().ConfigureAwait(true);
                SetStatus("Permanently deleted.");
            }
            else
            {
                await browse.MoveSelectedToTrashAsync().ConfigureAwait(true);
            }

            BindLists();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnPermanentlyDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (IsNavFocused())
        {
            OnNavFocusedDeleteClick(sender, e);
            return;
        }

        try
        {
            var browse = RequireBrowse();
            if (browse.ShowingOutbox)
            {
                await DiscardSelectedOutboxAsync().ConfigureAwait(true);
                return;
            }

            if (browse.SelectedMessageId is null && browse.SelectedThreadId is null)
            {
                return;
            }

            if (!await ConfirmPermanentDeleteAsync().ConfigureAwait(true))
            {
                return;
            }

            await browse.PermanentlyDeleteSelectedAsync().ConfigureAwait(true);
            SetStatus("Permanently deleted.");
            BindLists();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private Task<bool> ConfirmPermanentDeleteAsync() =>
        ConfirmAsync(
            "Permanently delete",
            "Permanently delete this conversation? This cannot be undone.",
            "Delete");

    private async void OnRestoreClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var browse = RequireBrowse();
            if (browse.SelectedMessageId is null && browse.SelectedThreadId is null)
            {
                return;
            }

            await browse.RestoreSelectedFromTrashAsync().ConfigureAwait(true);
            BindLists();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnArchiveClick(object? sender, RoutedEventArgs e)
    {
        if (IsNavFocused())
        {
            await GoToMailboxRoleAsync(MailboxRole.Archive, "No Archive Mailbox.").ConfigureAwait(true);
            return;
        }

        try
        {
            var browse = RequireBrowse();
            if (browse.ShowingOutbox
                || (browse.SelectedMessageId is null && browse.SelectedThreadId is null))
            {
                return;
            }

            await browse.MoveSelectedToArchiveAsync().ConfigureAwait(true);
            BindLists();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnMoveToJunkClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var browse = RequireBrowse();
            if (browse.SelectedMessageId is null && browse.SelectedThreadId is null)
            {
                return;
            }

            await browse.MoveSelectedToJunkAsync().ConfigureAwait(true);
            BindLists();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnNotJunkClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var browse = RequireBrowse();
            if (browse.SelectedMessageId is null && browse.SelectedThreadId is null)
            {
                return;
            }

            await browse.RestoreSelectedFromJunkAsync().ConfigureAwait(true);
            BindLists();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnEmptyTrashClick(object? sender, RoutedEventArgs e)
    {
        var browse = RequireBrowse();
        await EnsureAccountContextAsync().ConfigureAwait(true);
        if (browse.SelectedAccountId is null)
        {
            return;
        }

        if (!await ConfirmAsync(
                "Empty Trash",
                "Permanently delete all Messages in Trash? This cannot be undone.",
                "Empty Trash").ConfigureAwait(true))
        {
            return;
        }

        await browse.EmptyTrashAsync().ConfigureAwait(true);
        BindLists();
    }

    private async void OnEmptyJunkClick(object? sender, RoutedEventArgs e)
    {
        var browse = RequireBrowse();
        await EnsureAccountContextAsync().ConfigureAwait(true);
        if (browse.SelectedAccountId is null)
        {
            return;
        }

        if (!await ConfirmAsync(
                "Empty Junk",
                "Permanently delete all Messages in Junk? This cannot be undone.",
                "Empty Junk").ConfigureAwait(true))
        {
            return;
        }

        await browse.EmptyJunkAsync().ConfigureAwait(true);
        BindLists();
    }

    private async void OnNextInConversationClick(object? sender, RoutedEventArgs e) =>
        await MoveAdjacentConversationAsync(RequireBrowse().SelectedMessageId, forward: true).ConfigureAwait(true);

    private async void OnPreviousInConversationClick(object? sender, RoutedEventArgs e) =>
        await MoveAdjacentConversationAsync(RequireBrowse().SelectedMessageId, forward: false).ConfigureAwait(true);

    private async void OnFirstInConversationClick(object? sender, RoutedEventArgs e) =>
        await MoveAdjacentConversationAsync(selectedMessageId: null, forward: true).ConfigureAwait(true);

    private async void OnLastInConversationClick(object? sender, RoutedEventArgs e) =>
        await MoveAdjacentConversationAsync(selectedMessageId: null, forward: false).ConfigureAwait(true);

    private async Task MoveAdjacentConversationAsync(Guid? selectedMessageId, bool forward)
    {
        var browse = RequireBrowse();
        var next = MailShellFormatting.AdjacentConversationCard(
            browse.Conversation,
            selectedMessageId,
            forward);
        if (next is null)
        {
            return;
        }

        try
        {
            await browse.SelectMessageAsync(next.Message.Id).ConfigureAwait(true);
            await browse.RefreshConversationAsync().ConfigureAwait(true);
            BindLists();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnNextThreadClick(object? sender, RoutedEventArgs e) =>
        await MoveAdjacentThreadAsync(ThreadsList?.SelectedItem as ThreadRow, forward: true).ConfigureAwait(true);

    private async void OnPreviousThreadClick(object? sender, RoutedEventArgs e) =>
        await MoveAdjacentThreadAsync(ThreadsList?.SelectedItem as ThreadRow, forward: false).ConfigureAwait(true);

    private async void OnFirstThreadClick(object? sender, RoutedEventArgs e) =>
        await MoveAdjacentThreadAsync(current: null, forward: true).ConfigureAwait(true);

    private async void OnLastThreadClick(object? sender, RoutedEventArgs e) =>
        await MoveAdjacentThreadAsync(current: null, forward: false).ConfigureAwait(true);

    private async Task MoveAdjacentThreadAsync(ThreadRow? current, bool forward)
    {
        if (ThreadsList?.ItemsSource is not IEnumerable<ThreadRow> source)
        {
            return;
        }

        var rows = source as IReadOnlyList<ThreadRow> ?? source.ToList();
        var next = MailShellFormatting.AdjacentThreadRow(rows, current, forward);
        if (next is null)
        {
            return;
        }

        await OpenListedThreadRowAsync(next).ConfigureAwait(true);
    }

    private async void OnPageThreadClick(bool forward)
    {
        if (ThreadsList?.ItemsSource is not IEnumerable<ThreadRow> source)
        {
            return;
        }

        var rows = source as IReadOnlyList<ThreadRow> ?? source.ToList();
        var next = MailShellFormatting.PagedThreadRow(rows, ThreadsList.SelectedItem as ThreadRow, forward);
        if (next is null)
        {
            return;
        }

        await OpenListedThreadRowAsync(next).ConfigureAwait(true);
    }

    private void OnPageViewportClick(bool down)
    {
        if (ComposeSurface?.IsVisible == true)
        {
            return;
        }

        TryPageReading(down);
    }

    private void OnNextMailboxClick() => MoveAdjacentNav(forward: true);

    private void OnPreviousMailboxClick() => MoveAdjacentNav(forward: false);

    private void MoveAdjacentNav(bool forward)
    {
        if (NavTree is null)
        {
            return;
        }

        var roots = (NavTree.ItemsSource as IEnumerable<ShellNavItem>)?.ToList() ?? [];
        var next = MailShellFormatting.AdjacentNavItem(
            MailShellFormatting.FlattenNav(roots),
            NavTree.SelectedItem as ShellNavItem,
            forward);
        if (next is null)
        {
            return;
        }

        _ = RevealAndSelectNavAsync(next);
    }

    private async Task RevealAndSelectNavAsync(ShellNavItem item)
    {
        if (NavTree is null)
        {
            return;
        }

        var roots = (NavTree.ItemsSource as IEnumerable<ShellNavItem>)?.ToList() ?? [];
        var path = MailShellFormatting.NavPath(roots, item);
        for (var i = 0; i < path.Count - 1; i++)
        {
            path[i].IsExpanded = true;
            try
            {
                await RequireBrowse().SetNavExpandedAsync(path[i], true).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
                return;
            }
        }

        NavTree.SelectedItem = item;
        Dispatcher.UIThread.Post(() => NavTree.ScrollIntoView(item));
    }

    private async void OnNextUnreadClick(object? sender, RoutedEventArgs e)
    {
        var browse = RequireBrowse();
        await browse.SelectNextUnreadAsync().ConfigureAwait(true);
        if (browse.SelectedMessageId is not null)
        {
            await browse.RefreshConversationAsync().ConfigureAwait(true);
        }

        ShowReadSurface();
        BindLists();
    }

    private async void OnPreviousUnreadClick(object? sender, RoutedEventArgs e)
    {
        var browse = RequireBrowse();
        await browse.SelectPreviousUnreadAsync().ConfigureAwait(true);
        if (browse.SelectedMessageId is not null)
        {
            await browse.RefreshConversationAsync().ConfigureAwait(true);
        }

        ShowReadSurface();
        BindLists();
    }

    private async void OnNextFlaggedClick(object? sender, RoutedEventArgs e)
    {
        var browse = RequireBrowse();
        await browse.SelectNextFlaggedAsync().ConfigureAwait(true);
        if (browse.SelectedMessageId is not null)
        {
            await browse.RefreshConversationAsync().ConfigureAwait(true);
        }

        ShowReadSurface();
        BindLists();
    }

    private async void OnPreviousFlaggedClick(object? sender, RoutedEventArgs e)
    {
        var browse = RequireBrowse();
        await browse.SelectPreviousFlaggedAsync().ConfigureAwait(true);
        if (browse.SelectedMessageId is not null)
        {
            await browse.RefreshConversationAsync().ConfigureAwait(true);
        }

        ShowReadSurface();
        BindLists();
    }

    private void OnPageReadingClick(bool down)
    {
        if (ComposeSurface?.IsVisible == true)
        {
            return;
        }

        if (TryPageReading(down))
        {
            return;
        }

        if (down)
        {
            OnNextUnreadClick(this, new RoutedEventArgs());
            return;
        }

        OnPreviousUnreadClick(this, new RoutedEventArgs());
    }

    private bool TryPageReading(bool down)
    {
        if (ReadingScroll is null || ReadSurface?.IsVisible != true)
        {
            return false;
        }

        var offset = ReadingScroll.Offset.Y;
        var extent = ReadingScroll.Extent.Height;
        var viewport = ReadingScroll.Viewport.Height;
        if (!MailShellFormatting.CanPageScroll(offset, extent, viewport, down))
        {
            return false;
        }

        ReadingScroll.Offset = ReadingScroll.Offset.WithY(
            MailShellFormatting.PageScrollOffset(offset, extent, viewport, down));
        return true;
    }

    private async void OnExpandConversationClick(object? sender, RoutedEventArgs e)
    {
        var browse = RequireBrowse();
        if (browse.ShowingOutbox || browse.Conversation.Count <= 1 || browse.ConversationExpanded)
        {
            return;
        }

        await browse.SetConversationExpandedAsync(true).ConfigureAwait(true);
        BindLists();
    }

    private async void OnConversationOrderClick(object? sender, RoutedEventArgs e)
    {
        var browse = RequireBrowse();
        if (browse.ShowingOutbox)
        {
            return;
        }

        browse.ConversationNewestFirst = !browse.ConversationNewestFirst;
        try
        {
            await browse.PersistConversationOrderAsync().ConfigureAwait(true);
            await browse.RefreshConversationAsync().ConfigureAwait(true);
            BindLists();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnCollapseConversationClick(object? sender, RoutedEventArgs e)
    {
        var browse = RequireBrowse();
        if (browse.ShowingOutbox || browse.Conversation.Count <= 1 || !browse.ConversationExpanded)
        {
            return;
        }

        await browse.SetConversationExpandedAsync(false).ConfigureAwait(true);
        BindLists();
    }

    private async void OnRetryBodyClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await RequireBrowse().RetrySelectedBodyAsync().ConfigureAwait(true);
            BindLists();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private void WireAttachmentDrag(Control? control)
    {
        if (control is null)
        {
            return;
        }

        control.AddHandler(PointerPressedEvent, OnAttachmentListPointerPressed);
        control.AddHandler(PointerMovedEvent, OnAttachmentListPointerMoved);
        control.AddHandler(PointerReleasedEvent, OnAttachmentListPointerReleased);
        control.AddHandler(PointerCaptureLostEvent, OnAttachmentListPointerCaptureLost);
    }

    private void OnAttachmentListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (FindAncestorData<AttachmentInfo>(e.Source) is not { } attachment)
        {
            return;
        }

        _pendingAttachmentDrag = e;
        _attachmentDragStart = e.GetPosition(this);
        _attachmentDragItem = attachment;
    }

    private async void OnAttachmentListPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pendingAttachmentDrag is null || _attachmentDragItem is null)
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            ClearPendingAttachmentDrag();
            return;
        }

        var current = e.GetPosition(this);
        if (!MailShellFormatting.ShouldStartDrag(
                current.X - _attachmentDragStart.X,
                current.Y - _attachmentDragStart.Y))
        {
            return;
        }

        var press = _pendingAttachmentDrag;
        var attachment = _attachmentDragItem;
        ClearPendingAttachmentDrag();
        try
        {
            var path = await RequireBrowse().MaterializeAttachmentAsync(attachment.Id).ConfigureAwait(true);
            var top = TopLevel.GetTopLevel(this);
            if (string.IsNullOrWhiteSpace(path) || top is null)
            {
                return;
            }

            var file = await top.StorageProvider.TryGetFileFromPathAsync(path).ConfigureAwait(true);
            if (file is null)
            {
                return;
            }

            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(DataFormat.File, file));
            await DragDrop.DoDragDropAsync(press, transfer, DragDropEffects.Copy).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private void OnAttachmentListPointerReleased(object? sender, PointerReleasedEventArgs e) =>
        ClearPendingAttachmentDrag();

    private void OnAttachmentListPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) =>
        ClearPendingAttachmentDrag();

    private void ClearPendingAttachmentDrag()
    {
        _pendingAttachmentDrag = null;
        _attachmentDragItem = null;
    }

    private async void OnThreadAttachmentDoubleTapped(object? sender, TappedEventArgs e) =>
        await OpenSelectedThreadAttachmentAsync().ConfigureAwait(true);

    private async void OnThreadAttachmentKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await OpenSelectedThreadAttachmentAsync().ConfigureAwait(true);
    }

    private async void OnOpenAttachmentClick(object? sender, RoutedEventArgs e) =>
        await OpenSelectedThreadAttachmentAsync().ConfigureAwait(true);

    private async Task OpenSelectedThreadAttachmentAsync()
    {
        if (ThreadAttachmentsList.SelectedItem is not AttachmentInfo attachment)
        {
            return;
        }

        try
        {
            await RequireBrowse().OpenAttachmentAsync(attachment.Id).ConfigureAwait(true);
            BindLists();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnSaveAllAttachmentsClick(object? sender, RoutedEventArgs e)
    {
        var browse = RequireBrowse();
        var attachments = browse.ThreadAttachments.Count > 0 ? browse.ThreadAttachments : browse.Attachments;
        if (attachments.Count == 0)
        {
            return;
        }

        var top = TopLevel.GetTopLevel(this);
        if (top is null)
        {
            return;
        }

        try
        {
            var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Save attachments",
                AllowMultiple = false,
            }).ConfigureAwait(true);
            var folder = folders.Count > 0 ? folders[0] : null;
            var directory = folder?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            var saved = await browse.SaveAllAttachmentsAsync(directory).ConfigureAwait(true);
            SetStatus(saved == 1 ? "Saved 1 attachment." : $"Saved {saved} attachments.");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnSaveDraftShortcutClick(object? sender, RoutedEventArgs e)
    {
        if (ComposeSurface?.IsVisible != true)
        {
            return;
        }

        try
        {
            if (IsComposeBlank() && RequireCompose().SelectedDraftId is null)
            {
                SetStatus("Nothing to save.");
                return;
            }

            await SaveComposeDraftAsync().ConfigureAwait(true);
            RefreshComposeChrome();
            SetStatus("Draft saved.");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnSaveMessageEmlClick(object? sender, RoutedEventArgs e)
    {
        if (IsNavFocused())
        {
            return;
        }

        var browse = RequireBrowse();
        var top = TopLevel.GetTopLevel(this);
        if (top is null)
        {
            return;
        }

        try
        {
            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save message",
                SuggestedFileName = browse.SelectedMessageEmlFileName(),
                DefaultExtension = "eml",
                FileTypeChoices =
                [
                    new FilePickerFileType("Email message") { Patterns = ["*.eml"] },
                ],
            }).ConfigureAwait(true);
            if (file is null)
            {
                return;
            }

            await using var stream = await file.OpenWriteAsync().ConfigureAwait(true);
            await browse.ExportSelectedMessageAsync(stream).ConfigureAwait(true);
            SetStatus("Saved " + browse.SelectedMessageEmlFileName() + ".");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnSaveAttachmentClick(object? sender, RoutedEventArgs e)
    {
        if (ThreadAttachmentsList.SelectedItem is not AttachmentInfo attachment)
        {
            return;
        }

        var top = TopLevel.GetTopLevel(this);
        if (top is null)
        {
            return;
        }

        try
        {
            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save attachment",
                SuggestedFileName = attachment.FileName,
            }).ConfigureAwait(true);
            if (file is null)
            {
                return;
            }

            await using var stream = await file.OpenWriteAsync().ConfigureAwait(true);
            await RequireBrowse().SaveAttachmentAsync(attachment.Id, stream).ConfigureAwait(true);
            BindLists();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }
    private void ShowComposeSurface()
    {
        HideFindInMessageBar();
        SetPaneVisible(compose: true);
    }

    private void ShowReadSurface() => SetPaneVisible(read: true);

    private void ShowOutboxSurface(OutboxItemInfo item)
    {
        SetPaneVisible(outbox: true);
        OutboxSubjectText.Text = MailShellFormatting.Subject(item.Subject);
        OutboxToText.Text = MailShellFormatting.Recipients("To: ", item.ToAddresses);
        OutboxToText.IsVisible = item.ToAddresses.Count > 0;
        OutboxStateText.Text = item.State.ToString();
        OutboxErrorText.Text = item.ErrorMessage ?? string.Empty;
    }

    private void SetPaneVisible(bool read = false, bool compose = false, bool outbox = false)
    {
        if (ReadSurface is not null)
        {
            ReadSurface.IsVisible = read;
        }

        if (ComposeSurface is not null)
        {
            ComposeSurface.IsVisible = compose;
        }

        if (OutboxSurface is not null)
        {
            OutboxSurface.IsVisible = outbox;
        }

        if (BlankReading is not null)
        {
            BlankReading.IsVisible = !read && !compose && !outbox;
        }
    }

    private void FillComposeFromDraft(DraftInfo draft)
    {
        _suppressSelectionHandlers = true;
        try
        {
            ComposeToBox.Text = string.Join(", ", draft.ToAddresses);
            ComposeCcBox.Text = string.Join(", ", draft.CcAddresses);
            ComposeBccBox.Text = string.Join(", ", draft.BccAddresses);
            ComposeSubjectBox.Text = draft.Subject;
            ComposeBodyBox.Text = draft.BodyText;
            ComposeHtmlBox.Text = draft.BodyHtml ?? string.Empty;
            _composeDirty = false;
            SetCcBccVisible(
                draft.CcAddresses.Count > 0
                || draft.BccAddresses.Count > 0
                || _showCcBccPreferred);
            SetHtmlVisible(!string.IsNullOrWhiteSpace(draft.BodyHtml));
        }
        finally
        {
            _suppressSelectionHandlers = false;
        }

        ShowComposeSurface();
    }

    private void ClearComposeFields()
    {
        _suppressSelectionHandlers = true;
        try
        {
            ComposeToBox.Text = string.Empty;
            ComposeCcBox.Text = string.Empty;
            ComposeBccBox.Text = string.Empty;
            ComposeSubjectBox.Text = string.Empty;
            ComposeBodyBox.Text = string.Empty;
            ComposeHtmlBox.Text = string.Empty;
            _composeDirty = false;
            SetCcBccVisible(_showCcBccPreferred);
            SetHtmlVisible(false);
        }
        finally
        {
            _suppressSelectionHandlers = false;
        }
    }

    private async void OnToggleCcBccClick(object? sender, RoutedEventArgs e)
    {
        await ShowCcBccAsync(focus: ComposeCcBox).ConfigureAwait(true);
    }

    private async void OnFocusCcClick() =>
        await ShowCcBccAsync(focus: ComposeCcBox).ConfigureAwait(true);

    private async void OnFocusBccClick() =>
        await ShowCcBccAsync(focus: ComposeBccBox).ConfigureAwait(true);

    private async Task ShowCcBccAsync(TextBox? focus)
    {
        SetCcBccVisible(true);
        _showCcBccPreferred = true;
        try
        {
            await RequireCompose().RememberShowCcBccAsync(true).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }

        focus?.Focus();
    }

    private void OnToggleHtmlClick(object? sender, RoutedEventArgs e) => SetHtmlVisible(true);

    private void OnFocusHtmlClick()
    {
        SetHtmlVisible(true);
        ComposeHtmlBox?.Focus();
    }

    private void SetHtmlVisible(bool visible)
    {
        if (ComposeHtmlBox is not null)
        {
            ComposeHtmlBox.IsVisible = visible;
            ComposeHtmlBox.IsTabStop = visible;
        }

        if (ShowHtmlButton is not null)
        {
            ShowHtmlButton.IsVisible = !visible;
        }
    }

    private void SetCcBccVisible(bool visible)
    {
        if (ComposeCcBox is not null)
        {
            ComposeCcBox.IsVisible = visible;
            ComposeCcBox.IsTabStop = visible;
        }

        if (ComposeBccBox is not null)
        {
            ComposeBccBox.IsVisible = visible;
            ComposeBccBox.IsTabStop = visible;
        }

        if (ShowCcBccButton is not null)
        {
            ShowCcBccButton.IsVisible = !visible;
        }
    }

    private void RefreshComposeChrome()
    {
        if (ComposeSurface?.IsVisible != true)
        {
            return;
        }

        var compose = RequireCompose();
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        _suppressSelectionHandlers = true;
        try
        {
            if (DraftsCombo is not null)
            {
                DraftsCombo.ItemsSource = compose.Drafts;
                DraftsCombo.SelectedItem = compose.SelectedDraftId is { } selectedDraftId
                    ? compose.Drafts.FirstOrDefault(draft => draft.Id == selectedDraftId)
                    : null;
            }

            if (DraftAttachmentsList is not null)
            {
                DraftAttachmentsList.ItemsSource = compose.DraftAttachments;
            }
        }
        finally
        {
            _suppressSelectionHandlers = false;
            if (focused is not null && !ReferenceEquals(focused, DraftsCombo))
            {
                focused.Focus();
            }
        }
    }

    private async Task ClearSearchAsync(bool focusList)
    {
        _searchTimer.Stop();
        if (MessageSearchBox is not null)
        {
            MessageSearchBox.Text = string.Empty;
        }

        try
        {
            await RequireBrowse().SearchAsync(string.Empty).ConfigureAwait(true);
            BindAfterSearch();
            if (focusList)
            {
                ThreadsList?.Focus();
            }
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private void ScrollSelectedThreadIntoView()
    {
        if (ThreadsList?.SelectedItem is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (ThreadsList?.SelectedItem is { } item)
            {
                ThreadsList.ScrollIntoView(item);
            }
        });
    }

    private void ScrollSelectedConversationIntoView()
    {
        if (ReadSurface?.IsVisible != true)
        {
            return;
        }

        var messageId = RequireBrowse().SelectedMessageId;
        if (messageId == _readingScrollForMessageId)
        {
            return;
        }

        if (SelectedConversationHost is not { IsVisible: true })
        {
            return;
        }

        _readingScrollForMessageId = messageId;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (ReadSurface?.IsVisible != true
                    || ReadingScroll is null
                    || SelectedConversationHost is not { IsVisible: true } host)
                {
                    return;
                }

                var point = host.TranslatePoint(new Point(0, 0), ReadingScroll);
                if (point is { } local)
                {
                    ReadingScroll.Offset = ReadingScroll.Offset.WithY(
                        Math.Max(0, ReadingScroll.Offset.Y + local.Y));
                    return;
                }

                host.BringIntoView();
            },
            DispatcherPriority.Loaded);
    }

    private void BindAfterSearch() =>
        BindLists(rebindCompose: ComposeSurface?.IsVisible != true);

    private void BindLists(bool rebindCompose = true)
    {
        var browse = RequireBrowse();
        var compose = RequireCompose();
        _suppressSelectionHandlers = true;
        try
        {
            var hasAccounts = browse.Accounts.Count > 0;
            EmptyStatePanel.IsVisible = !hasAccounts;
            DailyShell.IsVisible = hasAccounts;
            ShellCommandBar.IsVisible = hasAccounts;

            var previousNav = NavTree.SelectedItem as ShellNavItem;
            var navItems = browse.BuildNavItems();
            NavTree.ItemsSource = navItems;
            NavTree.SelectedItem = MailShellFormatting.FindSelectedNav(
                navItems,
                browse.ShowingUnifiedInbox,
                browse.ShowingOutbox,
                browse.SelectedAccountId,
                browse.SelectedMailboxId,
                previousNav);

            var rows = BuildThreadRows(browse, compose);
            ThreadsList.ItemsSource = rows;
            ThreadsList.SelectedItem = SelectThreadRow(rows, browse);
            ScrollSelectedThreadIntoView();
            ResetReadingScrollIfThreadChanged(browse.SelectedThreadId);
            EmptyListText.IsVisible = hasAccounts && rows.Count == 0 && ComposeSurface?.IsVisible != true;
            EmptyListText.Text = EmptyListCopy(browse);
            ListHeaderText.Text = ListHeader(browse);
            if (MessageSearchBox.Text != browse.SearchQuery)
            {
                MessageSearchBox.Text = browse.SearchQuery;
            }

            var parsedSearch = MessageSearch.Parse(browse.SearchQuery);
            if (UnreadFilterButton is not null)
            {
                UnreadFilterButton.IsVisible = true;
                UnreadFilterButton.Content = browse.ShowingOutbox ? "Failed" : "Unread";
                UnreadFilterButton.FontWeight =
                    (browse.ShowingOutbox ? parsedSearch.FailedOnly : parsedSearch.UnreadOnly)
                        ? Avalonia.Media.FontWeight.SemiBold
                        : Avalonia.Media.FontWeight.Normal;
            }

            if (SortOrderButton is not null)
            {
                SortOrderButton.Content = MailShellFormatting.ListSortLabel(browse.ListSort);
            }

            if (FlaggedFilterButton is not null)
            {
                FlaggedFilterButton.IsVisible = !browse.ShowingOutbox;
                FlaggedFilterButton.FontWeight = parsedSearch.FlaggedOnly
                    ? Avalonia.Media.FontWeight.SemiBold
                    : Avalonia.Media.FontWeight.Normal;
            }

            if (AttachmentFilterButton is not null)
            {
                AttachmentFilterButton.IsVisible = !browse.ShowingOutbox;
                AttachmentFilterButton.FontWeight = parsedSearch.AttachmentOnly
                    ? Avalonia.Media.FontWeight.SemiBold
                    : Avalonia.Media.FontWeight.Normal;
            }

            if (rebindCompose)
            {
                DraftsCombo.ItemsSource = compose.Drafts;
                DraftsCombo.SelectedItem = compose.SelectedDraftId is { } selectedDraftId
                    ? compose.Drafts.FirstOrDefault(d => d.Id == selectedDraftId)
                    : null;
                DraftAttachmentsList.ItemsSource = compose.DraftAttachments;
                var fromAccount = compose.SelectedAccountId is { } composeAccountId
                    ? browse.Accounts.FirstOrDefault(account => account.Id == composeAccountId)
                    : null;
                ComposeAccountText.Text = fromAccount?.EmailAddress ?? string.Empty;
                ComposeAccountText.IsVisible = browse.Accounts.Count <= 1;
                ComposeFromBox.ItemsSource = browse.Accounts;
                ComposeFromBox.SelectedItem = fromAccount;
                ComposeFromBox.IsVisible = browse.Accounts.Count > 1;
            }

            var attachments = browse.ThreadAttachments.Count > 0 ? browse.ThreadAttachments : browse.Attachments;
            ThreadAttachmentsList.ItemsSource = attachments;
            ThreadAttachmentsList.IsVisible = attachments.Count > 0;
            if (SaveAllAttachmentsItem is not null)
            {
                SaveAllAttachmentsItem.IsVisible = attachments.Count > 1;
            }
            AttachmentOpenErrorText.Text = browse.AttachmentOpenError ?? string.Empty;

            var split = MailShellFormatting.SplitConversation(browse.Conversation);
            ConversationBeforeList.ItemsSource = split.Before;
            ConversationBeforeList.IsVisible = split.Before.Count > 0;
            SelectedConversationHost.Content = split.Selected;
            SelectedConversationHost.IsVisible = split.Selected is not null;
            ConversationAfterList.ItemsSource = split.After;
            ConversationAfterList.IsVisible = split.After.Count > 0;
            var subjectSource = MailShellFormatting.ConversationSubjectMessage(browse.Conversation)
                ?? browse.Messages.FirstOrDefault(message => message.Id == browse.SelectedMessageId)
                ?? browse.Messages.FirstOrDefault();
            ThreadSubjectText.Text = subjectSource is null
                ? string.Empty
                : MailShellFormatting.Subject(subjectSource.Subject);
            ThreadParticipantsText.Text = string.Join(
                " · ",
                browse.Conversation.Select(card => card.Sender).Distinct(StringComparer.OrdinalIgnoreCase));
            if (ThreadConversationPosition is not null)
            {
                var position = MailShellFormatting.ConversationPositionLabel(browse.Conversation);
                ThreadConversationPosition.Text = position ?? string.Empty;
                ThreadConversationPosition.IsVisible = position is not null;
            }

            ScrollSelectedConversationIntoView();

            var restoreVisible = IsTrashScope(browse);
            ThreadRestoreItem.IsVisible = restoreVisible;
            ReadRestoreItem.IsVisible = restoreVisible;
            var junkScope = IsJunkScope(browse);
            var showMoveToJunk = !junkScope && !browse.ShowingOutbox;
            ThreadJunkItem.IsVisible = showMoveToJunk;
            ReadJunkItem.IsVisible = showMoveToJunk;
            var showArchive = !IsArchiveScope(browse) && !browse.ShowingOutbox;
            if (ThreadArchiveItem is not null)
            {
                ThreadArchiveItem.IsVisible = showArchive;
            }

            if (ReadArchiveItem is not null)
            {
                ReadArchiveItem.IsVisible = showArchive;
            }
            var revealVisible = browse.ShowingUnifiedInbox && !browse.ShowingOutbox;
            if (ThreadRevealItem is not null)
            {
                ThreadRevealItem.IsVisible = revealVisible;
            }

            if (ReadRevealItem is not null)
            {
                ReadRevealItem.IsVisible = revealVisible;
            }

            var canExpandConversation = !browse.ShowingOutbox && browse.Conversation.Count > 1;
            if (ExpandConversationItem is not null)
            {
                ExpandConversationItem.IsVisible = canExpandConversation && !browse.ConversationExpanded;
            }

            if (CollapseConversationItem is not null)
            {
                CollapseConversationItem.IsVisible = canExpandConversation && browse.ConversationExpanded;
            }

            if (ConversationOrderItem is not null)
            {
                ConversationOrderItem.IsVisible = canExpandConversation;
                ConversationOrderItem.Header = browse.ConversationNewestFirst
                    ? "Oldest on top"
                    : "Newest on top";
            }
            ThreadNotJunkItem.IsVisible = junkScope;
            ReadNotJunkItem.IsVisible = junkScope;
            var flagged = browse.Messages.Any(message => message.IsFlagged)
                || (ThreadsList.SelectedItem is ThreadRow selectedRow && selectedRow.IsFlagged);
            var unread = browse.Messages.Any(message => !message.IsRead)
                || (ThreadsList.SelectedItem is ThreadRow unreadRow && unreadRow.IsUnread);
            var readHeader = unread ? "Mark Read" : "Mark Unread";
            if (ThreadReadItem is not null)
            {
                ThreadReadItem.Header = readHeader;
            }

            if (ReadReadItem is not null)
            {
                ReadReadItem.Header = readHeader;
            }

            var flagHeader = flagged ? "Unflag" : "Flag";
            if (ThreadFlagItem is not null)
            {
                ThreadFlagItem.Header = flagHeader;
            }

            if (ReadFlagItem is not null)
            {
                ReadFlagItem.Header = flagHeader;
            }
            OutboxRetryItem.IsVisible = browse.ShowingOutbox;
            OutboxDiscardItem.IsVisible = browse.ShowingOutbox;

            if (browse.ShowingOutbox)
            {
                if (ThreadsList.SelectedItem is ThreadRow { OutboxItem: { } item })
                {
                    ShowOutboxSurface(item);
                }
                else
                {
                    SetPaneVisible();
                }
            }
            else if (ComposeSurface?.IsVisible == true)
            {
                ShowComposeSurface();
            }
            else if (browse.SelectedMessageId is not null || browse.SelectedThreadId is not null)
            {
                ShowReadSurface();
                BindMessageBody(browse);
            }
            else
            {
                SetPaneVisible();
            }

            BindAuthFailureBanner();
            AccountActionStatus.IsVisible = !string.IsNullOrEmpty(AccountActionStatus.Text);
            BindWindowTitle(browse);
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
        var selected = browse.Conversation.FirstOrDefault(card => card.IsSelected);
        var showSelectedBody = selected is not null || browse.Conversation.Count == 0;
        MessageHtmlView.IsVisible = showHtml;
        BodyUnavailableText.IsVisible = browse.BodyUnavailable && showSelectedBody;
        if (RetryBodyButton is not null)
        {
            RetryBodyButton.IsVisible = browse.BodyUnavailable && showSelectedBody;
        }

        var split = MailShellFormatting.SplitQuoted(browse.BodyUnavailable ? string.Empty : browse.BodyText);
        EnsureQuotedState(browse.SelectedMessageId);
        var showPlain = !showHtml && !browse.BodyUnavailable && showSelectedBody;
        MessageBodyBox.IsVisible = showPlain;
        MessageBodyBox.Text = split.Visible;
        var htmlHasQuoted = showHtml && HtmlRemoteContentPolicy.HasQuotedHtml(html);
        var showQuoted = (showPlain && split.HasQuoted) || htmlHasQuoted;
        if (ShowQuotedButton is not null)
        {
            ShowQuotedButton.IsVisible = showQuoted;
            ShowQuotedButton.Content = _quotedExpanded ? "Hide quoted text" : "Show quoted text";
        }

        if (QuotedBodyBox is not null)
        {
            QuotedBodyBox.Text = split.Quoted;
            QuotedBodyBox.IsVisible = showPlain && split.HasQuoted && _quotedExpanded;
        }

        if (!showHtml)
        {
            return;
        }

        try
        {
            var renderHtml = html!;
            var needle = BodyHighlightNeedle(browse);
            if (!string.IsNullOrWhiteSpace(needle))
            {
                var current = -1;
                if (FindInMessageBar?.IsVisible == true
                    && _findHitIndex >= 0
                    && string.Equals(FindInMessageBox?.Text, needle, StringComparison.Ordinal))
                {
                    current = MailShellFormatting.CurrentBodyFindOccurrence(
                        MailShellFormatting.FindInConversation(browse.Conversation, needle),
                        _findHitIndex);
                }

                renderHtml = HtmlText.Highlight(html, needle, current);
            }

            MessageHtmlView.NavigateToString(
                HtmlRemoteContentPolicy.WrapForOfflineRender(renderHtml, _quotedExpanded));
        }
        catch
        {
            MessageHtmlView.IsVisible = false;
            MessageBodyBox.IsVisible = true;
            MessageBodyBox.Text = split.Visible.Length > 0 ? split.Visible : browse.BodyText ?? string.Empty;
        }
    }

    private string? BodyHighlightNeedle(BrowseShell browse)
    {
        if (FindInMessageBar?.IsVisible == true
            && !string.IsNullOrWhiteSpace(FindInMessageBox?.Text))
        {
            return FindInMessageBox.Text;
        }

        return MailShellFormatting.SearchHighlightNeedle(browse.SearchQuery);
    }

    private void EnsureQuotedState(Guid? messageId)
    {
        if (_quotedForMessageId == messageId)
        {
            return;
        }

        _quotedForMessageId = messageId;
        _quotedExpanded = false;
    }

    private void OnToggleQuotedClick(object? sender, RoutedEventArgs e)
    {
        _quotedExpanded = !_quotedExpanded;
        BindMessageBody(RequireBrowse());
    }
    private static IReadOnlyList<ThreadRow> BuildThreadRows(BrowseShell browse, ComposeOutboxShell compose)
    {
        if (browse.ShowingOutbox)
        {
            return MailShellFormatting.SortThreadRows(
                compose.OutboxItems
                    .Where(item => MailShellFormatting.OutboxItemMatches(item, browse.SearchQuery))
                    .Select(item => new ThreadRow
                    {
                        Id = item.Id,
                        OutboxItem = item,
                        Sender = item.ToAddresses.Count == 0
                            ? item.State.ToString()
                            : MailShellFormatting.ListPrimary(
                                item.State.ToString(),
                                item.ToAddresses,
                                MailboxRole.Sent),
                        FromAddress = item.ToAddresses.Count == 0
                            ? (item.ErrorMessage ?? item.State.ToString())
                            : string.Join(", ", item.ToAddresses),
                        Subject = MailShellFormatting.Subject(item.Subject),
                        Preview = item.ErrorMessage ?? item.State.ToString(),
                        DateLabel = MailShellFormatting.ListRightLabel(
                            browse.ListSort,
                            MailShellFormatting.DateLabel(item.UpdatedAt),
                            0),
                        SortDate = item.UpdatedAt,
                        IsUnread = item.State == OutboxItemState.Failed,
                    })
                    .ToList(),
                browse.ListSort);
        }

        if (!string.IsNullOrWhiteSpace(browse.SearchQuery) && browse.Threads.Count == 0)
        {
            return MailShellFormatting.SortThreadRows(
                browse.Messages.Select(message => ToSearchRow(browse, message)).ToList(),
                browse.ListSort);
        }

        return MailShellFormatting.SortThreadRows(
            browse.Threads.Select(thread => ToThreadRow(browse, thread)).ToList(),
            browse.ListSort);
    }

    private static ThreadRow ToThreadRow(BrowseShell browse, MessageThreadInfo thread)
    {
        var latest = thread.Latest;
        var role = MailboxRoleOf(browse, latest.MailboxId);
        return new ThreadRow
        {
            Id = latest.Id,
            Thread = thread,
            Sender = MailShellFormatting.ListPrimary(
                latest.FromAddress,
                latest.ToAddresses,
                role),
            FromAddress = ListPrimaryAddresses(latest.FromAddress, latest.ToAddresses, role),
            Subject = MailShellFormatting.Subject(latest.Subject),
            Preview = MailShellFormatting.ConversationSnippet(
                (browse.MatchingSearchMessage(thread) ?? latest).Preview,
                MailShellFormatting.SearchHighlightNeedle(browse.SearchQuery)),
            DateLabel = MailShellFormatting.ListRightLabel(
                browse.ListSort,
                MailShellFormatting.DateLabel(latest.ReceivedAt),
                thread.Messages.Max(message => message.SizeBytes)),
            SortDate = latest.ReceivedAt,
            AccountBadge = browse.ShowingUnifiedInbox
                ? browse.Accounts.FirstOrDefault(account => account.Id == latest.AccountId)?.DisplayName
                : null,
            IsUnread = thread.Messages.Any(message => !message.IsRead),
            IsFlagged = thread.Messages.Any(message => message.IsFlagged),
            HasAttachments = thread.Messages.Any(message => message.HasAttachments),
            SizeBytes = thread.Messages.Max(message => message.SizeBytes),
            MessageCount = thread.Messages.Count,
        };
    }

    private static ThreadRow ToSearchRow(BrowseShell browse, MessageInfo message)
    {
        var role = MailboxRoleOf(browse, message.MailboxId);
        return new()
        {
            Id = message.Id,
            SearchHit = message,
            Sender = MailShellFormatting.ListPrimary(
                message.FromAddress,
                message.ToAddresses,
                role),
            FromAddress = ListPrimaryAddresses(message.FromAddress, message.ToAddresses, role),
            Subject = MailShellFormatting.Subject(message.Subject),
            Preview = message.Preview,
            DateLabel = MailShellFormatting.ListRightLabel(
                browse.ListSort,
                MailShellFormatting.DateLabel(message.ReceivedAt),
                message.SizeBytes),
            SortDate = message.ReceivedAt,
            AccountBadge = browse.ShowingUnifiedInbox
                ? browse.Accounts.FirstOrDefault(account => account.Id == message.AccountId)?.DisplayName
                : null,
            IsUnread = !message.IsRead,
            IsFlagged = message.IsFlagged,
            HasAttachments = message.HasAttachments,
            SizeBytes = message.SizeBytes,
            MessageCount = 1,
        };
    }

    private static string ListPrimaryAddresses(
        string fromAddress,
        IReadOnlyList<string> toAddresses,
        MailboxRole? role) =>
        role is (MailboxRole.Sent or MailboxRole.Drafts) && toAddresses.Count > 0
            ? string.Join(", ", toAddresses)
            : fromAddress;

    private static MailboxRole? MailboxRoleOf(BrowseShell browse, Guid mailboxId) =>
        browse.AllMailboxes.FirstOrDefault(mailbox => mailbox.Id == mailboxId)?.Role
        ?? browse.Mailboxes.FirstOrDefault(mailbox => mailbox.Id == mailboxId)?.Role;

    private void ResetReadingScrollIfThreadChanged(Guid? threadId)
    {
        if (threadId == _readingScrollForThreadId)
        {
            return;
        }

        _readingScrollForThreadId = threadId;
        if (ReadingScroll is not null)
        {
            ReadingScroll.Offset = default;
        }
    }

    private static ThreadRow? SelectThreadRow(IReadOnlyList<ThreadRow> rows, BrowseShell browse)
    {
        if (browse.ShowingOutbox)
        {
            return rows.FirstOrDefault(row => !row.IsGroupHeader);
        }

        if (browse.SelectedThreadId is { } threadId)
        {
            return rows.FirstOrDefault(row => row.Id == threadId);
        }

        if (browse.SelectedMessageId is { } messageId)
        {
            return rows.FirstOrDefault(row => row.Id == messageId)
                ?? rows.FirstOrDefault(row => row.Thread?.Messages.Any(message => message.Id == messageId) == true);
        }

        return null;
    }

    private string ListHeader(BrowseShell browse)
    {
        if (!string.IsNullOrWhiteSpace(browse.SearchQuery))
        {
            var query = browse.SearchQuery.Trim();
            if (MailShellFormatting.ChipFilterTitle(query) is { } chipTitle)
            {
                var count = browse.ShowingOutbox
                    ? CountFailed()
                    : browse.Threads.Count > 0 ? browse.Threads.Count : browse.Messages.Count;
                return MailShellFormatting.WithCount(chipTitle, count);
            }

            var hits = browse.ShowingOutbox
                ? _compose?.OutboxItems.Count(item => MailShellFormatting.OutboxItemMatches(item, query)) ?? 0
                : browse.Threads.Count > 0 ? browse.Threads.Count : browse.Messages.Count;
            return MailShellFormatting.WithCount("Search results", hits);
        }

        if (browse.ShowingUnifiedInbox)
        {
            return MailShellFormatting.WithCount(
                "Unified Inbox",
                browse.Accounts.Sum(account => account.UnreadCount));
        }

        if (browse.ShowingOutbox)
        {
            var queued = browse.SelectedAccountId is { } outboxAccountId
                ? browse.OutboxCounts.GetValueOrDefault(outboxAccountId)
                : 0;
            return MailShellFormatting.WithCount("Outbox", queued);
        }

        var mailbox = browse.Mailboxes.FirstOrDefault(item => item.Id == browse.SelectedMailboxId)
            ?? browse.AllMailboxes.FirstOrDefault(item => item.Id == browse.SelectedMailboxId);
        if (mailbox is null)
        {
            return "Mail";
        }

        var title = MailShellFormatting.MailboxNavTitle(mailbox, MailShellFormatting.MailboxPathDelimiter([mailbox]));
        return MailShellFormatting.WithCount(title, mailbox.UnreadCount);
    }

    private int CountFailed() =>
        _compose?.OutboxItems.Count(item => item.State == OutboxItemState.Failed) ?? 0;

    private static string EmptyListCopy(BrowseShell browse) =>
        MailShellFormatting.EmptyListCopy(
            browse.ShowingOutbox,
            browse.ShowingUnifiedInbox,
            browse.SearchQuery,
            browse.SelectedMailboxId is { } mailboxId
                ? browse.AllMailboxes.FirstOrDefault(item => item.Id == mailboxId)?.Role
                    ?? browse.Mailboxes.FirstOrDefault(item => item.Id == mailboxId)?.Role
                : null);

    private static bool IsDraftsScope(BrowseShell browse) =>
        IsRoleScope(browse, MailboxRole.Drafts);

    private static bool IsTrashScope(BrowseShell browse) =>
        IsRoleScope(browse, MailboxRole.Trash);

    private static bool IsJunkScope(BrowseShell browse) =>
        IsRoleScope(browse, MailboxRole.Junk);

    private static bool IsArchiveScope(BrowseShell browse) =>
        IsRoleScope(browse, MailboxRole.Archive);

    private static bool IsRoleScope(BrowseShell browse, MailboxRole role)
    {
        if (browse.SelectedMailboxId is { } mailboxId)
        {
            return browse.AllMailboxes.FirstOrDefault(item => item.Id == mailboxId)?.Role == role;
        }

        var message = browse.Messages.FirstOrDefault(item => item.Id == browse.SelectedMessageId);
        return message is not null
            && browse.AllMailboxes.FirstOrDefault(item => item.Id == message.MailboxId)?.Role == role;
    }

    private AccountInfo? CurrentAccount()
    {
        var browse = RequireBrowse();
        if (_navMenuItem?.AccountId is { } menuAccountId)
        {
            return browse.Accounts.FirstOrDefault(account => account.Id == menuAccountId);
        }

        if (NavTree.SelectedItem is ShellNavItem item && item.AccountId is { } navAccountId)
        {
            return browse.Accounts.FirstOrDefault(account => account.Id == navAccountId);
        }

        if (browse.SelectedAccountId is { } accountId)
        {
            return browse.Accounts.FirstOrDefault(account => account.Id == accountId);
        }

        return null;
    }

    private OutboxItemInfo? SelectedOutboxItem() =>
        ThreadsList.SelectedItem is ThreadRow row ? row.OutboxItem : null;

    private async Task<bool> EnsureComposeAccountAsync()
    {
        var browse = RequireBrowse();
        var compose = RequireCompose();
        var lastFrom = await compose.GetLastFromAsync().ConfigureAwait(true);
        var accountId = browse.SelectedAccountId
            ?? browse.Messages.FirstOrDefault(message => message.Id == browse.SelectedMessageId)?.AccountId
            ?? browse.Threads.FirstOrDefault(thread => thread.Latest.Id == browse.SelectedThreadId)?.Latest.AccountId
            ?? compose.SelectedAccountId
            ?? (lastFrom is { } remembered && browse.Accounts.Any(account => account.Id == remembered)
                ? lastFrom
                : null)
            ?? browse.Accounts.FirstOrDefault()?.Id;
        if (accountId is not { } id)
        {
            return false;
        }

        await compose.SelectAccountAsync(id).ConfigureAwait(true);
        await compose.RememberLastFromAsync(id).ConfigureAwait(true);
        return true;
    }

    private async Task<bool> EnsureAccountContextAsync()
    {
        var browse = RequireBrowse();
        if (_navMenuItem is { AccountId: { } menuAccountId })
        {
            if (browse.SelectedAccountId != menuAccountId)
            {
                await browse.SelectAccountAsync(menuAccountId).ConfigureAwait(true);
            }

            if (_navMenuItem.Kind == ShellNavKind.Mailbox && _navMenuItem.MailboxId is { } mailboxId
                && browse.SelectedMailboxId != mailboxId)
            {
                await browse.SelectMailboxAsync(mailboxId).ConfigureAwait(true);
            }

            return true;
        }

        if (browse.SelectedAccountId is not null)
        {
            return true;
        }

        var account = CurrentAccount();
        if (account is null)
        {
            return false;
        }

        await browse.SelectAccountAsync(account.Id).ConfigureAwait(true);
        return true;
    }

    private async Task MaybeDiscardBlankDraftAsync()
    {
        var compose = RequireCompose();
        if (compose.SelectedDraftId is not { } draftId || !IsComposeBlank())
        {
            if (_composeDirty && ComposeSurface?.IsVisible == true && !IsComposeBlank())
            {
                await SaveComposeDraftAsync().ConfigureAwait(true);
            }

            return;
        }

        await compose.DiscardDraftAsync(draftId).ConfigureAwait(true);
        ClearComposeFields();
    }

    private void ApplyComposeSignature()
    {
        if (ComposeBodyBox is not null)
        {
            ComposeBodyBox.Text = MailSignature.Apply(ComposeBodyBox.Text, CurrentComposeSignature());
        }
    }

    private string? CurrentComposeSignature()
    {
        var accountId = RequireCompose().SelectedAccountId;
        return RequireBrowse().Accounts.FirstOrDefault(account => account.Id == accountId)?.Signature;
    }

    private bool IsComposeBlank()
    {
        var body = MailSignature.Without(ComposeBodyBox.Text, CurrentComposeSignature());
        return string.IsNullOrWhiteSpace(ComposeToBox.Text)
            && string.IsNullOrWhiteSpace(ComposeCcBox.Text)
            && string.IsNullOrWhiteSpace(ComposeBccBox.Text)
            && string.IsNullOrWhiteSpace(ComposeSubjectBox.Text)
            && string.IsNullOrWhiteSpace(body)
            && string.IsNullOrWhiteSpace(ComposeHtmlBox.Text)
            && RequireCompose().DraftAttachments.Count == 0;
    }

    private void BindWindowTitle(BrowseShell browse)
    {
        if (TopLevel.GetTopLevel(this) is not Window window)
        {
            return;
        }

        if (browse.Accounts.Count == 0)
        {
            window.Title = "Mailtide";
            return;
        }

        window.Title = ComposeSurface?.IsVisible == true
            ? MailShellFormatting.ComposeWindowTitle(ComposeSubjectBox?.Text)
            : MailShellFormatting.WindowTitle(ListHeader(browse));
    }

    private void SetStatus(string text)
    {
        AccountActionStatus.Text = text;
        AccountActionStatus.IsVisible = !string.IsNullOrEmpty(text);
    }

    private void OnMessageHtmlNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
    {
        if (HtmlRemoteContentPolicy.IsAllowed(e.Request))
        {
            return;
        }

        e.Cancel = true;
        OpenExternalUri(e.Request);
    }

    private void OnMessageHtmlNewWindowRequested(object? sender, WebViewNewWindowRequestedEventArgs e)
    {
        if (HtmlRemoteContentPolicy.IsAllowed(e.Request))
        {
            return;
        }

        e.Handled = true;
        OpenExternalUri(e.Request);
    }

    private async void OnCopySubjectClick(object? sender, RoutedEventArgs e)
    {
        var subject = ThreadsList.SelectedItem is ThreadRow { IsGroupHeader: false } row
            ? row.Subject
            : ThreadSubjectText?.Text;
        await CopyTextAsync(subject).ConfigureAwait(true);
    }

    private async void OnCopyAddressClick(object? sender, RoutedEventArgs e)
    {
        var address = ThreadsList.SelectedItem is ThreadRow { IsGroupHeader: false } row
            && !string.IsNullOrWhiteSpace(row.FromAddress)
            ? row.FromAddress
            : RequireBrowse().Conversation.FirstOrDefault(card => card.IsSelected)?.Message.FromAddress;
        await CopyTextAsync(address).ConfigureAwait(true);
    }

    private async void OnSearchFromSenderClick(object? sender, RoutedEventArgs e)
    {
        if (RequireBrowse().ShowingOutbox)
        {
            return;
        }

        var fromAddress = sender switch
        {
            Control { DataContext: ConversationCard card } => card.Message.FromAddress,
            Control { DataContext: ThreadRow { IsGroupHeader: false } contextRow } =>
                MailShellFormatting.ThreadSenderAddress(contextRow),
            _ => MailShellFormatting.ThreadSenderAddress(ThreadsList.SelectedItem as ThreadRow)
                ?? CurrentHeaderMessage()?.FromAddress,
        };
        var query = MailShellFormatting.FromSearchQuery(fromAddress);
        if (query.Length == 0)
        {
            return;
        }

        try
        {
            await ApplySearchHistoryAsync(query).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnSearchToRecipientClick(object? sender, RoutedEventArgs e)
    {
        if (RequireBrowse().ShowingOutbox)
        {
            return;
        }

        IReadOnlyList<string> toAddresses = sender is Control { DataContext: ConversationCard card }
            ? card.Message.ToAddresses
            : CurrentHeaderMessage()?.ToAddresses ?? [];
        var query = MailShellFormatting.ToSearchQuery(toAddresses);
        if (query.Length == 0)
        {
            return;
        }

        try
        {
            await ApplySearchHistoryAsync(query).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnShowOriginalClick(object? sender, RoutedEventArgs e)
    {
        if (IsNavFocused())
        {
            return;
        }

        try
        {
            var source = await RequireBrowse().ReadSelectedMessageSourceAsync().ConfigureAwait(true);
            await ShowOriginalAsync(source).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async Task ShowOriginalAsync(string source)
    {
        var tcs = new TaskCompletionSource<bool>();
        var box = new TextBox
        {
            Text = source,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
            FontFamily = new Avalonia.Media.FontFamily("Cascadia Mono, Consolas, Courier New, monospace"),
            FontSize = 12,
            Width = 640,
            Height = 420,
        };
        var copy = new Button { Content = "Copy", MinWidth = 96 };
        var close = new Button { Content = "Close", MinWidth = 96 };
        copy.Click += async (_, _) =>
        {
            await CopyTextAsync(source).ConfigureAwait(true);
        };
        close.Click += (_, _) => tcs.TrySetResult(true);
        void CloseOnEscape(object? _, KeyEventArgs e)
        {
            if (e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None)
            {
                return;
            }

            e.Handled = true;
            tcs.TrySetResult(true);
        }

        box.KeyDown += CloseOnEscape;
        box.AttachedToVisualTree += (_, _) => box.Focus();
        var content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "Original Message",
                    FontSize = 18,
                    FontWeight = Avalonia.Media.FontWeight.SemiBold,
                },
                box,
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { copy, close },
                },
            },
        };
        try
        {
            await AvaloniaOverlayDialog.ShowAsync(this, content, tcs.Task).ConfigureAwait(true);
        }
        catch (InvalidOperationException)
        {
            SetStatus("Unable to open original Message.");
        }
    }

    private async void OnCopyHeadersClick(object? sender, RoutedEventArgs e)
    {
        var message = CurrentHeaderMessage();
        if (message is null)
        {
            return;
        }

        await CopyTextAsync(MailShellFormatting.Headers(message)).ConfigureAwait(true);
    }

    private async void OnCopyMessageIdClick(object? sender, RoutedEventArgs e)
    {
        var message = CurrentHeaderMessage();
        if (message is null)
        {
            return;
        }

        await CopyTextAsync(MailShellFormatting.FormatMessageId(message.InternetMessageId)).ConfigureAwait(true);
    }

    private MessageInfo? CurrentHeaderMessage()
    {
        var browse = RequireBrowse();
        return browse.Conversation.FirstOrDefault(card => card.IsSelected)?.Message
            ?? browse.Messages.FirstOrDefault(item => item.Id == browse.SelectedMessageId)
            ?? browse.Threads.FirstOrDefault(item => item.Latest.Id == browse.SelectedThreadId)?.Latest;
    }

    private async void OnCopySenderAddressClick(object? sender, RoutedEventArgs e)
    {
        var address = (sender as Control)?.DataContext is ConversationCard card
            ? card.Message.FromAddress
            : null;
        await CopyTextAsync(address).ConfigureAwait(true);
    }

    private async Task CopyTextAsync(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            return;
        }

        try
        {
            await clipboard.SetTextAsync(text).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private void OnMessageBodyMenuOpening(object? sender, CancelEventArgs e)
    {
        var selected = MessageBodyBox?.SelectedText;
        if (OpenPlainLinkItem is not null)
        {
            OpenPlainLinkItem.IsEnabled = TryGetPlainLink(MessageBodyBox, out _);
        }

        if (CopyAsQuoteItem is not null)
        {
            CopyAsQuoteItem.IsEnabled = !string.IsNullOrWhiteSpace(selected);
        }
    }

    private void OnQuotedBodyMenuOpening(object? sender, CancelEventArgs e)
    {
        if (sender is ContextMenu menu)
        {
            var enabled = !string.IsNullOrWhiteSpace(QuotedBodyBox?.SelectedText);
            foreach (var obj in menu.Items)
            {
                if (obj is MenuItem { Header: "Copy as quote" } item)
                {
                    item.IsEnabled = enabled;
                }
            }
        }
    }

    private void OnOpenPlainLinkClick(object? sender, RoutedEventArgs e) =>
        TryOpenPlainLinkAtCaret(MessageBodyBox);

    private void OnMessageBodyPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left
            || !e.KeyModifiers.HasFlag(KeyModifiers.Control)
            || sender is not TextBox box)
        {
            return;
        }

        if (TryOpenPlainLinkAtCaret(box))
        {
            e.Handled = true;
        }
    }

    private bool TryOpenPlainLinkAtCaret(TextBox? box)
    {
        if (!TryGetPlainLink(box, out var uri))
        {
            return false;
        }

        OpenExternalUri(uri);
        return true;
    }

    private static bool TryGetPlainLink(TextBox? box, out Uri uri)
    {
        uri = null!;
        if (box is null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(box.SelectedText) && LinkText.TryGetUri(box.SelectedText, out uri))
        {
            return true;
        }

        return LinkText.TryGetUriAt(box.Text, box.CaretIndex, out uri);
    }

    private void OnCopyMessageBodyClick(object? sender, RoutedEventArgs e)
    {
        if (QuotedBodyBox is { IsVisible: true }
            && !string.IsNullOrWhiteSpace(QuotedBodyBox.SelectedText))
        {
            QuotedBodyBox.Copy();
            return;
        }

        MessageBodyBox.Copy();
    }

    private async void OnCopyAsQuoteClick(object? sender, RoutedEventArgs e)
    {
        await CopyTextAsync(MailShellFormatting.AsQuote(SelectedPlainQuote())).ConfigureAwait(true);
    }

    private async void OpenExternalUri(Uri? uri)
    {
        if (uri is null)
        {
            return;
        }

        if (MailAddresses.TryParseMailto(uri, out var mailto))
        {
            try
            {
                await OpenMailtoComposeAsync(mailto).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }

            return;
        }

        if (!HtmlRemoteContentPolicy.IsExternalNavigation(uri))
        {
            return;
        }

        var open = HostBootstrap.OpenExternalUri;
        if (open is null)
        {
            return;
        }

        try
        {
            await open.OpenAsync(uri).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async Task OpenMailtoComposeAsync(MailtoCompose mailto)
    {
        await MaybeDiscardBlankDraftAsync().ConfigureAwait(true);
        if (!await EnsureComposeAccountAsync().ConfigureAwait(true))
        {
            SetStatus("Add an Account before composing.");
            return;
        }

        RequireCompose().StartNewDraft();
        await RequireCompose().LoadRecentAddressesAsync().ConfigureAwait(true);
        ClearComposeFields();
        ComposeToBox.Text = mailto.To;
        ComposeCcBox.Text = mailto.Cc;
        ComposeBccBox.Text = mailto.Bcc;
        ComposeSubjectBox.Text = mailto.Subject;
        ComposeBodyBox.Text = mailto.Body;
        ApplyComposeSignature();
        SetCcBccVisible(!string.IsNullOrWhiteSpace(mailto.Cc) || !string.IsNullOrWhiteSpace(mailto.Bcc));
        _composeDirty = !IsComposeBlank();
        ShowComposeSurface();
        BindLists();
        FocusComposeAfterStart();
    }

    private async Task<bool> ConfirmAsync(string title, string body, string confirmLabel)
    {
        var tcs = new TaskCompletionSource<bool>();
        var cancel = new Button { Content = "Cancel", Width = 96 };
        var confirm = new Button { Content = confirmLabel, MinWidth = 96 };
        cancel.Click += (_, _) => tcs.TrySetResult(false);
        confirm.Click += (_, _) => tcs.TrySetResult(true);
        var content = new StackPanel
        {
            Width = 388,
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = title, FontSize = 18, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                new TextBlock { Text = body, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { cancel, confirm },
                },
            },
        };
        try
        {
            return await AvaloniaOverlayDialog.ShowAsync(this, content, tcs.Task).ConfigureAwait(true);
        }
        catch (InvalidOperationException)
        {
            SetStatus("Unable to open confirmation dialog.");
            return false;
        }
    }

    private BrowseShell RequireBrowse() =>
        _browse ?? throw new InvalidOperationException("BrowseShell was not attached to MailShellView.");

    private sealed record FavoriteDrag(Guid MailboxId);

    private ComposeOutboxShell RequireCompose() =>
        _compose ?? throw new InvalidOperationException("ComposeOutboxShell was not attached to MailShellView.");
}
