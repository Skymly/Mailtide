using Mailtide.Core.Store;
using Microsoft.EntityFrameworkCore;

namespace Mailtide.Core.Outbox;

/// <summary>
/// Local Outbox item state transitions. Caller serializes store access.
/// </summary>
internal sealed class OutboxStateMachine
{
    private readonly MailtideDbContext _db;

    public OutboxStateMachine(MailtideDbContext db)
    {
        _db = db;
    }

    public async Task RequeueSendingAsync(
        Guid accountId,
        Guid outboxItemId,
        CancellationToken cancellationToken)
    {
        var item = await _db.OutboxItems
            .SingleOrDefaultAsync(
                o => o.AccountId == accountId && o.Id == outboxItemId,
                cancellationToken)
            .ConfigureAwait(false);

        if (item is null || item.State != OutboxItemState.Sending)
        {
            return;
        }

        item.State = OutboxItemState.Queued;
        item.ErrorMessage = null;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkQueuedFailedAsync(
        Guid accountId,
        Guid outboxItemId,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        var item = await _db.OutboxItems
            .SingleOrDefaultAsync(
                o => o.AccountId == accountId && o.Id == outboxItemId,
                cancellationToken)
            .ConfigureAwait(false);

        if (item is null || item.State != OutboxItemState.Queued)
        {
            return;
        }

        item.State = OutboxItemState.Failed;
        item.ErrorMessage = errorMessage;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkFailedAsync(
        Guid accountId,
        Guid outboxItemId,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        var item = await _db.OutboxItems
            .SingleOrDefaultAsync(
                o => o.AccountId == accountId && o.Id == outboxItemId,
                cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
        {
            return;
        }

        if (item.State is not (OutboxItemState.Queued or OutboxItemState.Sending))
        {
            return;
        }

        item.State = OutboxItemState.Failed;
        item.ErrorMessage = errorMessage;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task FailQueuedAsync(
        Guid accountId,
        IReadOnlyList<Guid> itemIds,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        foreach (var itemId in itemIds)
        {
            await MarkQueuedFailedAsync(accountId, itemId, errorMessage, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
