using Mailtide.Core.Imap;
using Mailtide.Core.Sync;
using Microsoft.EntityFrameworkCore;

namespace Mailtide.Core;

public sealed partial class MailtideApp
{
    public async Task SyncNowAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var workGate = AccountWorkGate(accountId);
        await workGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var prepared = await TryPrepareImapAsync(
                    accountId,
                    reportStatus: true,
                    invalidateOnAuthFailure: true,
                    catchResolveFailures: true,
                    cancellationToken)
                .ConfigureAwait(false);
            if (prepared is null)
            {
                return;
            }

            // Network I/O runs outside _dbGate so other Accounts can sync in parallel.
            SetStatus(accountId, AccountStatus.Syncing());

            try
            {
                await UsingImapClientAsync(
                        prepared.Value.Endpoint,
                        prepared.Value.ProtocolSecret,
                        client => ApplyRemoteSnapshotAsync(accountId, client, cancellationToken),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await ClearTrackerAsync(CancellationToken.None).ConfigureAwait(false);
                SetStatus(accountId, AccountStatus.Idle());
                throw;
            }
            catch (Exception ex)
            {
                await ClearTrackerAsync(CancellationToken.None).ConfigureAwait(false);
                SetStatus(accountId, MapSyncError(ex));
            }
        }
        finally
        {
            workGate.Release();
        }
    }

    private async Task WaitForInboxChangeAsync(
        Guid accountId,
        string mailboxPath,
        CancellationToken cancellationToken)
    {
        var prepared = await TryPrepareImapAsync(
                accountId,
                reportStatus: false,
                invalidateOnAuthFailure: false,
                catchResolveFailures: false,
                cancellationToken)
            .ConfigureAwait(false);
        if (prepared is null)
        {
            throw new InvalidOperationException(AuthenticationFailedMessage);
        }

        await UsingImapClientAsync(
                prepared.Value.Endpoint,
                prepared.Value.ProtocolSecret,
                client => client.WaitForMailboxChangeAsync(mailboxPath, cancellationToken),
                cancellationToken,
                ImapSessionPool.Kind.Idle)
            .ConfigureAwait(false);
    }

    private async Task ApplyRemoteSnapshotAsync(
        Guid accountId,
        IImapClient client,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, KnownRemoteMailbox> known;
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            known = await _snapshotSync.LoadKnownAsync(accountId, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _dbGate.Release();
        }

        var snapshot = await RemoteSnapshotSync.FetchAsync(client, known, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<InboxArrival> arrivals = [];
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Account may have been removed while IMAP ran outside _dbGate.
            var stillPresent = await _db.Accounts
                .AsNoTracking()
                .AnyAsync(a => a.Id == accountId, cancellationToken)
                .ConfigureAwait(false);

            if (!stillPresent)
            {
                lock (_statusGate)
                {
                    _accountStatuses.Remove(accountId);
                }

                return;
            }

            arrivals = await _snapshotSync.PersistAsync(accountId, snapshot, cancellationToken)
                .ConfigureAwait(false);
            SetStatus(accountId, AccountStatus.Idle());
        }
        finally
        {
            _dbGate.Release();
        }

        RaiseInboxArrivals(arrivals);
    }
}
