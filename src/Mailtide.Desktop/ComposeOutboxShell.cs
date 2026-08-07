using Mailtide.Core;

namespace Mailtide.Desktop;

/// <summary>
/// UI-framework-agnostic compose / Outbox surface. Issues Core intents only.
/// </summary>
public sealed class ComposeOutboxShell
{
    private readonly MailtideApp _app;

    public ComposeOutboxShell(MailtideApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        _app = app;
    }

    public Guid? SelectedAccountId { get; private set; }

    public IReadOnlyList<DraftInfo> Drafts { get; private set; } = [];

    public IReadOnlyList<OutboxItemInfo> OutboxItems { get; private set; } = [];

    public async Task SelectAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        SelectedAccountId = accountId;
        await RefreshListsAsync(accountId, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveDraftAsync(
        string toAddresses,
        string subject,
        string bodyText,
        CancellationToken cancellationToken = default)
    {
        var accountId = RequireSelectedAccount();
        var addresses = ParseAddresses(toAddresses);
        await _app
            .SaveDraftAsync(accountId, new DraftContent(addresses, subject, bodyText), cancellationToken)
            .ConfigureAwait(false);
        Drafts = await _app.ListDraftsAsync(accountId, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendAsync(Guid draftId, CancellationToken cancellationToken = default)
    {
        var accountId = RequireSelectedAccount();
        await _app.SendAsync(accountId, draftId, cancellationToken).ConfigureAwait(false);
        await RefreshListsAsync(accountId, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendNowAsync(CancellationToken cancellationToken = default)
    {
        var accountId = RequireSelectedAccount();
        await _app.SendNowAsync(accountId, cancellationToken).ConfigureAwait(false);
        await RefreshListsAsync(accountId, cancellationToken).ConfigureAwait(false);
    }

    public async Task SyncNowAsync(CancellationToken cancellationToken = default)
    {
        var accountId = RequireSelectedAccount();
        await _app.SyncNowAsync(accountId, cancellationToken).ConfigureAwait(false);
    }

    public async Task RetryOutboxItemAsync(Guid outboxItemId, CancellationToken cancellationToken = default)
    {
        var accountId = RequireSelectedAccount();
        await _app.RetryOutboxItemAsync(accountId, outboxItemId, cancellationToken).ConfigureAwait(false);
        await RefreshListsAsync(accountId, cancellationToken).ConfigureAwait(false);
    }

    public async Task DiscardOutboxItemAsync(Guid outboxItemId, CancellationToken cancellationToken = default)
    {
        var accountId = RequireSelectedAccount();
        await _app.DiscardOutboxItemAsync(accountId, outboxItemId, cancellationToken).ConfigureAwait(false);
        await RefreshListsAsync(accountId, cancellationToken).ConfigureAwait(false);
    }

    private async Task RefreshListsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        Drafts = await _app.ListDraftsAsync(accountId, cancellationToken).ConfigureAwait(false);
        OutboxItems = await _app.ListOutboxAsync(accountId, cancellationToken).ConfigureAwait(false);
    }

    private Guid RequireSelectedAccount() =>
        SelectedAccountId
        ?? throw new InvalidOperationException("Select an Account before composing.");

    private static IReadOnlyList<string> ParseAddresses(string toAddresses) =>
        toAddresses
            .Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
