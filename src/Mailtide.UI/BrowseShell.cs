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

    public IReadOnlyList<MessageInfo> Messages { get; private set; } = [];

    public IReadOnlyList<MessageThreadInfo> Threads { get; private set; } = [];

    public IReadOnlyList<AttachmentInfo> Attachments { get; private set; } = [];

    public Guid? SelectedAccountId { get; private set; }

    public Guid? SelectedMailboxId { get; private set; }

    public Guid? SelectedThreadId { get; private set; }

    public Guid? SelectedMessageId { get; private set; }

    public bool ShowingUnifiedInbox { get; private set; }

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
        SearchQuery = string.Empty;
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
    }

    public async Task SelectMailboxAsync(Guid mailboxId, CancellationToken cancellationToken = default)
    {
        if (SelectedAccountId is not { } accountId)
        {
            throw new InvalidOperationException("Select an Account before selecting a Mailbox.");
        }

        SelectedMailboxId = mailboxId;
        ShowingUnifiedInbox = false;
        SearchQuery = string.Empty;
        SelectedThreadId = null;
        ClearMessageDetail();
        await ReloadMailboxListingAsync(accountId, mailboxId, cancellationToken).ConfigureAwait(false);
    }

    public async Task SelectThreadAsync(Guid latestMessageId, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var thread = Threads.FirstOrDefault(item => item.Latest.Id == latestMessageId)
            ?? throw new InvalidOperationException("Thread is not in the current list.");
        SelectedThreadId = thread.Latest.Id;
        SelectedMessageId = null;
        ClearMessageDetail();
        Messages = thread.Messages;
    }

    public async Task ShowUnifiedInboxAsync(CancellationToken cancellationToken = default)
    {
        SelectedAccountId = null;
        SelectedMailboxId = null;
        SelectedThreadId = null;
        ShowingUnifiedInbox = true;
        SearchQuery = string.Empty;
        Mailboxes = [];
        ClearMessageDetail();
        await ReloadUnifiedInboxListingAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        SearchQuery = query ?? string.Empty;
        SelectedThreadId = null;
        if (ShowingUnifiedInbox)
        {
            if (string.IsNullOrWhiteSpace(SearchQuery))
            {
                await ReloadUnifiedInboxListingAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            Threads = [];
            Messages = await _app.SearchUnifiedInboxAsync(SearchQuery, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (SelectedAccountId is not { } accountId || SelectedMailboxId is not { } mailboxId)
        {
            Messages = [];
            Threads = [];
            return;
        }

        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            await ReloadMailboxListingAsync(accountId, mailboxId, cancellationToken).ConfigureAwait(false);
            return;
        }

        Threads = [];
        Messages = await _app
            .SearchMessagesAsync(accountId, mailboxId, SearchQuery, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SelectMessageAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var message = Messages.FirstOrDefault(m => m.Id == messageId)
            ?? throw new InvalidOperationException("Message is not in the current list.");

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
            Messages = Messages
                .Select(item => item.Id == messageId ? item with { IsRead = true } : item)
                .ToList();
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
        if (Messages.Count == 0)
        {
            return;
        }

        var start = 0;
        if (SelectedMessageId is { } selectedId)
        {
            var index = -1;
            for (var i = 0; i < Messages.Count; i++)
            {
                if (Messages[i].Id == selectedId)
                {
                    index = i;
                    break;
                }
            }

            start = index + 1;
        }

        MessageInfo? next = null;
        for (var i = start; i < Messages.Count; i++)
        {
            if (!Messages[i].IsRead)
            {
                next = Messages[i];
                break;
            }
        }

        if (next is null)
        {
            return;
        }

        await SelectMessageAsync(next.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task OpenArrivedMessageAsync(
        Guid accountId,
        Guid mailboxId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        await LoadAccountsAsync(cancellationToken).ConfigureAwait(false);
        await SelectAccountAsync(accountId, cancellationToken).ConfigureAwait(false);
        await SelectMailboxAsync(mailboxId, cancellationToken).ConfigureAwait(false);
        if (Messages.Any(m => m.Id == messageId))
        {
            await SelectMessageAsync(messageId, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task SelectPreviousUnreadAsync(CancellationToken cancellationToken = default)
    {
        if (Messages.Count == 0)
        {
            return;
        }

        var start = Messages.Count - 1;
        if (SelectedMessageId is { } selectedId)
        {
            var index = -1;
            for (var i = 0; i < Messages.Count; i++)
            {
                if (Messages[i].Id == selectedId)
                {
                    index = i;
                    break;
                }
            }

            start = index - 1;
        }

        MessageInfo? previous = null;
        for (var i = start; i >= 0; i--)
        {
            if (!Messages[i].IsRead)
            {
                previous = Messages[i];
                break;
            }
        }

        if (previous is null)
        {
            return;
        }

        await SelectMessageAsync(previous.Id, cancellationToken).ConfigureAwait(false);
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

    public async Task MarkSelectedUnreadAsync(CancellationToken cancellationToken = default)

    {
        if (SelectedMessageId is not { } messageId)
        {
            throw new InvalidOperationException("Select a Message before marking it unread.");
        }

        var message = Messages.FirstOrDefault(m => m.Id == messageId)
            ?? throw new InvalidOperationException("Message is not in the current list.");

        await _app
            .MarkUnreadAsync(message.AccountId, messageId, cancellationToken)
            .ConfigureAwait(false);
        Messages = Messages
            .Select(item => item.Id == messageId ? item with { IsRead = false } : item)
            .ToList();
        if (SelectedAccountId is { } mailboxAccountId)
        {
            Mailboxes = await _app
                .ListMailboxesAsync(mailboxAccountId, cancellationToken)
                .ConfigureAwait(false);
        }

        await LoadAccountsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ToggleSelectedFlagAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedMessageId is not { } messageId)
        {
            throw new InvalidOperationException("Select a Message before flagging it.");
        }

        var message = Messages.FirstOrDefault(m => m.Id == messageId)
            ?? throw new InvalidOperationException("Message is not in the current list.");

        var flagged = !message.IsFlagged;
        await _app
            .MarkFlaggedAsync(message.AccountId, messageId, flagged, cancellationToken)
            .ConfigureAwait(false);
        Messages = Messages
            .Select(item => item.Id == messageId ? item with { IsFlagged = flagged } : item)
            .ToList();
    }

    public async Task RefreshAfterAccountWorkAsync(CancellationToken cancellationToken = default)
    {
        var mailboxId = SelectedMailboxId;
        var messageId = SelectedMessageId;
        var accountId = SelectedAccountId;
        var unified = ShowingUnifiedInbox;

        await LoadAccountsAsync(cancellationToken).ConfigureAwait(false);

        if (unified)
        {
            await ShowUnifiedInboxAsync(cancellationToken).ConfigureAwait(false);
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
            return await _app.ListMailboxesAsync(accountId, cancellationToken).ConfigureAwait(false);
        }

        if (SelectedMessageId is not { } messageId)
        {
            throw new InvalidOperationException("Select a Message before moving it.");
        }

        var message = Messages.FirstOrDefault(m => m.Id == messageId)
            ?? throw new InvalidOperationException("Message is not in the current list.");

        return await _app.ListMailboxesAsync(message.AccountId, cancellationToken).ConfigureAwait(false);
    }

    public async Task MoveSelectedToMailboxAsync(
        Guid destinationMailboxId,
        CancellationToken cancellationToken = default)
    {
        if (!ShowingUnifiedInbox
            && SelectedThreadId is { } threadId
            && SelectedAccountId is { } threadAccountId
            && SelectedMailboxId is { } threadMailboxId)
        {
            await _app
                .MoveMailboxThreadAsync(
                    threadAccountId,
                    threadMailboxId,
                    threadId,
                    destinationMailboxId,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            if (SelectedMessageId is not { } messageId)
            {
                throw new InvalidOperationException("Select a Message before moving it.");
            }

            var message = Messages.FirstOrDefault(m => m.Id == messageId)
                ?? throw new InvalidOperationException("Message is not in the current list.");

            await _app
                .MoveMessageAsync(message.AccountId, messageId, destinationMailboxId, cancellationToken)
                .ConfigureAwait(false);
        }

        ClearMessageDetail();
        if (ShowingUnifiedInbox)
        {
            await ReloadUnifiedInboxListingAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (SelectedAccountId is { } accountId && SelectedMailboxId is { } mailboxId)
        {
            await ReloadMailboxListingAsync(accountId, mailboxId, cancellationToken).ConfigureAwait(false);
            Mailboxes = await _app.ListMailboxesAsync(accountId, cancellationToken).ConfigureAwait(false);
        }

        await LoadAccountsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task MoveSelectedToTrashAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedMessageId is not { } messageId)
        {
            throw new InvalidOperationException("Select a Message before moving it to Trash.");
        }

        var message = Messages.FirstOrDefault(m => m.Id == messageId)
            ?? throw new InvalidOperationException("Message is not in the current list.");

        await _app
            .MoveToTrashAsync(message.AccountId, messageId, cancellationToken)
            .ConfigureAwait(false);

        ClearMessageDetail();
        if (ShowingUnifiedInbox)
        {
            await ReloadUnifiedInboxListingAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (SelectedAccountId is { } accountId && SelectedMailboxId is { } mailboxId)
        {
            await ReloadMailboxListingAsync(accountId, mailboxId, cancellationToken).ConfigureAwait(false);
            Mailboxes = await _app.ListMailboxesAsync(accountId, cancellationToken).ConfigureAwait(false);
        }

        await LoadAccountsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RestoreSelectedFromTrashAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedMessageId is not { } messageId)
        {
            throw new InvalidOperationException("Select a Message before restoring it from Trash.");
        }

        var message = Messages.FirstOrDefault(m => m.Id == messageId)
            ?? throw new InvalidOperationException("Message is not in the current list.");

        await _app
            .RestoreFromTrashAsync(message.AccountId, messageId, cancellationToken)
            .ConfigureAwait(false);

        ClearMessageDetail();
        if (ShowingUnifiedInbox)
        {
            await ReloadUnifiedInboxListingAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (SelectedAccountId is { } accountId && SelectedMailboxId is { } mailboxId)
        {
            await ReloadMailboxListingAsync(accountId, mailboxId, cancellationToken).ConfigureAwait(false);
            Mailboxes = await _app.ListMailboxesAsync(accountId, cancellationToken).ConfigureAwait(false);
        }

        await LoadAccountsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task EmptyTrashAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedAccountId is not { } accountId)
        {
            throw new InvalidOperationException("Select an Account before emptying Trash.");
        }

        await _app
            .EmptyTrashAsync(accountId, cancellationToken)
            .ConfigureAwait(false);

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

    public async Task OpenAttachmentAsync(
        Guid attachmentId,
        CancellationToken cancellationToken = default)
    {
        AttachmentOpenError = null;

        if (SelectedMessageId is not { } messageId)
        {
            throw new InvalidOperationException("Select a Message before opening an attachment.");
        }

        var message = Messages.FirstOrDefault(m => m.Id == messageId)
            ?? throw new InvalidOperationException("Message is not in the current list.");

        var content = await _app
            .OpenAttachmentAsync(message.AccountId, attachmentId, cancellationToken)
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
        AttachmentOpenError = null;
    }
}

public sealed record AccountStatusRow(AccountInfo Account, AccountStatus Status);

public enum AuthenticationFailureAction
{
    Reauthorize = 0,
    EditAccount = 1,
}

public sealed record AuthenticationFailurePrompt(
    Guid AccountId,
    string DisplayName,
    AuthenticationFailureAction Action);
