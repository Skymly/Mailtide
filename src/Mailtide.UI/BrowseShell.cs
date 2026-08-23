using Mailtide.Core;

namespace Mailtide.UI;

/// <summary>
/// UI-framework-agnostic browse surface. Issues Core queries and Host ports for open/confirm.
/// </summary>
public sealed class BrowseShell
{
    private readonly MailtideApp _app;

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

    public IReadOnlyList<AttachmentInfo> Attachments { get; private set; } = [];

    public Guid? SelectedAccountId { get; private set; }

    public Guid? SelectedMailboxId { get; private set; }

    public Guid? SelectedMessageId { get; private set; }

    public bool ShowingUnifiedInbox { get; private set; }

    public string SearchQuery { get; private set; } = string.Empty;

    public string? BodyText { get; private set; }

    public string? BodyHtml { get; private set; }

    public bool BodyUnavailable { get; private set; }

    public string? AttachmentOpenError { get; private set; }

    public async Task LoadAccountsAsync(CancellationToken cancellationToken = default)
    {
        Accounts = await _app.ListAccountsAsync(cancellationToken).ConfigureAwait(false);
        AccountStatuses = Accounts
            .Select(account => new AccountStatusRow(account, _app.GetAccountStatus(account.Id)))
            .ToList();
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
        ClearMessageDetail();
        Messages = await _app
            .ListMessagesAsync(accountId, mailboxId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task ShowUnifiedInboxAsync(CancellationToken cancellationToken = default)
    {
        SelectedAccountId = null;
        SelectedMailboxId = null;
        ShowingUnifiedInbox = true;
        SearchQuery = string.Empty;
        Mailboxes = [];
        ClearMessageDetail();
        Messages = await _app.ListUnifiedInboxAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        SearchQuery = query ?? string.Empty;
        if (ShowingUnifiedInbox)
        {
            Messages = await _app.SearchUnifiedInboxAsync(SearchQuery, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (SelectedAccountId is not { } accountId || SelectedMailboxId is not { } mailboxId)
        {
            Messages = [];
            return;
        }

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

        if (messageId is { } selectedMessageId
            && Messages.Any(message => message.Id == selectedMessageId))
        {
            await SelectMessageAsync(selectedMessageId, cancellationToken).ConfigureAwait(false);
        }
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
            Messages = await _app.ListUnifiedInboxAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (SelectedAccountId is { } accountId && SelectedMailboxId is { } mailboxId)
        {
            Messages = await _app
                .ListMessagesAsync(accountId, mailboxId, cancellationToken)
                .ConfigureAwait(false);
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
            Messages = await _app.ListUnifiedInboxAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (SelectedAccountId is { } accountId && SelectedMailboxId is { } mailboxId)
        {
            Messages = await _app
                .ListMessagesAsync(accountId, mailboxId, cancellationToken)
                .ConfigureAwait(false);
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
            Messages = await _app.ListUnifiedInboxAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (SelectedMailboxId is { } mailboxId)
        {
            Messages = await _app
                .ListMessagesAsync(accountId, mailboxId, cancellationToken)
                .ConfigureAwait(false);
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

    private void ClearMessageDetail()
    {
        SelectedMessageId = null;
        BodyText = null;
        BodyHtml = null;
        BodyUnavailable = false;
        Attachments = [];
        AttachmentOpenError = null;
    }
}

public sealed record AccountStatusRow(AccountInfo Account, AccountStatus Status);
