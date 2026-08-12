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

    public string? BodyText { get; private set; }

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
        _app.AddGoogleAccountAsync(displayName, cancellationToken);

    public Task<AccountInfo> AddMicrosoftConsumerAccountAsync(
        string displayName,
        CancellationToken cancellationToken = default) =>
        _app.AddMicrosoftConsumerAccountAsync(displayName, cancellationToken);

    public Task<AccountInfo> AddQqMailAccountAsync(
        QqMailAccountDraft draft,
        CancellationToken cancellationToken = default) =>
        _app.AddQqMailAccountAsync(draft, cancellationToken);

    public Task<AccountInfo> AddManualAccountAsync(
        ManualAccountDraft draft,
        CancellationToken cancellationToken = default) =>
        _app.AddManualAccountAsync(draft, cancellationToken);

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
        Mailboxes = [];
        ClearMessageDetail();
        Messages = await _app.ListUnifiedInboxAsync(cancellationToken).ConfigureAwait(false);
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
        BodyText = body;
        BodyUnavailable = string.IsNullOrEmpty(body);

        Attachments = await _app
            .ListAttachmentsAsync(message.AccountId, messageId, cancellationToken)
            .ConfigureAwait(false);
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
        catch (OpenAttachmentException)
        {
            AttachmentOpenError = "Could not open the attachment.";
        }
    }

    private void ClearMessageDetail()
    {
        SelectedMessageId = null;
        BodyText = null;
        BodyUnavailable = false;
        Attachments = [];
        AttachmentOpenError = null;
    }
}

public sealed record AccountStatusRow(AccountInfo Account, AccountStatus Status);
