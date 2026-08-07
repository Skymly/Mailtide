using Mailtide.Core;

namespace Mailtide.Desktop;

/// <summary>
/// UI-framework-agnostic browse surface. Issues Core queries only.
/// </summary>
public sealed class BrowseShell
{
    private readonly MailtideApp _app;

    public BrowseShell(MailtideApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        _app = app;
    }

    public IReadOnlyList<AccountInfo> Accounts { get; private set; } = [];

    public IReadOnlyList<MailboxInfo> Mailboxes { get; private set; } = [];

    public IReadOnlyList<MessageInfo> Messages { get; private set; } = [];

    public Guid? SelectedAccountId { get; private set; }

    public Guid? SelectedMailboxId { get; private set; }

    public bool ShowingUnifiedInbox { get; private set; }

    public async Task LoadAccountsAsync(CancellationToken cancellationToken = default)
    {
        Accounts = await _app.ListAccountsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SelectAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        SelectedAccountId = accountId;
        SelectedMailboxId = null;
        ShowingUnifiedInbox = false;
        Messages = [];
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
        Messages = await _app.ListUnifiedInboxAsync(cancellationToken).ConfigureAwait(false);
    }
}
