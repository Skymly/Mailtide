using Mailtide.Core;

namespace Mailtide.UI;

/// <summary>
/// UI-framework-agnostic browse surface. Issues Core queries and Host ports for open/confirm.
/// </summary>
public sealed class BrowseShell
{
    private readonly MailtideApp _app;

    private readonly HashSet<Guid> _dismissedAuthenticationFailureAccountIds = [];

    public BrowseShell(MailtideApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        _app = app;
    }

    /// <summary>UI-provided confirmation gate for Remove Account (not a Host port).</summary>
    public IConfirmAccountRemoval? AccountRemovalConfirmation { get; set; }

    public IReadOnlyList<AccountInfo> Accounts { get; private set; } = [];

    public IReadOnlyList<AccountStatusRow> AccountStatuses { get; private set; } = [];

    public IReadOnlyList<MailboxInfo> Mailboxes { get; private set; } = [];

    public IReadOnlyList<MailboxInfo> AllMailboxes { get; private set; } = [];

    public IReadOnlyList<MessageInfo> Messages { get; private set; } = [];

    public IReadOnlyList<MessageThreadInfo> Threads { get; private set; } = [];

    public IReadOnlyList<AttachmentInfo> Attachments { get; private set; } = [];

    public IReadOnlyList<AttachmentInfo> ThreadAttachments { get; private set; } = [];

    public IReadOnlyDictionary<Guid, int> OutboxCounts { get; private set; } =
        new Dictionary<Guid, int>();

    public IReadOnlyDictionary<Guid, int> OutboxFailedCounts { get; private set; } =
        new Dictionary<Guid, int>();

    public Guid? SelectedAccountId { get; private set; }

    public Guid? SelectedMailboxId { get; private set; }

    public Guid? SelectedThreadId { get; private set; }

    public Guid? SelectedMessageId { get; private set; }

    public bool ShowingUnifiedInbox { get; private set; }

    public bool ShowingOutbox { get; private set; }

    public const string ListNewestFirstPreferenceKey = "browse.listNewestFirst";
    public const string ConversationNewestFirstPreferenceKey = "browse.conversationNewestFirst";

    public const string NavPreferenceKey = "browse.nav";

    public const string LastThreadsPreferenceKey = "browse.lastThreads";

    public const string RecentMovePreferenceKey = "browse.recentMove";

    public const string CollapsedNavPreferenceKey = "browse.navCollapsed";

    public const string FavoritesPreferenceKey = "browse.favorites";

    public const string RecentSearchPreferenceKey = "browse.recentSearch";

    public Task<string?> GetPreferenceAsync(string key, CancellationToken cancellationToken = default) =>
        _app.GetPreferenceAsync(key, cancellationToken);

    public Task SetPreferenceAsync(string key, string value, CancellationToken cancellationToken = default) =>
        _app.SetPreferenceAsync(key, value, cancellationToken);

    public ThreadListSort ListSort { get; set; } = ThreadListSort.Newest;

    public bool ConversationNewestFirst { get; set; }

    public bool ListNewestFirst
    {
        get => ListSort != ThreadListSort.Oldest;
        set => ListSort = value ? ThreadListSort.Newest : ThreadListSort.Oldest;
    }

    public string SearchQuery { get; private set; } = string.Empty;

    public string? BodyText { get; private set; }

    public string? BodyHtml { get; private set; }

    public bool BodyUnavailable { get; private set; }

    public IReadOnlyList<string> SelectedToAddresses { get; private set; } = [];

    public IReadOnlyList<string> SelectedCcAddresses { get; private set; } = [];

    public string? AttachmentOpenError { get; private set; }

    public AuthenticationFailurePrompt? AuthenticationFailurePrompt { get; private set; }

    public async Task LoadAccountsAsync(CancellationToken cancellationToken = default)
    {
        Accounts = await _app.ListAccountsAsync(cancellationToken).ConfigureAwait(false);
        AccountStatuses = Accounts
            .Select(account => new AccountStatusRow(account, _app.GetAccountStatus(account.Id)))
            .ToList();
        await RefreshAllMailboxesAsync(cancellationToken).ConfigureAwait(false);
        await RefreshOutboxCountsAsync(cancellationToken).ConfigureAwait(false);
        await EnsureRecentMovesAsync(cancellationToken).ConfigureAwait(false);
        await EnsureCollapsedNavAsync(cancellationToken).ConfigureAwait(false);
        await EnsureFavoritesAsync(cancellationToken).ConfigureAwait(false);
        await EnsureRecentSearchesAsync(cancellationToken).ConfigureAwait(false);
        RefreshAuthenticationFailurePrompt();
    }

    public void DismissAuthenticationFailurePrompt()
    {
        if (AuthenticationFailurePrompt is not { } prompt)
        {
            return;
        }

        _dismissedAuthenticationFailureAccountIds.Add(prompt.AccountId);
        AuthenticationFailurePrompt = null;
    }

    public Task<AccountInfo> AddGoogleAccountAsync(
        string displayName,
        CancellationToken cancellationToken = default) =>
        AddThenReloadAsync(
            ct => _app.AddGoogleAccountAsync(displayName, ct),
            cancellationToken);

    public Task<AccountInfo> AddMicrosoftConsumerAccountAsync(
        string displayName,
        CancellationToken cancellationToken = default) =>
        AddThenReloadAsync(
            ct => _app.AddMicrosoftConsumerAccountAsync(displayName, ct),
            cancellationToken);

    public Task<AccountInfo> AddQqMailAccountAsync(
        QqMailAccountDraft draft,
        CancellationToken cancellationToken = default) =>
        AddThenReloadAsync(
            ct => _app.AddQqMailAccountAsync(draft, ct),
            cancellationToken);

    public Task<AccountInfo> AddManualAccountAsync(
        ManualAccountDraft draft,
        CancellationToken cancellationToken = default) =>
        AddThenReloadAsync(
            ct => _app.AddManualAccountAsync(draft, ct),
            cancellationToken);

    public async Task<AccountInfo?> UpdateManualAccountAsync(
        Guid accountId,
        ManualAccountDraft draft,
        CancellationToken cancellationToken = default)
    {
        var updated = await _app
            .UpdateManualAccountAsync(accountId, draft, cancellationToken)
            .ConfigureAwait(false);
        await LoadAccountsAsync(cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task<AccountInfo?> SetAccountSignatureAsync(
        Guid accountId,
        string? signature,
        CancellationToken cancellationToken = default)
    {
        var updated = await _app
            .SetAccountSignatureAsync(accountId, signature, cancellationToken)
            .ConfigureAwait(false);
        await LoadAccountsAsync(cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task<AccountInfo?> UpdateQqMailAccountAsync(
        Guid accountId,
        QqMailAccountDraft draft,
        CancellationToken cancellationToken = default)
    {
        var updated = await _app
            .UpdateQqMailAccountAsync(accountId, draft, cancellationToken)
            .ConfigureAwait(false);
        await LoadAccountsAsync(cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task<AccountInfo?> ReauthorizeAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var updated = await _app
            .ReauthorizeAccountAsync(accountId, cancellationToken)
            .ConfigureAwait(false);
        await LoadAccountsAsync(cancellationToken).ConfigureAwait(false);
        return updated;
    }

    private async Task<AccountInfo> AddThenReloadAsync(
        Func<CancellationToken, Task<AccountInfo>> add,
        CancellationToken cancellationToken)
    {
        var account = await add(cancellationToken).ConfigureAwait(false);
        await LoadAccountsAsync(cancellationToken).ConfigureAwait(false);
        return account;
    }

    public async Task<bool> RemoveAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var confirm = AccountRemovalConfirmation
            ?? throw new InvalidOperationException(
                "BrowseShell.AccountRemovalConfirmation was not set by the UI.");

        var account = Accounts.FirstOrDefault(a => a.Id == accountId)
            ?? (await _app.ListAccountsAsync(cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(a => a.Id == accountId);
        var displayName = account?.DisplayName ?? accountId.ToString("D");

        if (!await confirm.ConfirmAsync(displayName, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        await _app.RemoveAccountAsync(accountId, cancellationToken).ConfigureAwait(false);

        if (SelectedAccountId == accountId)
        {
            SelectedAccountId = null;
            SelectedMailboxId = null;
            Mailboxes = [];
            Messages = [];
            ClearMessageDetail();
        }

        await LoadAccountsAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public AccountStatus GetAccountStatus(Guid accountId) => _app.GetAccountStatus(accountId);

    public async Task SelectAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        SelectedAccountId = accountId;
        SelectedMailboxId = null;
        ShowingUnifiedInbox = false;
        ShowingOutbox = false;
        if (!MessageSearch.IsChipFilterOnly(SearchQuery))
        {
            SearchQuery = string.Empty;
        }

        Messages = [];
        Threads = [];
        SelectedThreadId = null;
        ClearMessageDetail();
        Mailboxes = await _app.ListMailboxesAsync(accountId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MailboxInfo> CreateMailboxAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        if (SelectedAccountId is not { } accountId)
        {
            throw new InvalidOperationException("Select an Account before creating a Mailbox.");
        }

        var created = await _app
            .CreateMailboxAsync(accountId, name, cancellationToken)
            .ConfigureAwait(false);
        Mailboxes = await _app.ListMailboxesAsync(accountId, cancellationToken).ConfigureAwait(false);
        await RefreshAllMailboxesAsync(cancellationToken).ConfigureAwait(false);
        return created;
    }

    public async Task<MailboxInfo> RenameMailboxAsync(
        string newName,
        CancellationToken cancellationToken = default)
    {
        if (SelectedAccountId is not { } accountId)
        {
            throw new InvalidOperationException("Select an Account before renaming a Mailbox.");
        }

        if (SelectedMailboxId is not { } mailboxId)
        {
            throw new InvalidOperationException("Select a Mailbox before renaming it.");
        }

        var renamed = await _app
            .RenameMailboxAsync(accountId, mailboxId, newName, cancellationToken)
            .ConfigureAwait(false);
        Mailboxes = await _app.ListMailboxesAsync(accountId, cancellationToken).ConfigureAwait(false);
        await RefreshAllMailboxesAsync(cancellationToken).ConfigureAwait(false);
        return renamed;
    }

    public async Task DeleteMailboxAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedAccountId is not { } accountId)
        {
            throw new InvalidOperationException("Select an Account before deleting a Mailbox.");
        }

        if (SelectedMailboxId is not { } mailboxId)
        {
            throw new InvalidOperationException("Select a Mailbox before deleting it.");
        }

        await _app
            .DeleteMailboxAsync(accountId, mailboxId, cancellationToken)
            .ConfigureAwait(false);
        SelectedMailboxId = null;
        SelectedThreadId = null;
        Messages = [];
        Threads = [];
        ClearMessageDetail();
        Mailboxes = await _app.ListMailboxesAsync(accountId, cancellationToken).ConfigureAwait(false);
        await RefreshAllMailboxesAsync(cancellationToken).ConfigureAwait(false);
        await UnpinFavoriteAsync(mailboxId, cancellationToken).ConfigureAwait(false);
    }

    public async Task RestoreBrowseAsync(CancellationToken cancellationToken = default)
    {
        var newest = await _app
            .GetPreferenceAsync(ListNewestFirstPreferenceKey, cancellationToken)
            .ConfigureAwait(false);
        ListSort = MailShellFormatting.ParseListSort(newest);
        var conversationOrder = await _app
            .GetPreferenceAsync(ConversationNewestFirstPreferenceKey, cancellationToken)
            .ConfigureAwait(false);
        ConversationNewestFirst = conversationOrder == "1";

        var lastThreads = await _app
            .GetPreferenceAsync(LastThreadsPreferenceKey, cancellationToken)
            .ConfigureAwait(false);
        _lastThreadByNav = DecodeLastThreads(lastThreads);
        await EnsureRecentMovesAsync(cancellationToken).ConfigureAwait(false);
        await EnsureCollapsedNavAsync(cancellationToken).ConfigureAwait(false);
        await EnsureFavoritesAsync(cancellationToken).ConfigureAwait(false);
        await EnsureRecentSearchesAsync(cancellationToken).ConfigureAwait(false);

        if (Accounts.Count == 0)
        {
            return;
        }

        var nav = await _app.GetPreferenceAsync(NavPreferenceKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(nav) || nav == "unified")
        {
            await ShowUnifiedInboxAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (nav.StartsWith("outbox:", StringComparison.Ordinal)
            && Guid.TryParse(nav["outbox:".Length..], out var outboxAccountId)
            && Accounts.Any(account => account.Id == outboxAccountId))
        {
            await ShowOutboxAsync(outboxAccountId, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (nav.StartsWith("mailbox:", StringComparison.Ordinal)
            && Guid.TryParse(nav["mailbox:".Length..], out var mailboxId))
        {
            var mailbox = AllMailboxes.FirstOrDefault(item => item.Id == mailboxId)
                ?? Mailboxes.FirstOrDefault(item => item.Id == mailboxId);
            if (mailbox is not null)
            {
                if (SelectedAccountId != mailbox.AccountId)
                {
                    await SelectAccountAsync(mailbox.AccountId, cancellationToken).ConfigureAwait(false);
                }

                await SelectMailboxAsync(mailbox.Id, cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        await ShowUnifiedInboxAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task PersistListSortAsync(CancellationToken cancellationToken = default) =>
        _app.SetPreferenceAsync(
            ListNewestFirstPreferenceKey,
            MailShellFormatting.EncodeListSort(ListSort),
            cancellationToken);

    public Task PersistConversationOrderAsync(CancellationToken cancellationToken = default) =>
        _app.SetPreferenceAsync(
            ConversationNewestFirstPreferenceKey,
            ConversationNewestFirst ? "1" : "0",
            cancellationToken);

    private Task PersistNavAsync(CancellationToken cancellationToken)
    {
        var value = ShowingUnifiedInbox
            ? "unified"
            : ShowingOutbox && SelectedAccountId is { } outboxAccountId
                ? "outbox:" + outboxAccountId.ToString("D")
                : SelectedMailboxId is { } mailboxId
                    ? "mailbox:" + mailboxId.ToString("D")
                    : "unified";
        return _app.SetPreferenceAsync(NavPreferenceKey, value, cancellationToken);
    }

    public Task SelectMailboxAsync(Guid mailboxId, CancellationToken cancellationToken = default) =>
        SelectMailboxAsync(mailboxId, restoreThread: true, cancellationToken);

    public async Task SelectMailboxAsync(
        Guid mailboxId,
        bool restoreThread,
        CancellationToken cancellationToken = default)
    {
        if (SelectedAccountId is not { } accountId)
        {
            throw new InvalidOperationException("Select an Account before selecting a Mailbox.");
        }

        SelectedMailboxId = mailboxId;
        ShowingUnifiedInbox = false;
        ShowingOutbox = false;
        var chipFilter = MessageSearch.KeepChipFilter(SearchQuery, outbox: false);
        SearchQuery = string.Empty;
        SelectedThreadId = null;
        ClearMessageDetail();
        await ReloadMailboxListingAsync(accountId, mailboxId, cancellationToken).ConfigureAwait(false);
        await PersistNavAsync(cancellationToken).ConfigureAwait(false);
        if (restoreThread && chipFilter is null)
        {
            await RestoreLastThreadAsync(cancellationToken).ConfigureAwait(false);
        }

        await ReapplyChipFilterAsync(chipFilter, cancellationToken).ConfigureAwait(false);
    }

    public async Task GoToMailboxAsync(Guid mailboxId, CancellationToken cancellationToken = default)
    {
        var mailbox = AllMailboxes.FirstOrDefault(item => item.Id == mailboxId)
            ?? Mailboxes.FirstOrDefault(item => item.Id == mailboxId)
            ?? throw new InvalidOperationException("Mailbox is not available.");
        if (SelectedMailboxId == mailbox.Id
            && !ShowingUnifiedInbox
            && !ShowingOutbox
            && SelectedAccountId == mailbox.AccountId)
        {
            return;
        }

        if (SelectedAccountId != mailbox.AccountId)
        {
            await SelectAccountAsync(mailbox.AccountId, cancellationToken).ConfigureAwait(false);
        }

        await SelectMailboxAsync(mailbox.Id, cancellationToken).ConfigureAwait(false);
    }

    public bool IsOnAccountInbox() => IsOnAccountRole(MailboxRole.Inbox);

    public bool IsOnAccountRole(MailboxRole role)
    {
        if (ShowingUnifiedInbox || ShowingOutbox || SelectedMailboxId is not { } mailboxId)
        {
            return false;
        }

        return AccountMailbox(role)?.Id == mailboxId;
    }

    public MailboxInfo? AccountMailbox(MailboxRole role)
    {
        var accountId = SelectedAccountId ?? CurrentAccountId();
        if (accountId is null)
        {
            return null;
        }

        return AllMailboxes.FirstOrDefault(item =>
                item.AccountId == accountId && item.Role == role)
            ?? Mailboxes.FirstOrDefault(item => item.Role == role);
    }

    public async Task GoToAccountIndexAsync(int index, CancellationToken cancellationToken = default)
    {
        if (index < 0 || index >= Accounts.Count)
        {
            return;
        }

        var account = Accounts[index];
        var inbox = AllMailboxes.FirstOrDefault(item =>
                item.AccountId == account.Id && item.Role == MailboxRole.Inbox)
            ?? (SelectedAccountId == account.Id
                ? Mailboxes.FirstOrDefault(item => item.Role == MailboxRole.Inbox)
                : null);
        if (inbox is null)
        {
            return;
        }

        if (SelectedAccountId == account.Id
            && SelectedMailboxId == inbox.Id
            && !ShowingUnifiedInbox
            && !ShowingOutbox)
        {
            return;
        }

        if (SelectedAccountId != account.Id)
        {
            await SelectAccountAsync(account.Id, cancellationToken).ConfigureAwait(false);
        }

        await SelectMailboxAsync(inbox.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task GoToUnifiedInboxAsync(CancellationToken cancellationToken = default)
    {
        if (ShowingUnifiedInbox)
        {
            return;
        }

        await ShowUnifiedInboxAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task GoToInboxAsync(CancellationToken cancellationToken = default)
    {
        if (ShowingUnifiedInbox)
        {
            return;
        }

        var accountId = CurrentAccountId();
        if (accountId is null)
        {
            await ShowUnifiedInboxAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var inbox = AllMailboxes.FirstOrDefault(item =>
                item.AccountId == accountId && item.Role == MailboxRole.Inbox)
            ?? Mailboxes.FirstOrDefault(item => item.Role == MailboxRole.Inbox);
        if (inbox is null)
        {
            await ShowUnifiedInboxAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (SelectedMailboxId == inbox.Id)
        {
            return;
        }

        if (SelectedAccountId != inbox.AccountId)
        {
            await SelectAccountAsync(inbox.AccountId, cancellationToken).ConfigureAwait(false);
        }

        await SelectMailboxAsync(inbox.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task GoToAccountOutboxAsync(CancellationToken cancellationToken = default)
    {
        var accountId = CurrentAccountId();
        if (accountId is null || (ShowingOutbox && SelectedAccountId == accountId))
        {
            return;
        }

        await ShowOutboxAsync(accountId.Value, cancellationToken).ConfigureAwait(false);
    }

    public async Task GoToMailboxRoleAsync(
        MailboxRole role,
        CancellationToken cancellationToken = default)
    {
        var mailbox = AccountMailbox(role);
        if (mailbox is null || IsOnAccountRole(role))
        {
            return;
        }

        if (SelectedAccountId != mailbox.AccountId)
        {
            await SelectAccountAsync(mailbox.AccountId, cancellationToken).ConfigureAwait(false);
        }

        await SelectMailboxAsync(mailbox.Id, cancellationToken).ConfigureAwait(false);
    }

    private Guid? CurrentAccountId() =>
        SelectedAccountId
        ?? CurrentThread()?.Latest.AccountId
        ?? FindLoadedMessage(SelectedMessageId)?.AccountId
        ?? Accounts.FirstOrDefault()?.Id;

    public async Task SelectThreadAsync(Guid latestMessageId, CancellationToken cancellationToken = default)
    {
        var thread = Threads.FirstOrDefault(item => item.Latest.Id == latestMessageId)
            ?? throw new InvalidOperationException("Thread is not in the current list.");
        if (SelectedThreadId != thread.Latest.Id)
        {
            ConversationExpanded = MailShellFormatting.ThreadWasExpanded(
                thread.Messages.Select(message => message.Id),
                _expandedConversationIds);
        }

        SelectedThreadId = thread.Latest.Id;
        SelectedMessageId = null;
        ClearMessageDetail();
        Messages = thread.Messages;
        await MarkListedMessagesReadAsync(cancellationToken).ConfigureAwait(false);
        await LoadThreadAttachmentsAsync(cancellationToken).ConfigureAwait(false);
        await RememberSelectedThreadAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ShowUnifiedInboxAsync(CancellationToken cancellationToken = default)
    {
        SelectedAccountId = null;
        SelectedMailboxId = null;
        SelectedThreadId = null;
        ShowingUnifiedInbox = true;
        ShowingOutbox = false;
        var chipFilter = MessageSearch.KeepChipFilter(SearchQuery, outbox: false);
        SearchQuery = string.Empty;
        Mailboxes = [];
        ClearMessageDetail();
        await ReloadUnifiedInboxListingAsync(cancellationToken).ConfigureAwait(false);
        await PersistNavAsync(cancellationToken).ConfigureAwait(false);
        if (chipFilter is null)
        {
            await RestoreLastThreadAsync(cancellationToken).ConfigureAwait(false);
        }

        await ReapplyChipFilterAsync(chipFilter, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReapplyChipFilterAsync(string? filter, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(filter))
        {
            return;
        }

        await SearchAsync(filter, cancellationToken).ConfigureAwait(false);
    }

    public async Task SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        query ??= string.Empty;
        if (ShowingOutbox)
        {
            SearchQuery = query;
            return;
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            SearchQuery = string.Empty;
            _conversationMembers = null;
            _searchHitIds = null;
            Conversation = [];
            var restoreId = _searchReturnThreadId ?? SelectedThreadId;
            _searchReturnThreadId = null;
            SelectedThreadId = null;
            if (ShowingUnifiedInbox)
            {
                await ReloadUnifiedInboxListingAsync(cancellationToken).ConfigureAwait(false);
            }
            else if (SelectedAccountId is { } accountId && SelectedMailboxId is { } mailboxId)
            {
                await ReloadMailboxListingAsync(accountId, mailboxId, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                Messages = [];
                Threads = [];
            }

            if (restoreId is { } id)
            {
                var thread = Threads.FirstOrDefault(item => item.Latest.Id == id)
                    ?? Threads.FirstOrDefault(item => item.Messages.Any(message => message.Id == id));
                if (thread is not null)
                {
                    await SelectThreadAsync(thread.Latest.Id, cancellationToken).ConfigureAwait(false);
                    await SelectMessageAsync(thread.Latest.Id, cancellationToken).ConfigureAwait(false);
                    await RefreshConversationAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            return;
        }

        if (SelectedThreadId is { } currentThreadId)
        {
            _searchReturnThreadId = currentThreadId;
        }

        SearchQuery = query;
        SelectedThreadId = null;
        _conversationMembers = null;
        Conversation = [];
        if (ShowingUnifiedInbox)
        {
            var unifiedHits = await _app
                .SearchUnifiedInboxAsync(SearchQuery, cancellationToken)
                .ConfigureAwait(false);
            await ApplySearchHitsAsync(unifiedHits, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (SelectedAccountId is not { } searchAccountId || SelectedMailboxId is not { } searchMailboxId)
        {
            Messages = [];
            Threads = [];
            _searchHitIds = null;
            return;
        }

        var mailboxHits = await _app
            .SearchMessagesAsync(searchAccountId, searchMailboxId, SearchQuery, cancellationToken)
            .ConfigureAwait(false);
        await ApplySearchHitsAsync(mailboxHits, cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplySearchHitsAsync(
        IReadOnlyList<MessageInfo> hits,
        CancellationToken cancellationToken)
    {
        Messages = hits;
        _searchHitIds = hits.Select(item => item.Id).ToHashSet();
        if (hits.Count == 0)
        {
            Threads = [];
            return;
        }

        var hitIds = _searchHitIds;
        var threads = new List<MessageThreadInfo>();
        var seen = new HashSet<Guid>();
        foreach (var group in hits.GroupBy(item => (item.AccountId, item.MailboxId)))
        {
            var listed = await _app
                .ListMailboxThreadsAsync(group.Key.AccountId, group.Key.MailboxId, cancellationToken)
                .ConfigureAwait(false);
            foreach (var thread in listed)
            {
                if (thread.Messages.Any(message => hitIds.Contains(message.Id))
                    && seen.Add(thread.Latest.Id))
                {
                    threads.Add(OverlaySearchPreviews(thread, hits));
                }
            }
        }

        Threads = threads
            .OrderByDescending(thread => thread.Latest.ReceivedAt)
            .ThenBy(thread => thread.Latest.Subject, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static MessageThreadInfo OverlaySearchPreviews(
        MessageThreadInfo thread,
        IReadOnlyList<MessageInfo> hits)
    {
        var previews = hits
            .Where(item => !string.IsNullOrEmpty(item.Preview))
            .GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.First().Preview);
        if (previews.Count == 0)
        {
            return thread;
        }

        var messages = thread.Messages
            .Select(item => previews.TryGetValue(item.Id, out var preview)
                ? item with { Preview = preview }
                : item)
            .ToList();
        var latest = messages.FirstOrDefault(item => item.Id == thread.Latest.Id) ?? thread.Latest;
        return new MessageThreadInfo(latest, messages);
    }

    public MessageInfo? MatchingSearchMessage(MessageThreadInfo thread)
    {
        ArgumentNullException.ThrowIfNull(thread);
        if (_searchHitIds is not { Count: > 0 } ids)
        {
            return null;
        }

        return thread.Messages
            .Where(message => ids.Contains(message.Id))
            .OrderByDescending(message => message.ReceivedAt)
            .FirstOrDefault();
    }

    public Guid SearchOpenMessageId(MessageThreadInfo thread)
    {
        ArgumentNullException.ThrowIfNull(thread);
        return MatchingSearchMessage(thread)?.Id ?? thread.Latest.Id;
    }

    public async Task OpenListedThreadAsync(
        Guid latestMessageId,
        CancellationToken cancellationToken = default)
    {
        await SelectThreadAsync(latestMessageId, cancellationToken).ConfigureAwait(false);
        var thread = CurrentThread()
            ?? throw new InvalidOperationException("Thread is not in the current list.");
        await SelectMessageAsync(SearchOpenMessageId(thread), cancellationToken).ConfigureAwait(false);
        await RefreshConversationAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RetrySelectedBodyAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedMessageId is not { } messageId)
        {
            throw new InvalidOperationException("Select a Message before downloading its body.");
        }

        var message = RequireLoadedMessage(messageId);

        await _app.FetchMessageBodyAsync(message.AccountId, messageId, cancellationToken).ConfigureAwait(false);
        await SelectMessageAsync(messageId, cancellationToken).ConfigureAwait(false);
        await RefreshConversationAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SelectMessageAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var message = RequireLoadedMessage(messageId);

        SelectedMessageId = messageId;
        AttachmentOpenError = null;

        var body = await _app
            .GetMessageBodyAsync(message.AccountId, messageId, cancellationToken)
            .ConfigureAwait(false);
        var html = await _app
            .GetMessageHtmlForDisplayAsync(message.AccountId, messageId, cancellationToken)
            .ConfigureAwait(false);
        BodyText = body;
        BodyHtml = html;
        BodyUnavailable = string.IsNullOrEmpty(body) && string.IsNullOrEmpty(html);
        SelectedToAddresses = message.ToAddresses;
        SelectedCcAddresses = message.CcAddresses;

        Attachments = await _app
            .ListAttachmentsAsync(message.AccountId, messageId, cancellationToken)
            .ConfigureAwait(false);

        if (!message.IsRead)
        {
            await _app
                .MarkReadAsync(message.AccountId, messageId, cancellationToken)
                .ConfigureAwait(false);
            ApplyToListed([messageId], item => item with { IsRead = true });
            if (SelectedAccountId is { } mailboxAccountId)
            {
                Mailboxes = await _app
                    .ListMailboxesAsync(mailboxAccountId, cancellationToken)
                    .ConfigureAwait(false);
            }

            await LoadAccountsAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task SelectNextUnreadAsync(CancellationToken cancellationToken = default)
    {
        if (await SelectAdjacentMatchingAsync(forward: true, UnreadMatch, UnreadThreadMatch, cancellationToken)
            .ConfigureAwait(false))
        {
            return;
        }

        await SelectUnreadInAdjacentMailboxAsync(forward: true, cancellationToken).ConfigureAwait(false);
    }

    public async Task SelectPreviousUnreadAsync(CancellationToken cancellationToken = default)
    {
        if (await SelectAdjacentMatchingAsync(forward: false, UnreadMatch, UnreadThreadMatch, cancellationToken)
            .ConfigureAwait(false))
        {
            return;
        }

        await SelectUnreadInAdjacentMailboxAsync(forward: false, cancellationToken).ConfigureAwait(false);
    }

    public Task SelectNextFlaggedAsync(CancellationToken cancellationToken = default) =>
        SelectAdjacentMatchingAsync(forward: true, FlaggedMatch, FlaggedThreadMatch, cancellationToken);

    public Task SelectPreviousFlaggedAsync(CancellationToken cancellationToken = default) =>
        SelectAdjacentMatchingAsync(forward: false, FlaggedMatch, FlaggedThreadMatch, cancellationToken);

    private static bool UnreadMatch(MessageInfo message) => !message.IsRead;

    private static bool UnreadThreadMatch(MessageThreadInfo thread) =>
        thread.Messages.Any(message => !message.IsRead);

    private static bool FlaggedMatch(MessageInfo message) => message.IsFlagged;

    private static bool FlaggedThreadMatch(MessageThreadInfo thread) =>
        thread.Messages.Any(message => message.IsFlagged);

    private async Task<bool> SelectAdjacentMatchingAsync(
        bool forward,
        Func<MessageInfo, bool> matchMessage,
        Func<MessageThreadInfo, bool> matchThread,
        CancellationToken cancellationToken)
    {
        if (SelectedThreadId is not null && Threads.Count > 0 && string.IsNullOrEmpty(SearchQuery))
        {
            return await SelectMatchingThreadAsync(forward, matchThread, cancellationToken).ConfigureAwait(false);
        }

        if (Messages.Count == 0)
        {
            return false;
        }

        var index = -1;
        if (SelectedMessageId is { } selectedId)
        {
            for (var i = 0; i < Messages.Count; i++)
            {
                if (Messages[i].Id == selectedId)
                {
                    index = i;
                    break;
                }
            }
        }

        if (forward)
        {
            var start = index + 1;
            for (var i = start; i < Messages.Count; i++)
            {
                if (matchMessage(Messages[i]))
                {
                    await SelectMessageAsync(Messages[i].Id, cancellationToken).ConfigureAwait(false);
                    return true;
                }
            }

            return false;
        }

        var previous = index < 0 ? Messages.Count - 1 : index - 1;
        for (var i = previous; i >= 0; i--)
        {
            if (matchMessage(Messages[i]))
            {
                await SelectMessageAsync(Messages[i].Id, cancellationToken).ConfigureAwait(false);
                return true;
            }
        }

        return false;
    }

    public async Task OpenArrivedMessageAsync(
        Guid accountId,
        Guid mailboxId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        await LoadAccountsAsync(cancellationToken).ConfigureAwait(false);
        await SelectAccountAsync(accountId, cancellationToken).ConfigureAwait(false);
        await SelectMailboxAsync(mailboxId, restoreThread: false, cancellationToken)
            .ConfigureAwait(false);
        var thread = Threads.FirstOrDefault(item => item.Messages.Any(message => message.Id == messageId));
        if (thread is not null)
        {
            await SelectThreadAsync(thread.Latest.Id, cancellationToken).ConfigureAwait(false);
        }

        if (Messages.Any(m => m.Id == messageId))
        {
            await SelectMessageAsync(messageId, cancellationToken).ConfigureAwait(false);
        }
    }


    public async Task MarkCurrentReadAsync(CancellationToken cancellationToken = default)
    {
        if (ShowingUnifiedInbox)
        {
            await _app.MarkUnifiedInboxReadAsync(cancellationToken).ConfigureAwait(false);
            await ReloadUnifiedInboxListingAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (SelectedAccountId is { } accountId && SelectedMailboxId is { } mailboxId)
        {
            await _app.MarkMailboxReadAsync(accountId, mailboxId, cancellationToken).ConfigureAwait(false);
            await ReloadMailboxListingAsync(accountId, mailboxId, cancellationToken).ConfigureAwait(false);
            Mailboxes = await _app.ListMailboxesAsync(accountId, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            return;
        }

        await LoadAccountsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkSelectedReadAsync(CancellationToken cancellationToken = default)
    {
        var targets = CurrentThread()?.Messages.ToList()
            ?? LoadedSelection();
        if (targets.Count == 0)
        {
            throw new InvalidOperationException("Select a Message before changing read state.");
        }

        var unread = targets.Where(item => !item.IsRead).ToList();
        if (unread.Count == 0)
        {
            return;
        }

        foreach (var message in unread)
        {
            await _app
                .MarkReadAsync(message.AccountId, message.Id, cancellationToken)
                .ConfigureAwait(false);
        }

        ApplyToListed(unread.Select(item => item.Id), item => item with { IsRead = true });
        if (SelectedAccountId is { } mailboxAccountId)
        {
            Mailboxes = await _app
                .ListMailboxesAsync(mailboxAccountId, cancellationToken)
                .ConfigureAwait(false);
        }

        await LoadAccountsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ToggleSelectedReadAsync(CancellationToken cancellationToken = default)
    {
        var targets = CurrentThread()?.Messages.ToList()
            ?? LoadedSelection();
        if (targets.Count == 0)
        {
            throw new InvalidOperationException("Select a Message before changing read state.");
        }

        if (targets.Any(message => !message.IsRead))
        {
            await MarkSelectedReadAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await MarkSelectedUnreadAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkSelectedUnreadAsync(CancellationToken cancellationToken = default)
    {
        var targets = CurrentThread()?.Messages.ToList()
            ?? LoadedSelection();
        if (targets.Count == 0)
        {
            throw new InvalidOperationException("Select a Message before marking it unread.");
        }

        foreach (var message in targets)
        {
            await _app
                .MarkUnreadAsync(message.AccountId, message.Id, cancellationToken)
                .ConfigureAwait(false);
        }

        ApplyToListed(targets.Select(item => item.Id), item => item with { IsRead = false });
        if (SelectedAccountId is { } mailboxAccountId)
        {
            Mailboxes = await _app
                .ListMailboxesAsync(mailboxAccountId, cancellationToken)
                .ConfigureAwait(false);
        }

        await LoadAccountsAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task ToggleSelectedFlagAsync(CancellationToken cancellationToken = default)
    {
        var id = SelectedThreadId ?? SelectedMessageId;
        if (id is null)
        {
            throw new InvalidOperationException("Select a Message before flagging it.");
        }

        return ToggleFlagAsync(id.Value, cancellationToken);
    }

    public async Task ToggleFlagAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var thread = Threads.FirstOrDefault(item => item.Latest.Id == id)
            ?? Threads.FirstOrDefault(item => item.Messages.Any(message => message.Id == id))
            ?? (CurrentThread() is { } open
                && open.Messages.Any(message => message.Id == id)
                    ? open
                    : null);
        var targets = thread is not null
            ? thread.Messages.ToList()
            : FindLoadedMessage(id) is { } loaded ? [loaded] : [];
        if (targets.Count == 0)
        {
            throw new InvalidOperationException("Select a Message before flagging it.");
        }

        var flagged = !targets[0].IsFlagged;
        foreach (var message in targets)
        {
            await _app
                .MarkFlaggedAsync(message.AccountId, message.Id, flagged, cancellationToken)
                .ConfigureAwait(false);
        }

        ApplyToListed(targets.Select(item => item.Id), item => item with { IsFlagged = flagged });
    }

    public async Task RefreshAfterAccountWorkAsync(CancellationToken cancellationToken = default)
    {
        var mailboxId = SelectedMailboxId;
        var messageId = SelectedMessageId;
        var accountId = SelectedAccountId;
        var unified = ShowingUnifiedInbox;
        var outbox = ShowingOutbox;

        await LoadAccountsAsync(cancellationToken).ConfigureAwait(false);

        if (unified)
        {
            await ShowUnifiedInboxAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (outbox && accountId is { } outboxAccountId)
        {
            await ShowOutboxAsync(outboxAccountId, cancellationToken).ConfigureAwait(false);
        }
        else if (accountId is { } selectedAccountId)
        {
            await SelectAccountAsync(selectedAccountId, cancellationToken).ConfigureAwait(false);
            if (mailboxId is { } selectedMailboxId)
            {
                await SelectMailboxAsync(selectedMailboxId, cancellationToken).ConfigureAwait(false);
            }
        }

        if (messageId is { } selectedMessageId)
        {
            await RestoreMessageInMailboxAsync(selectedMessageId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ReloadUnifiedInboxListingAsync(CancellationToken cancellationToken)
    {
        Threads = await _app.ListUnifiedInboxThreadsAsync(cancellationToken).ConfigureAwait(false);
        ApplyThreadListing();
    }

    private async Task ReloadMailboxListingAsync(
        Guid accountId,
        Guid mailboxId,
        CancellationToken cancellationToken)
    {
        Threads = await _app
            .ListMailboxThreadsAsync(accountId, mailboxId, cancellationToken)
            .ConfigureAwait(false);
        ApplyThreadListing();
    }

    private void ApplyThreadListing()
    {
        if (SelectedThreadId is { } threadId)
        {
            var thread = Threads.FirstOrDefault(item => item.Latest.Id == threadId)
                ?? Threads.FirstOrDefault(item => item.Messages.Any(message => message.Id == threadId));
            if (thread is null)
            {
                SelectedThreadId = null;
                Messages = Threads.Select(item => item.Latest).ToList();
            }
            else
            {
                SelectedThreadId = thread.Latest.Id;
                Messages = thread.Messages;
            }

            return;
        }

        Messages = Threads.Select(item => item.Latest).ToList();
    }

    private async Task RestoreMessageInMailboxAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var thread = Threads.FirstOrDefault(item => item.Messages.Any(message => message.Id == messageId));
        if (thread is not null && SelectedThreadId is null)
        {
            await SelectThreadAsync(thread.Latest.Id, cancellationToken).ConfigureAwait(false);
        }

        if (Messages.Any(message => message.Id == messageId))
        {
            await SelectMessageAsync(messageId, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<MailboxInfo>> ListMoveDestinationsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!ShowingUnifiedInbox && SelectedAccountId is { } accountId)
        {
            var scoped = await _app.ListMailboxesAsync(accountId, cancellationToken).ConfigureAwait(false);
            return await WithRecentMoveDestinationsAsync(scoped, cancellationToken).ConfigureAwait(false);
        }

        var ownerId = CurrentThread()?.Latest.AccountId
            ?? FindLoadedMessage(SelectedMessageId)?.AccountId;
        if (ownerId is null)
        {
            throw new InvalidOperationException("Select a Message before moving it.");
        }

        var listed = await _app.ListMailboxesAsync(ownerId.Value, cancellationToken).ConfigureAwait(false);
        return await WithRecentMoveDestinationsAsync(listed, cancellationToken).ConfigureAwait(false);
    }

    public IReadOnlyList<Guid> RecentMoveMailboxIds => _recentMoveMailboxIds;

    public async Task MoveSelectedToMailboxAsync(
        Guid destinationMailboxId,
        CancellationToken cancellationToken = default)
    {
        var neighborId = NeighborThreadId();
        var items = CaptureRelocateSelection();
        var sourceMailboxId = CaptureSourceMailboxId();
        foreach (var (accountId, messageId) in items)
        {
            await _app
                .MoveMessageAsync(accountId, messageId, destinationMailboxId, cancellationToken)
                .ConfigureAwait(false);
        }

        if (sourceMailboxId is { } source && source != destinationMailboxId)
        {
            RememberRelocate(UndoRelocateKind.Move, items, source);
            await RememberRecentMoveAsync(destinationMailboxId, cancellationToken).ConfigureAwait(false);
        }

        await ReloadAfterRelocateAsync(neighborId, cancellationToken).ConfigureAwait(false);
    }

    public async Task CopySelectedToMailboxAsync(
        Guid destinationMailboxId,
        CancellationToken cancellationToken = default)
    {
        var items = CaptureRelocateSelection();
        var sourceMailboxId = CaptureSourceMailboxId();
        foreach (var (accountId, messageId) in items)
        {
            await _app
                .CopyMessageAsync(accountId, messageId, destinationMailboxId, cancellationToken)
                .ConfigureAwait(false);
        }

        if (sourceMailboxId is { } source && source != destinationMailboxId)
        {
            await RememberRecentMoveAsync(destinationMailboxId, cancellationToken).ConfigureAwait(false);
        }

        await RefreshAllMailboxesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task MoveSelectedToTrashAsync(CancellationToken cancellationToken = default)
    {
        var items = CaptureRelocateSelection();
        foreach (var (accountId, messageId) in items)
        {
            await _app
                .MoveToTrashAsync(accountId, messageId, cancellationToken)
                .ConfigureAwait(false);
        }

        RememberRelocate(UndoRelocateKind.Trash, items, sourceMailboxId: null);
        await ReloadAfterRelocateAsync(NeighborThreadId(), cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> UndoLastTrashAsync(CancellationToken cancellationToken = default)
    {
        if (_undoItems.Count == 0 || _undoKind is not { } kind)
        {
            return false;
        }

        var items = _undoItems;
        var sourceMailboxId = _undoSourceMailboxId;
        _undoItems = [];
        _undoKind = null;
        _undoSourceMailboxId = null;
        LastRelocateUndoStatus = kind switch
        {
            UndoRelocateKind.Trash => "Restored from Trash.",
            UndoRelocateKind.Junk => "Restored from Junk.",
            UndoRelocateKind.Move => "Move undone.",
            _ => "Undone.",
        };
        foreach (var (accountId, messageId) in items)
        {
            switch (kind)
            {
                case UndoRelocateKind.Trash:
                    await _app
                        .RestoreFromTrashAsync(accountId, messageId, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case UndoRelocateKind.Junk:
                    await _app
                        .RestoreFromJunkAsync(accountId, messageId, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case UndoRelocateKind.Move:
                    if (sourceMailboxId is not { } destination)
                    {
                        return false;
                    }

                    await _app
                        .MoveMessageAsync(accountId, messageId, destination, cancellationToken)
                        .ConfigureAwait(false);
                    break;
            }
        }

        ClearMessageDetail();
        await ReloadListingAsync(cancellationToken).ConfigureAwait(false);
        await LoadAccountsAsync(cancellationToken).ConfigureAwait(false);
        await RestoreNeighborAsync(items[0].MessageId, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task RestoreSelectedFromTrashAsync(CancellationToken cancellationToken = default)
    {
        var thread = CurrentThread();
        if (thread is not null)
        {
            foreach (var item in thread.Messages)
            {
                await _app
                    .RestoreFromTrashAsync(item.AccountId, item.Id, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        else
        {
            if (SelectedMessageId is not { } messageId)
            {
                throw new InvalidOperationException("Select a Message before restoring it from Trash.");
            }

            var message = RequireLoadedMessage(messageId);

            await _app
                .RestoreFromTrashAsync(message.AccountId, messageId, cancellationToken)
                .ConfigureAwait(false);
        }

        await ReloadAfterRelocateAsync(NeighborThreadId(), cancellationToken).ConfigureAwait(false);
    }

    public async Task MoveSelectedToJunkAsync(CancellationToken cancellationToken = default)
    {
        var items = CaptureRelocateSelection();
        foreach (var (accountId, messageId) in items)
        {
            await _app
                .MoveToJunkAsync(accountId, messageId, cancellationToken)
                .ConfigureAwait(false);
        }

        RememberRelocate(UndoRelocateKind.Junk, items, sourceMailboxId: null);
        await ReloadAfterRelocateAsync(NeighborThreadId(), cancellationToken).ConfigureAwait(false);
    }

    public async Task MoveSelectedToArchiveAsync(CancellationToken cancellationToken = default)
    {
        var items = CaptureRelocateSelection();
        var sourceMailboxId = CaptureSourceMailboxId();
        foreach (var (accountId, messageId) in items)
        {
            await _app
                .MoveToArchiveAsync(accountId, messageId, cancellationToken)
                .ConfigureAwait(false);
        }

        RememberRelocate(UndoRelocateKind.Move, items, sourceMailboxId);
        await ReloadAfterRelocateAsync(NeighborThreadId(), cancellationToken).ConfigureAwait(false);
    }

    public async Task RestoreSelectedFromJunkAsync(CancellationToken cancellationToken = default)
    {
        var thread = CurrentThread();
        if (thread is not null)
        {
            foreach (var item in thread.Messages)
            {
                await _app
                    .RestoreFromJunkAsync(item.AccountId, item.Id, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        else
        {
            if (SelectedMessageId is not { } messageId)
            {
                throw new InvalidOperationException("Select a Message before restoring it from Junk.");
            }

            var message = RequireLoadedMessage(messageId);

            await _app
                .RestoreFromJunkAsync(message.AccountId, messageId, cancellationToken)
                .ConfigureAwait(false);
        }

        await ReloadAfterRelocateAsync(NeighborThreadId(), cancellationToken).ConfigureAwait(false);
    }

    public async Task PermanentlyDeleteSelectedAsync(CancellationToken cancellationToken = default)
    {
        var thread = CurrentThread();
        IReadOnlyList<(Guid AccountId, Guid MessageId)> deleted;
        if (thread is not null)
        {
            deleted = thread.Messages.Select(item => (item.AccountId, item.Id)).ToList();
            foreach (var item in thread.Messages)
            {
                await _app
                    .PermanentlyDeleteMessageAsync(item.AccountId, item.Id, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        else
        {
            if (SelectedMessageId is not { } messageId)
            {
                throw new InvalidOperationException("Select a Message before permanently deleting it.");
            }

            var message = RequireLoadedMessage(messageId);

            deleted = [(message.AccountId, messageId)];
            await _app
                .PermanentlyDeleteMessageAsync(message.AccountId, messageId, cancellationToken)
                .ConfigureAwait(false);
        }

        if (_undoItems.Count > 0)
        {
            var gone = deleted.ToHashSet();
            _undoItems = _undoItems.Where(item => !gone.Contains(item)).ToList();
            if (_undoItems.Count == 0)
            {
                _undoKind = null;
                _undoSourceMailboxId = null;
            }
        }

        await ReloadAfterRelocateAsync(NeighborThreadId(), cancellationToken).ConfigureAwait(false);
    }

    public Task EmptyTrashAsync(CancellationToken cancellationToken = default) =>
        EmptyRoleMailboxAsync(
            (app, accountId, ct) => app.EmptyTrashAsync(accountId, ct),
            "Select an Account before emptying Trash.",
            cancellationToken);

    public Task EmptyJunkAsync(CancellationToken cancellationToken = default) =>
        EmptyRoleMailboxAsync(
            (app, accountId, ct) => app.EmptyJunkAsync(accountId, ct),
            "Select an Account before emptying Junk.",
            cancellationToken);

    private async Task EmptyRoleMailboxAsync(
        Func<MailtideApp, Guid, CancellationToken, Task> empty,
        string missingAccountMessage,
        CancellationToken cancellationToken)
    {
        if (SelectedAccountId is not { } accountId)
        {
            throw new InvalidOperationException(missingAccountMessage);
        }

        await empty(_app, accountId, cancellationToken).ConfigureAwait(false);

        ClearMessageDetail();
        if (ShowingUnifiedInbox)
        {
            await ReloadUnifiedInboxListingAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (SelectedMailboxId is { } mailboxId)
        {
            await ReloadMailboxListingAsync(accountId, mailboxId, cancellationToken).ConfigureAwait(false);
            Mailboxes = await _app.ListMailboxesAsync(accountId, cancellationToken).ConfigureAwait(false);
        }

        await LoadAccountsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task OpenFirstThreadAttachmentAsync(
        Guid latestMessageId,
        CancellationToken cancellationToken = default)
    {
        AttachmentOpenError = null;
        var thread = Threads.FirstOrDefault(item => item.Latest.Id == latestMessageId)
            ?? Threads.FirstOrDefault(item => item.Messages.Any(message => message.Id == latestMessageId));
        var messages = thread?.Messages
            ?? Messages.Where(item => item.Id == latestMessageId).ToList();
        foreach (var message in messages.OrderByDescending(item => item.ReceivedAt).ThenByDescending(item => item.Id))
        {
            var attachments = await _app
                .ListAttachmentsAsync(message.AccountId, message.Id, cancellationToken)
                .ConfigureAwait(false);
            if (attachments.Count == 0)
            {
                continue;
            }

            ThreadAttachments = attachments;
            Attachments = attachments;
            await OpenAttachmentAsync(attachments[0].Id, cancellationToken).ConfigureAwait(false);
            return;
        }

        AttachmentOpenError = "Attachment is not available.";
    }

    public async Task OpenAttachmentAsync(
        Guid attachmentId,
        CancellationToken cancellationToken = default)
    {
        AttachmentOpenError = null;

        var attachment = ThreadAttachments.FirstOrDefault(item => item.Id == attachmentId)
            ?? Attachments.FirstOrDefault(item => item.Id == attachmentId);
        if (attachment is null)
        {
            AttachmentOpenError = "Attachment is not available.";
            return;
        }

        var content = await _app
            .OpenAttachmentAsync(attachment.AccountId, attachmentId, cancellationToken)
            .ConfigureAwait(false);
        if (content is null)
        {
            AttachmentOpenError = "Attachment is not available.";
            return;
        }

        var opener = HostBootstrap.OpenDownloadedAttachment
            ?? throw new InvalidOperationException(
                "HostBootstrap.OpenDownloadedAttachment was not set by the Host.");

        try
        {
            await opener
                .OpenAsync(content.FileName, content.ContentType, content.Content, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OpenAttachmentException ex)
        {
            AttachmentOpenError = ex.Message;
        }
    }

    public async Task SaveAttachmentAsync(
        Guid attachmentId,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        AttachmentOpenError = null;

        var attachment = ThreadAttachments.FirstOrDefault(item => item.Id == attachmentId)
            ?? Attachments.FirstOrDefault(item => item.Id == attachmentId);
        if (attachment is null)
        {
            AttachmentOpenError = "Attachment is not available.";
            return;
        }

        var content = await _app
            .OpenAttachmentAsync(attachment.AccountId, attachmentId, cancellationToken)
            .ConfigureAwait(false);
        if (content is null)
        {
            AttachmentOpenError = "Attachment is not available.";
            return;
        }

        await destination.WriteAsync(content.Content, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> MaterializeAttachmentAsync(
        Guid attachmentId,
        CancellationToken cancellationToken = default)
    {
        AttachmentOpenError = null;
        var attachment = ThreadAttachments.FirstOrDefault(item => item.Id == attachmentId)
            ?? Attachments.FirstOrDefault(item => item.Id == attachmentId);
        if (attachment is null)
        {
            AttachmentOpenError = "Attachment is not available.";
            return null;
        }

        var content = await _app
            .OpenAttachmentAsync(attachment.AccountId, attachmentId, cancellationToken)
            .ConfigureAwait(false);
        if (content is null)
        {
            AttachmentOpenError = "Attachment is not available.";
            return null;
        }

        var dir = Path.Combine(Path.GetTempPath(), "MailtideDrag", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var name = MailShellFormatting.UniqueFileName(
            content.FileName,
            candidate => File.Exists(Path.Combine(dir, candidate)));
        var path = Path.Combine(dir, name);
        await File.WriteAllBytesAsync(path, content.Content, cancellationToken).ConfigureAwait(false);
        return path;
    }

    public string SelectedMessageEmlFileName()
    {
        var subject = Conversation.FirstOrDefault(card => card.IsSelected)?.Message.Subject
            ?? Messages.FirstOrDefault(item => item.Id == SelectedMessageId)?.Subject
            ?? CurrentThread()?.Latest.Subject;
        return MessageRfc822.FileName(subject);
    }

    public async Task<string?> MaterializeMessageEmlAsync(
        Guid accountId,
        Guid messageId,
        string? subject,
        CancellationToken cancellationToken = default)
    {
        var dir = Path.Combine(Path.GetTempPath(), "MailtideDrag", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var name = MailShellFormatting.UniqueFileName(
            MessageRfc822.FileName(subject),
            candidate => File.Exists(Path.Combine(dir, candidate)));
        var path = Path.Combine(dir, name);
        await using (var stream = File.Create(path))
        {
            await _app
                .WriteMessageRfc822Async(accountId, messageId, stream, cancellationToken)
                .ConfigureAwait(false);
        }

        return path;
    }

    public async Task ExportSelectedMessageAsync(
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var message = Conversation.FirstOrDefault(card => card.IsSelected)?.Message
            ?? Messages.FirstOrDefault(item => item.Id == SelectedMessageId)
            ?? CurrentThread()?.Latest
            ?? throw new InvalidOperationException("Select a Message before saving it.");
        await _app
            .WriteMessageRfc822Async(message.AccountId, message.Id, destination, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<string> ReadSelectedMessageSourceAsync(CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream();
        await ExportSelectedMessageAsync(stream, cancellationToken).ConfigureAwait(false);
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    public async Task<int> SaveAllAttachmentsAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        var attachments = ThreadAttachments.Count > 0 ? ThreadAttachments : Attachments;
        var saved = 0;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var attachment in attachments)
        {
            var name = MailShellFormatting.UniqueFileName(
                attachment.FileName,
                candidate => used.Contains(candidate)
                    || File.Exists(Path.Combine(directory, candidate)));
            used.Add(name);
            var content = await _app
                .OpenAttachmentAsync(attachment.AccountId, attachment.Id, cancellationToken)
                .ConfigureAwait(false);
            if (content is null)
            {
                continue;
            }

            await File.WriteAllBytesAsync(
                    Path.Combine(directory, name),
                    content.Content,
                    cancellationToken)
                .ConfigureAwait(false);
            saved++;
        }

        return saved;
    }

    private void RefreshAuthenticationFailurePrompt()
    {
        var requiring = AccountStatuses
            .Where(row => row.Status.RequiresSignIn)
            .ToList();

        _dismissedAuthenticationFailureAccountIds.RemoveWhere(
            id => requiring.All(row => row.Account.Id != id));

        var next = requiring.FirstOrDefault(row =>
            !_dismissedAuthenticationFailureAccountIds.Contains(row.Account.Id));

        AuthenticationFailurePrompt = next is null
            ? null
            : new AuthenticationFailurePrompt(
                next.Account.Id,
                next.Account.DisplayName,
                next.Account.CredentialKind == CredentialKind.OAuth
                    ? AuthenticationFailureAction.Reauthorize
                    : AuthenticationFailureAction.EditAccount);
    }

    private void ClearMessageDetail()
    {
        SelectedMessageId = null;
        BodyText = null;
        BodyHtml = null;
        BodyUnavailable = false;
        SelectedToAddresses = [];
        SelectedCcAddresses = [];
        Attachments = [];
        ThreadAttachments = [];
        Conversation = [];
        _conversationMembers = null;
        AttachmentOpenError = null;
    }

    public async Task ShowOutboxAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        SelectedAccountId = accountId;
        SelectedMailboxId = null;
        SelectedThreadId = null;
        ShowingUnifiedInbox = false;
        ShowingOutbox = true;
        var chipFilter = MessageSearch.KeepChipFilter(SearchQuery, outbox: true);
        SearchQuery = string.Empty;
        Messages = [];
        Threads = [];
        ClearMessageDetail();
        Mailboxes = await _app.ListMailboxesAsync(accountId, cancellationToken).ConfigureAwait(false);
        await RefreshOutboxCountsAsync(cancellationToken).ConfigureAwait(false);
        await PersistNavAsync(cancellationToken).ConfigureAwait(false);
        await ReapplyChipFilterAsync(chipFilter, cancellationToken).ConfigureAwait(false);
    }

    public async Task SyncCurrentScopeAsync(CancellationToken cancellationToken = default)
    {
        var accountIds = ShowingUnifiedInbox || SelectedAccountId is null
            ? Accounts.Select(account => account.Id).ToList()
            : [SelectedAccountId.Value];

        foreach (var accountId in accountIds)
        {
            await _app.SendNowAsync(accountId, cancellationToken).ConfigureAwait(false);
            await _app.SyncNowAsync(accountId, cancellationToken).ConfigureAwait(false);
        }

        await ReloadAfterSyncAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SyncAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await _app.SendNowAsync(accountId, cancellationToken).ConfigureAwait(false);
        await _app.SyncNowAsync(accountId, cancellationToken).ConfigureAwait(false);
        await ReloadAfterSyncAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ReloadAfterSyncAsync(CancellationToken cancellationToken)
    {
        await LoadAccountsAsync(cancellationToken).ConfigureAwait(false);
        if (ShowingUnifiedInbox)
        {
            await ReloadUnifiedInboxListingAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (ShowingOutbox && SelectedAccountId is { } outboxAccountId)
        {
            Mailboxes = await _app
                .ListMailboxesAsync(outboxAccountId, cancellationToken)
                .ConfigureAwait(false);
        }
        else if (SelectedAccountId is { } accountId && SelectedMailboxId is { } mailboxId)
        {
            await ReloadMailboxListingAsync(accountId, mailboxId, cancellationToken).ConfigureAwait(false);
            Mailboxes = await _app.ListMailboxesAsync(accountId, cancellationToken).ConfigureAwait(false);
        }
    }

    public IReadOnlyList<ShellNavItem> BuildNavItems()
    {
        var unifiedUnread = Accounts.Sum(account => account.UnreadCount);
        var items = new List<ShellNavItem>
        {
            new()
            {
                Kind = ShellNavKind.Favorites,
                Title = "Favorites",
                Children = BuildFavoriteNavItems(),
            },
            new()
            {
                Kind = ShellNavKind.UnifiedInbox,
                Title = "Unified Inbox",
                UnreadCount = unifiedUnread,
            },
        };

        foreach (var row in AccountStatuses)
        {
            var children = MailShellFormatting
                .MailboxNavTree(AllMailboxes.Where(mailbox => mailbox.AccountId == row.Account.Id))
                .ToList();

            children.Add(new ShellNavItem
            {
                Kind = ShellNavKind.Outbox,
                Title = "Outbox",
                AccountId = row.Account.Id,
                OutboxCount = OutboxCounts.GetValueOrDefault(row.Account.Id),
                OutboxFailedCount = OutboxFailedCounts.GetValueOrDefault(row.Account.Id),
            });

            items.Add(new ShellNavItem
            {
                Kind = ShellNavKind.Account,
                Title = row.Account.DisplayName,
                AccountId = row.Account.Id,
                UnreadCount = row.Account.UnreadCount,
                SyncState = row.Status.State,
                Children = children,
            });
        }

        ApplyNavExpansion(items);
        return items;
    }

    public IReadOnlyList<Guid> FavoriteMailboxIds => _favoriteMailboxIds;

    public IReadOnlyList<string> RecentSearches => _recentSearches;

    public bool IsFavorite(Guid mailboxId) => _favoriteMailboxIds.Contains(mailboxId);

    public async Task PinFavoriteAsync(
        Guid mailboxId,
        int index = 0,
        CancellationToken cancellationToken = default)
    {
        await EnsureFavoritesAsync(cancellationToken).ConfigureAwait(false);
        if (_favoriteMailboxIds.Contains(mailboxId)
            || AllMailboxes.All(item => item.Id != mailboxId))
        {
            return;
        }

        var dest = Math.Clamp(index, 0, _favoriteMailboxIds.Count);
        _favoriteMailboxIds.Insert(dest, mailboxId);
        if (_favoriteMailboxIds.Count > 15)
        {
            _favoriteMailboxIds.RemoveRange(15, _favoriteMailboxIds.Count - 15);
        }

        await PersistFavoritesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UnpinFavoriteAsync(Guid mailboxId, CancellationToken cancellationToken = default)
    {
        await EnsureFavoritesAsync(cancellationToken).ConfigureAwait(false);
        if (!_favoriteMailboxIds.Remove(mailboxId))
        {
            return;
        }

        await PersistFavoritesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task MoveFavoriteAsync(
        Guid mailboxId,
        int index,
        CancellationToken cancellationToken = default)
    {
        await EnsureFavoritesAsync(cancellationToken).ConfigureAwait(false);
        var current = _favoriteMailboxIds.IndexOf(mailboxId);
        if (current < 0 || _favoriteMailboxIds.Count == 0)
        {
            return;
        }

        var dest = Math.Clamp(index, 0, _favoriteMailboxIds.Count - 1);
        if (dest == current)
        {
            return;
        }

        _favoriteMailboxIds.RemoveAt(current);
        _favoriteMailboxIds.Insert(dest, mailboxId);
        await PersistFavoritesAsync(cancellationToken).ConfigureAwait(false);
    }

    private IReadOnlyList<ShellNavItem> BuildFavoriteNavItems()
    {
        var manyAccounts = Accounts.Count > 1;
        var names = Accounts.ToDictionary(account => account.Id, account => account.DisplayName);
        var byId = AllMailboxes.ToDictionary(item => item.Id);
        var children = new List<ShellNavItem>();
        foreach (var id in _favoriteMailboxIds)
        {
            if (!byId.TryGetValue(id, out var mailbox))
            {
                continue;
            }

            children.Add(new ShellNavItem
            {
                Kind = ShellNavKind.Mailbox,
                Title = MailShellFormatting.MailboxPickerLabel(
                    mailbox,
                    manyAccounts ? names.GetValueOrDefault(mailbox.AccountId) : null),
                AccountId = mailbox.AccountId,
                MailboxId = mailbox.Id,
                Role = mailbox.Role,
                UnreadCount = mailbox.UnreadCount,
                IsFavoritePin = true,
            });
        }

        return children;
    }

    private async Task EnsureFavoritesAsync(CancellationToken cancellationToken)
    {
        if (_favoritesLoaded)
        {
            return;
        }

        _favoritesLoaded = true;
        var raw = await _app
            .GetPreferenceAsync(FavoritesPreferenceKey, cancellationToken)
            .ConfigureAwait(false);
        _favoriteMailboxIds = DecodeGuidList(raw);
    }

    public async Task ForgetRecentSearchAsync(string? query, CancellationToken cancellationToken = default)
    {
        await EnsureRecentSearchesAsync(cancellationToken).ConfigureAwait(false);
        var next = MailShellFormatting.RemoveRecentSearch(_recentSearches, query);
        if (next.Count == _recentSearches.Count)
        {
            return;
        }

        _recentSearches = next.ToList();
        await PersistRecentSearchesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RememberRecentSearchAsync(string? query, CancellationToken cancellationToken = default)
    {
        await EnsureRecentSearchesAsync(cancellationToken).ConfigureAwait(false);
        var next = MailShellFormatting.PushRecentSearch(_recentSearches, query);
        if (next.Count == _recentSearches.Count
            && next.SequenceEqual(_recentSearches, StringComparer.Ordinal))
        {
            return;
        }

        _recentSearches = next.ToList();
        await PersistRecentSearchesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureRecentSearchesAsync(CancellationToken cancellationToken)
    {
        if (_recentSearchesLoaded)
        {
            return;
        }

        _recentSearchesLoaded = true;
        var raw = await _app
            .GetPreferenceAsync(RecentSearchPreferenceKey, cancellationToken)
            .ConfigureAwait(false);
        _recentSearches = MailShellFormatting.DecodeRecentSearches(raw);
    }

    private Task PersistRecentSearchesAsync(CancellationToken cancellationToken) =>
        _app.SetPreferenceAsync(
            RecentSearchPreferenceKey,
            MailShellFormatting.EncodeRecentSearches(_recentSearches),
            cancellationToken);

    private Task PersistFavoritesAsync(CancellationToken cancellationToken) =>
        _app.SetPreferenceAsync(
            FavoritesPreferenceKey,
            string.Join(';', _favoriteMailboxIds.Select(id => id.ToString("D"))),
            cancellationToken);

    public async Task SetNavExpandedAsync(
        ShellNavItem item,
        bool expanded,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        await EnsureCollapsedNavAsync(cancellationToken).ConfigureAwait(false);
        var key = MailShellFormatting.NavPersistenceKey(item);
        if (key.Length == 0)
        {
            return;
        }

        var changed = expanded ? _collapsedNav.Remove(key) : _collapsedNav.Add(key);
        if (!changed)
        {
            return;
        }

        item.IsExpanded = expanded;
        await _app
            .SetPreferenceAsync(
                CollapsedNavPreferenceKey,
                string.Join(';', _collapsedNav),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private void ApplyNavExpansion(IReadOnlyList<ShellNavItem> items)
    {
        foreach (var item in items)
        {
            var key = MailShellFormatting.NavPersistenceKey(item);
            item.IsExpanded = key.Length == 0 || !_collapsedNav.Contains(key);
            if (item.Children.Count > 0)
            {
                ApplyNavExpansion(item.Children);
            }
        }
    }

    private async Task EnsureCollapsedNavAsync(CancellationToken cancellationToken)
    {
        if (_collapsedNavLoaded)
        {
            return;
        }

        _collapsedNavLoaded = true;
        var raw = await _app
            .GetPreferenceAsync(CollapsedNavPreferenceKey, cancellationToken)
            .ConfigureAwait(false);
        _collapsedNav = new HashSet<string>(
            (raw ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries),
            StringComparer.Ordinal);
    }

    private async Task RefreshAllMailboxesAsync(CancellationToken cancellationToken)
    {
        var all = new List<MailboxInfo>();
        foreach (var account in Accounts)
        {
            var listed = await _app.ListMailboxesAsync(account.Id, cancellationToken).ConfigureAwait(false);
            all.AddRange(listed);
        }

        AllMailboxes = all;
    }

    private async Task RefreshOutboxCountsAsync(CancellationToken cancellationToken)
    {
        var counts = new Dictionary<Guid, int>();
        var failed = new Dictionary<Guid, int>();
        foreach (var account in Accounts)
        {
            var items = await _app.ListOutboxAsync(account.Id, cancellationToken).ConfigureAwait(false);
            counts[account.Id] = items.Count;
            failed[account.Id] = items.Count(item => item.State == OutboxItemState.Failed);
        }

        OutboxCounts = counts;
        OutboxFailedCounts = failed;
    }

    public IReadOnlyList<ConversationCard> Conversation { get; private set; } = [];

    public bool ConversationExpanded { get; private set; }

    private readonly HashSet<Guid> _expandedConversationIds = [];

    private IReadOnlyList<MessageInfo>? _conversationMembers;

    private HashSet<Guid>? _searchHitIds;

    private IReadOnlyList<(Guid AccountId, Guid MessageId)> _undoItems = [];

    private UndoRelocateKind? _undoKind;

    private Guid? _undoSourceMailboxId;

    public string? LastRelocateUndoStatus { get; private set; }

    private List<Guid> _recentMoveMailboxIds = [];

    private bool _recentMovesLoaded;

    private HashSet<string> _collapsedNav = new(StringComparer.Ordinal);

    private bool _collapsedNavLoaded;

    private List<Guid> _favoriteMailboxIds = [];

    private bool _favoritesLoaded;

    private List<string> _recentSearches = [];

    private bool _recentSearchesLoaded;

    private Guid? _searchReturnThreadId;

    private Dictionary<string, Guid> _lastThreadByNav = new(StringComparer.Ordinal);

    public async Task OpenSearchHitAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        await SelectMessageAsync(messageId, cancellationToken).ConfigureAwait(false);
        var hit = Messages.FirstOrDefault(item => item.Id == messageId)
            ?? throw new InvalidOperationException("Message is not in the current list.");
        var threads = await _app
            .ListMailboxThreadsAsync(hit.AccountId, hit.MailboxId, cancellationToken)
            .ConfigureAwait(false);
        var thread = threads.FirstOrDefault(item => item.Messages.Any(message => message.Id == messageId));
        _conversationMembers = thread?.Messages ?? [hit];
        var unread = _conversationMembers.Where(message => !message.IsRead).ToList();
        foreach (var message in unread)
        {
            await _app
                .MarkReadAsync(message.AccountId, message.Id, cancellationToken)
                .ConfigureAwait(false);
        }

        if (unread.Count > 0)
        {
            ApplyToListed(unread.Select(message => message.Id), item => item with { IsRead = true });
        }

        await LoadThreadAttachmentsAsync(cancellationToken).ConfigureAwait(false);
        await RefreshConversationAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetConversationExpandedAsync(
        bool expanded,
        CancellationToken cancellationToken = default)
    {
        ConversationExpanded = expanded;
        foreach (var id in ConversationSource().Select(message => message.Id))
        {
            if (expanded)
            {
                _expandedConversationIds.Add(id);
            }
            else
            {
                _expandedConversationIds.Remove(id);
            }
        }

        await RefreshConversationAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RefreshConversationAsync(CancellationToken cancellationToken = default)
    {
        var cards = new List<ConversationCard>();
        var threadAttachments = new List<AttachmentInfo>();
        foreach (var message in MailShellFormatting.OrderConversationMessages(ConversationSource(), ConversationNewestFirst))
        {
            var body = await _app
                .GetMessageBodyAsync(message.AccountId, message.Id, cancellationToken)
                .ConfigureAwait(false);
            var html = await _app
                .GetMessageHtmlForDisplayAsync(message.AccountId, message.Id, cancellationToken)
                .ConfigureAwait(false);
            var attachments = await _app
                .ListAttachmentsAsync(message.AccountId, message.Id, cancellationToken)
                .ConfigureAwait(false);
            threadAttachments.AddRange(attachments);
            cards.Add(new ConversationCard
            {
                Message = message,
                Sender = MailShellFormatting.Sender(message.FromAddress),
                DateLabel = MailShellFormatting.DateLabel(message.ReceivedAt),
                ToLine = MailShellFormatting.Recipients("To: ", message.ToAddresses),
                CcLine = MailShellFormatting.Recipients("Cc: ", message.CcAddresses),
                BccLine = MailShellFormatting.Recipients("Bcc: ", message.BccAddresses),
                ReplyToLine = MailShellFormatting.Recipients("Reply-To: ", message.ReplyToAddresses),
                HasRemoteImages = HtmlRemoteContentPolicy.HasRemoteImages(html),
                BodyUnavailable = string.IsNullOrEmpty(body) && string.IsNullOrEmpty(html),
                BodyText = body ?? string.Empty,
                BodyHtml = html,
                IsSelected = message.Id == SelectedMessageId,
                ConversationExpanded = ConversationExpanded,
                Attachments = attachments,
                SearchNeedle = MailShellFormatting.SearchHighlightNeedle(SearchQuery),
            });
        }

        ThreadAttachments = threadAttachments;
        Conversation = cards;
    }

    private void ApplyToListed(IEnumerable<Guid> messageIds, Func<MessageInfo, MessageInfo> change)
    {
        var ids = messageIds.ToHashSet();
        Messages = Messages
            .Select(item => ids.Contains(item.Id) ? change(item) : item)
            .ToList();
        if (_conversationMembers is { Count: > 0 } members)
        {
            _conversationMembers = members
                .Select(item => ids.Contains(item.Id) ? change(item) : item)
                .ToList();
        }

        Threads = Threads
            .Select(thread =>
            {
                if (thread.Messages.All(message => !ids.Contains(message.Id))
                    && !ids.Contains(thread.Latest.Id))
                {
                    return thread;
                }

                var messages = thread.Messages
                    .Select(item => ids.Contains(item.Id) ? change(item) : item)
                    .ToList();
                var latest = messages.FirstOrDefault(item => item.Id == thread.Latest.Id)
                    ?? (ids.Contains(thread.Latest.Id) ? change(thread.Latest) : thread.Latest);
                return new MessageThreadInfo(latest, messages);
            })
            .ToList();
    }

    private IReadOnlyList<(Guid AccountId, Guid MessageId)> CaptureRelocateSelection()
    {
        if (CurrentThread() is { } thread)
        {
            return thread.Messages.Select(item => (item.AccountId, item.Id)).ToList();
        }

        if (SelectedMessageId is not { } messageId)
        {
            throw new InvalidOperationException("Select a Message before moving it.");
        }

        var message = RequireLoadedMessage(messageId);
        return [(message.AccountId, messageId)];
    }

    private Guid? CaptureSourceMailboxId() =>
        CurrentThread()?.Latest.MailboxId
        ?? FindLoadedMessage(SelectedMessageId)?.MailboxId;

    private void RememberRelocate(
        UndoRelocateKind kind,
        IReadOnlyList<(Guid AccountId, Guid MessageId)> items,
        Guid? sourceMailboxId)
    {
        _undoItems = items;
        _undoKind = kind;
        _undoSourceMailboxId = sourceMailboxId;
        LastRelocateUndoStatus = null;
    }

    private async Task EnsureRecentMovesAsync(CancellationToken cancellationToken)
    {
        if (_recentMovesLoaded)
        {
            return;
        }

        _recentMovesLoaded = true;
        var raw = await _app
            .GetPreferenceAsync(RecentMovePreferenceKey, cancellationToken)
            .ConfigureAwait(false);
        _recentMoveMailboxIds = DecodeGuidList(raw);
    }

    private async Task RememberRecentMoveAsync(Guid mailboxId, CancellationToken cancellationToken)
    {
        await EnsureRecentMovesAsync(cancellationToken).ConfigureAwait(false);
        _recentMoveMailboxIds.Remove(mailboxId);
        _recentMoveMailboxIds.Insert(0, mailboxId);
        if (_recentMoveMailboxIds.Count > 5)
        {
            _recentMoveMailboxIds.RemoveRange(5, _recentMoveMailboxIds.Count - 5);
        }

        await _app
            .SetPreferenceAsync(
                RecentMovePreferenceKey,
                string.Join(';', _recentMoveMailboxIds.Select(id => id.ToString("D"))),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<MailboxInfo>> WithRecentMoveDestinationsAsync(
        IReadOnlyList<MailboxInfo> listed,
        CancellationToken cancellationToken)
    {
        await EnsureRecentMovesAsync(cancellationToken).ConfigureAwait(false);
        if (_recentMoveMailboxIds.Count == 0)
        {
            return listed;
        }

        var byId = listed.ToDictionary(item => item.Id);
        var recent = new List<MailboxInfo>();
        foreach (var id in _recentMoveMailboxIds)
        {
            if (byId.TryGetValue(id, out var mailbox))
            {
                recent.Add(mailbox);
            }
        }

        var recentIds = recent.Select(item => item.Id).ToHashSet();
        return recent.Concat(listed.Where(item => !recentIds.Contains(item.Id))).ToList();
    }

    internal static List<Guid> DecodeGuidList(string? raw)
    {
        var ids = new List<Guid>();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return ids;
        }

        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Guid.TryParse(part, out var id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    private Guid? NeighborThreadId()
    {
        if (SelectedThreadId is not { } threadId || Threads.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(SearchQuery) && !ShowingOutbox)
            {
                return NeighborSearchHitId();
            }

            return null;
        }

        var index = -1;
        for (var i = 0; i < Threads.Count; i++)
        {
            if (Threads[i].Latest.Id == threadId
                || Threads[i].Messages.Any(message => message.Id == threadId))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return null;
        }

        if (index + 1 < Threads.Count)
        {
            return Threads[index + 1].Latest.Id;
        }

        if (index > 0)
        {
            return Threads[index - 1].Latest.Id;
        }

        return null;
    }

    private Guid? NeighborSearchHitId()
    {
        if (Messages.Count == 0)
        {
            return null;
        }

        var conversationIds = ConversationSource().Select(item => item.Id).ToHashSet();
        if (SelectedMessageId is { } selected)
        {
            conversationIds.Add(selected);
        }

        var index = -1;
        for (var i = 0; i < Messages.Count; i++)
        {
            if (conversationIds.Contains(Messages[i].Id))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return Messages[0].Id;
        }

        for (var i = index + 1; i < Messages.Count; i++)
        {
            if (!conversationIds.Contains(Messages[i].Id))
            {
                return Messages[i].Id;
            }
        }

        for (var i = index - 1; i >= 0; i--)
        {
            if (!conversationIds.Contains(Messages[i].Id))
            {
                return Messages[i].Id;
            }
        }

        return null;
    }

    private async Task ReloadListingAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(SearchQuery) && !ShowingOutbox)
        {
            await SearchAsync(SearchQuery, cancellationToken).ConfigureAwait(false);
            if (SelectedAccountId is { } searchAccountId)
            {
                Mailboxes = await _app
                    .ListMailboxesAsync(searchAccountId, cancellationToken)
                    .ConfigureAwait(false);
            }

            return;
        }

        if (ShowingUnifiedInbox)
        {
            await ReloadUnifiedInboxListingAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (SelectedAccountId is { } accountId && SelectedMailboxId is { } mailboxId)
        {
            await ReloadMailboxListingAsync(accountId, mailboxId, cancellationToken).ConfigureAwait(false);
            Mailboxes = await _app.ListMailboxesAsync(accountId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ReloadAfterRelocateAsync(Guid? neighborThreadId, CancellationToken cancellationToken)
    {
        ClearMessageDetail();
        await ReloadListingAsync(cancellationToken).ConfigureAwait(false);
        await LoadAccountsAsync(cancellationToken).ConfigureAwait(false);
        await RestoreNeighborAsync(neighborThreadId, cancellationToken).ConfigureAwait(false);
    }

    private async Task RestoreNeighborAsync(Guid? neighborId, CancellationToken cancellationToken)
    {
        if (neighborId is not { } id)
        {
            return;
        }

        var neighbor = Threads.FirstOrDefault(item => item.Latest.Id == id)
            ?? Threads.FirstOrDefault(item => item.Messages.Any(message => message.Id == id));
        if (neighbor is not null)
        {
            await SelectThreadAsync(neighbor.Latest.Id, cancellationToken).ConfigureAwait(false);
            var selectId = neighbor.Messages.Any(message => message.Id == id)
                ? id
                : neighbor.Latest.Id;
            if (Messages.Any(message => message.Id == selectId))
            {
                await SelectMessageAsync(selectId, cancellationToken).ConfigureAwait(false);
            }

            await RefreshConversationAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!string.IsNullOrWhiteSpace(SearchQuery) && !ShowingOutbox)
        {
            var hit = Messages.FirstOrDefault(item => item.Id == id)
                ?? Messages.FirstOrDefault();
            if (hit is not null)
            {
                await OpenSearchHitAsync(hit.Id, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task RevealInMailboxAsync(CancellationToken cancellationToken = default)
    {
        var message = CurrentThread()?.Latest
            ?? FindLoadedMessage(SelectedMessageId)
            ?? Messages.FirstOrDefault();
        if (message is null)
        {
            throw new InvalidOperationException("Select a Message before revealing its Mailbox.");
        }

        var messageId = message.Id;
        await SelectAccountAsync(message.AccountId, cancellationToken).ConfigureAwait(false);
        await SelectMailboxAsync(message.MailboxId, restoreThread: false, cancellationToken)
            .ConfigureAwait(false);
        var thread = Threads.FirstOrDefault(item => item.Messages.Any(member => member.Id == messageId))
            ?? Threads.FirstOrDefault(item => item.Latest.Id == messageId);
        if (thread is not null)
        {
            await SelectThreadAsync(thread.Latest.Id, cancellationToken).ConfigureAwait(false);
        }
    }

    private MessageThreadInfo? CurrentThread()
    {
        if (SelectedThreadId is { } threadId)
        {
            var listed = Threads.FirstOrDefault(item => item.Latest.Id == threadId)
                ?? Threads.FirstOrDefault(item => item.Messages.Any(message => message.Id == threadId));
            if (listed is not null)
            {
                return listed;
            }
        }

        if (_conversationMembers is { Count: > 0 } members)
        {
            var latest = members.MaxBy(item => item.ReceivedAt) ?? members[0];
            return new MessageThreadInfo(latest, members.ToList());
        }

        return null;
    }

    private async Task<bool> SelectMatchingThreadAsync(
        bool forward,
        Func<MessageThreadInfo, bool> matchThread,
        CancellationToken cancellationToken)
    {
        var index = -1;
        for (var i = 0; i < Threads.Count; i++)
        {
            if (Threads[i].Latest.Id == SelectedThreadId
                || Threads[i].Messages.Any(message => message.Id == SelectedThreadId))
            {
                index = i;
                break;
            }
        }

        if (forward)
        {
            for (var i = index + 1; i < Threads.Count; i++)
            {
                if (matchThread(Threads[i]))
                {
                    var latestId = Threads[i].Latest.Id;
                    await SelectThreadAsync(latestId, cancellationToken).ConfigureAwait(false);
                    await SelectMessageAsync(latestId, cancellationToken).ConfigureAwait(false);
                    return true;
                }
            }

            return false;
        }

        for (var i = index - 1; i >= 0; i--)
        {
            if (matchThread(Threads[i]))
            {
                var latestId = Threads[i].Latest.Id;
                await SelectThreadAsync(latestId, cancellationToken).ConfigureAwait(false);
                await SelectMessageAsync(latestId, cancellationToken).ConfigureAwait(false);
                return true;
            }
        }

        return false;
    }

    private async Task SelectUnreadInAdjacentMailboxAsync(bool forward, CancellationToken cancellationToken)
    {
        if (ShowingOutbox
            || !(string.IsNullOrEmpty(SearchQuery) || MessageSearch.IsChipFilterOnly(SearchQuery)))
        {
            return;
        }

        var unreadByMailbox = AllMailboxes.ToDictionary(item => item.Id, item => item.UnreadCount);
        var items = MailShellFormatting.FlattenNav(BuildNavItems());
        var current = CurrentNavForUnreadWalk(items);
        while (true)
        {
            var next = MailShellFormatting.AdjacentUnreadMailbox(
                items,
                current,
                SelectedMailboxId,
                unreadByMailbox,
                forward);
            if (next?.MailboxId is not { } mailboxId || next.AccountId is not { } accountId)
            {
                return;
            }

            if (SelectedAccountId != accountId)
            {
                await SelectAccountAsync(accountId, cancellationToken).ConfigureAwait(false);
            }

            await SelectMailboxAsync(mailboxId, restoreThread: false, cancellationToken).ConfigureAwait(false);
            if (await SelectMatchingThreadAsync(forward, UnreadThreadMatch, cancellationToken).ConfigureAwait(false)
                || await SelectExtremeMatchingThreadAsync(forward, UnreadThreadMatch, cancellationToken)
                    .ConfigureAwait(false)
                || await SelectAdjacentMatchingAsync(forward, UnreadMatch, UnreadThreadMatch, cancellationToken)
                    .ConfigureAwait(false))
            {
                return;
            }

            current = next;
        }
    }

    private async Task<bool> SelectExtremeMatchingThreadAsync(
        bool first,
        Func<MessageThreadInfo, bool> matchThread,
        CancellationToken cancellationToken)
    {
        if (first)
        {
            return await SelectMatchingThreadAsync(forward: true, matchThread, cancellationToken).ConfigureAwait(false);
        }

        for (var i = Threads.Count - 1; i >= 0; i--)
        {
            if (matchThread(Threads[i]))
            {
                var latestId = Threads[i].Latest.Id;
                await SelectThreadAsync(latestId, cancellationToken).ConfigureAwait(false);
                await SelectMessageAsync(latestId, cancellationToken).ConfigureAwait(false);
                return true;
            }
        }

        return false;
    }

    private ShellNavItem? CurrentNavForUnreadWalk(IReadOnlyList<ShellNavItem> items)
    {
        var inboxId = Mailboxes.FirstOrDefault(mailbox => mailbox.Role == MailboxRole.Inbox)?.Id
            ?? AllMailboxes.FirstOrDefault(mailbox =>
                mailbox.AccountId == SelectedAccountId && mailbox.Role == MailboxRole.Inbox)?.Id;
        foreach (var item in items)
        {
            if (item.Kind == ShellNavKind.Favorites || item.IsFavoritePin)
            {
                continue;
            }

            if (MailShellFormatting.IsCurrentNav(
                    item,
                    ShowingUnifiedInbox,
                    ShowingOutbox,
                    SelectedAccountId,
                    SelectedMailboxId,
                    inboxId))
            {
                return item;
            }
        }

        return null;
    }

    private async Task MarkListedMessagesReadAsync(CancellationToken cancellationToken)
    {
        var unread = Messages.Where(message => !message.IsRead).ToList();
        foreach (var message in unread)
        {
            await _app
                .MarkReadAsync(message.AccountId, message.Id, cancellationToken)
                .ConfigureAwait(false);
        }

        if (unread.Count == 0)
        {
            return;
        }

        var unreadIds = unread.Select(message => message.Id).ToHashSet();
        Messages = Messages
            .Select(item => unreadIds.Contains(item.Id) ? item with { IsRead = true } : item)
            .ToList();
        Threads = Threads
            .Select(thread => thread.Messages.Any(message => unreadIds.Contains(message.Id))
                ? new MessageThreadInfo(
                    thread.Latest with { IsRead = true },
                    thread.Messages
                        .Select(item => unreadIds.Contains(item.Id) ? item with { IsRead = true } : item)
                        .ToList())
                : thread)
            .ToList();
        if (SelectedAccountId is { } mailboxAccountId)
        {
            Mailboxes = await _app
                .ListMailboxesAsync(mailboxAccountId, cancellationToken)
                .ConfigureAwait(false);
        }

        await LoadAccountsAsync(cancellationToken).ConfigureAwait(false);
    }

    private List<MessageInfo> LoadedSelection()
    {
        if (SelectedMessageId is not { } messageId)
        {
            return [];
        }

        return FindLoadedMessage(messageId) is { } message ? [message] : [];
    }

    private MessageInfo RequireLoadedMessage(Guid messageId) =>
        FindLoadedMessage(messageId)
        ?? throw new InvalidOperationException("Message is not in the current list.");

    private MessageInfo? FindLoadedMessage(Guid? messageId) =>
        messageId is not { } id
            ? null
            : Messages.FirstOrDefault(item => item.Id == id)
                ?? ConversationSource().FirstOrDefault(item => item.Id == id);

    private IReadOnlyList<MessageInfo> ConversationSource()
    {
        if (_conversationMembers is { Count: > 0 } members)
        {
            return members;
        }

        if (CurrentThread() is { } thread)
        {
            return thread.Messages;
        }

        if (SelectedMessageId is { } selectedId)
        {
            return Messages.Where(item => item.Id == selectedId).ToList();
        }

        return [];
    }

    private string? CurrentNavKey()
    {
        if (ShowingUnifiedInbox)
        {
            return "unified";
        }

        if (ShowingOutbox)
        {
            return null;
        }

        return SelectedMailboxId is { } mailboxId
            ? "mailbox:" + mailboxId.ToString("D")
            : null;
    }

    private async Task RememberSelectedThreadAsync(CancellationToken cancellationToken)
    {
        if (CurrentNavKey() is not { } key || SelectedThreadId is not { } threadId)
        {
            return;
        }

        _lastThreadByNav[key] = threadId;
        await _app
            .SetPreferenceAsync(
                LastThreadsPreferenceKey,
                EncodeLastThreads(_lastThreadByNav),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task RestoreLastThreadAsync(CancellationToken cancellationToken)
    {
        if (CurrentNavKey() is not { } key
            || !_lastThreadByNav.TryGetValue(key, out var threadId))
        {
            return;
        }

        var thread = Threads.FirstOrDefault(item => item.Latest.Id == threadId)
            ?? Threads.FirstOrDefault(item => item.Messages.Any(message => message.Id == threadId));
        if (thread is null)
        {
            _lastThreadByNav.Remove(key);
            return;
        }

        await SelectThreadAsync(thread.Latest.Id, cancellationToken).ConfigureAwait(false);
        await SelectMessageAsync(thread.Latest.Id, cancellationToken).ConfigureAwait(false);
        await RefreshConversationAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static string EncodeLastThreads(IReadOnlyDictionary<string, Guid> map) =>
        string.Join(';', map.Select(pair => pair.Key + "=" + pair.Value.ToString("D")));

    internal static Dictionary<string, Guid> DecodeLastThreads(string? raw)
    {
        var map = new Dictionary<string, Guid>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return map;
        }

        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = part.IndexOf('=');
            if (split <= 0)
            {
                continue;
            }

            if (Guid.TryParse(part[(split + 1)..], out var id))
            {
                map[part[..split]] = id;
            }
        }

        return map;
    }

    private async Task LoadThreadAttachmentsAsync(CancellationToken cancellationToken)
    {
        var attachments = new List<AttachmentInfo>();
        foreach (var message in ConversationSource())
        {
            var listed = await _app
                .ListAttachmentsAsync(message.AccountId, message.Id, cancellationToken)
                .ConfigureAwait(false);
            attachments.AddRange(listed);
        }

        ThreadAttachments = attachments;
    }
}

public sealed record AccountStatusRow(AccountInfo Account, AccountStatus Status);

internal enum UndoRelocateKind
{
    Trash = 0,
    Junk = 1,
    Move = 2,
}

public enum AuthenticationFailureAction
{
    Reauthorize = 0,
    EditAccount = 1,
}

public sealed record AuthenticationFailurePrompt(
    Guid AccountId,
    string DisplayName,
    AuthenticationFailureAction Action);
