using Mailtide.Core.Imap;
using Mailtide.Core.Store;
using Microsoft.EntityFrameworkCore;

namespace Mailtide.Core;

public sealed partial class MailtideApp
{
    private readonly record struct MailboxLifecyclePrep(
        MailboxInfo? Unchanged,
        AccountImapEndpoint Endpoint,
        string? Secret);

    public async Task<MailboxInfo> CreateMailboxAsync(
        Guid accountId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        name = name.Trim();

        return await WithMailboxLifecycleAsync(
                accountId,
                async ct =>
                {
                    var account = await RequireAccountAsync(accountId, ct).ConfigureAwait(false);
                    await EnsureMailboxNameAvailableAsync(accountId, name, exceptMailboxId: null, ct)
                        .ConfigureAwait(false);
                    var (endpoint, secret) = await BindImapEndpointAsync(account, ct).ConfigureAwait(false);
                    return new MailboxLifecyclePrep(Unchanged: null, endpoint, secret);
                },
                (client, ct) => client.CreateMailboxAsync(name, ct),
                async (path, ct) =>
                {
                    var record = new MailboxRecord
                    {
                        Id = Guid.NewGuid(),
                        AccountId = accountId,
                        Name = name,
                        Path = path,
                        Role = null,
                    };
                    _db.Mailboxes.Add(record);
                    await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                    return ToMailboxInfo(record);
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<MailboxInfo> RenameMailboxAsync(
        Guid accountId,
        Guid mailboxId,
        string newName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        newName = newName.Trim();
        var oldPath = string.Empty;

        return await WithMailboxLifecycleAsync(
                accountId,
                async ct =>
                {
                    var account = await RequireAccountAsync(accountId, ct).ConfigureAwait(false);
                    var mailbox = await RequireMailboxAsync(accountId, mailboxId, ct).ConfigureAwait(false);
                    if (mailbox.Name == newName)
                    {
                        return new MailboxLifecyclePrep(ToMailboxInfo(mailbox), default, null);
                    }

                    await EnsureMailboxNameAvailableAsync(accountId, newName, mailboxId, ct)
                        .ConfigureAwait(false);
                    var (endpoint, secret) = await BindImapEndpointAsync(account, ct).ConfigureAwait(false);
                    oldPath = mailbox.Path;
                    return new MailboxLifecyclePrep(Unchanged: null, endpoint, secret);
                },
                (client, ct) => client.RenameMailboxAsync(oldPath, newName, ct),
                async (path, ct) =>
                {
                    var record = await _db.Mailboxes
                        .SingleAsync(m => m.Id == mailboxId, ct)
                        .ConfigureAwait(false);
                    record.Name = newName;
                    record.Path = path;
                    await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                    return ToMailboxInfo(record);
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<MailboxInfo> WithMailboxLifecycleAsync(
        Guid accountId,
        Func<CancellationToken, Task<MailboxLifecyclePrep>> prepare,
        Func<IImapClient, CancellationToken, Task<string>> applyRemote,
        Func<string, CancellationToken, Task<MailboxInfo>> commitLocal,
        CancellationToken cancellationToken)
    {
        var workGate = AccountWorkGate(accountId);
        await workGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            MailboxLifecyclePrep prep;
            await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                prep = await prepare(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _dbGate.Release();
            }

            if (prep.Unchanged is not null)
            {
                return prep.Unchanged;
            }

            var remotePath = string.Empty;
            await UsingAuthenticatedImapAsync(
                    prep.Endpoint,
                    prep.Secret,
                    async client =>
                    {
                        remotePath = await applyRemote(client, cancellationToken).ConfigureAwait(false);
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await commitLocal(remotePath, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _dbGate.Release();
            }
        }
        finally
        {
            workGate.Release();
        }
    }

    private async Task EnsureMailboxNameAvailableAsync(
        Guid accountId,
        string name,
        Guid? exceptMailboxId,
        CancellationToken cancellationToken)
    {
        var exists = await _db.Mailboxes
            .AsNoTracking()
            .AnyAsync(
                m => m.AccountId == accountId
                    && (exceptMailboxId == null || m.Id != exceptMailboxId)
                    && (m.Name == name || m.Path == name),
                cancellationToken)
            .ConfigureAwait(false);
        if (exists)
        {
            throw new InvalidOperationException(
                $"A Mailbox named '{name}' already exists on this Account.");
        }
    }

    private static MailboxInfo ToMailboxInfo(MailboxRecord record) =>
        new(record.Id, record.AccountId, record.Name, record.Path, record.Role);
}
