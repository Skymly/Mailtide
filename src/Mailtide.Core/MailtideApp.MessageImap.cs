using Mailtide.Core.Imap;
using Mailtide.Core.Store;
using Microsoft.EntityFrameworkCore;

namespace Mailtide.Core;

public sealed partial class MailtideApp
{
    private sealed record MessageImapTarget(
        string MailboxPath,
        string RemoteId,
        AccountImapEndpoint Endpoint,
        string? Secret);

    public Task MarkReadAsync(
        Guid accountId,
        Guid messageId,
        CancellationToken cancellationToken = default) =>
        ApplyMessageImapFlagAsync(
            accountId,
            messageId,
            alreadyApplied: message => message.IsRead,
            applyLocal: message => message.IsRead = true,
            applyRemote: (client, mailboxPath, remoteId, ct) =>
                client.SetSeenAsync(mailboxPath, remoteId, ct),
            cancellationToken);

    public Task MarkUnreadAsync(
        Guid accountId,
        Guid messageId,
        CancellationToken cancellationToken = default) =>
        ApplyMessageImapFlagAsync(
            accountId,
            messageId,
            alreadyApplied: message => !message.IsRead,
            applyLocal: message => message.IsRead = false,
            applyRemote: (client, mailboxPath, remoteId, ct) =>
                client.SetUnseenAsync(mailboxPath, remoteId, ct),
            cancellationToken);

    public Task MarkFlaggedAsync(
        Guid accountId,
        Guid messageId,
        bool flagged,
        CancellationToken cancellationToken = default) =>
        ApplyMessageImapFlagAsync(
            accountId,
            messageId,
            alreadyApplied: message => message.IsFlagged == flagged,
            applyLocal: message => message.IsFlagged = flagged,
            applyRemote: (client, mailboxPath, remoteId, ct) =>
                client.SetFlaggedAsync(mailboxPath, remoteId, flagged, ct),
            cancellationToken);

    public async Task MoveMessageAsync(
        Guid accountId,
        Guid messageId,
        Guid destinationMailboxId,
        CancellationToken cancellationToken = default)
    {
        var workGate = AccountWorkGate(accountId);
        await workGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string sourcePath;
            string destinationPath;
            string remoteId;
            AccountImapEndpoint endpoint;
            string? secret;

            await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var message = await RequireStoredMessageAsync(accountId, messageId, cancellationToken)
                    .ConfigureAwait(false);
                var source = await RequireMailboxAsync(accountId, message.MailboxId, cancellationToken)
                    .ConfigureAwait(false);
                var destination = await RequireMailboxAsync(accountId, destinationMailboxId, cancellationToken)
                    .ConfigureAwait(false);
                if (source.Id == destination.Id)
                {
                    return;
                }

                var account = await RequireAccountAsync(accountId, cancellationToken).ConfigureAwait(false);
                sourcePath = source.Path;
                destinationPath = destination.Path;
                remoteId = message.RemoteId;
                (endpoint, secret) = await BindImapEndpointAsync(account, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _dbGate.Release();
            }

            await UsingAuthenticatedImapAsync(
                    endpoint,
                    secret,
                    client => client.MoveAsync(sourcePath, destinationPath, remoteId, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);

            await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var message = await _db.Messages
                    .SingleOrDefaultAsync(
                        m => m.AccountId == accountId && m.Id == messageId,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (message is not null)
                {
                    message.MailboxId = destinationMailboxId;
                    await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
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

    public async Task MoveMailboxThreadAsync(
        Guid accountId,
        Guid mailboxId,
        Guid latestMessageId,
        Guid destinationMailboxId,
        CancellationToken cancellationToken = default)
    {
        var threads = await ListMailboxThreadsAsync(accountId, mailboxId, cancellationToken)
            .ConfigureAwait(false);
        var thread = threads.FirstOrDefault(item => item.Latest.Id == latestMessageId)
            ?? threads.FirstOrDefault(item => item.Messages.Any(message => message.Id == latestMessageId))
            ?? throw new InvalidOperationException("Thread is not in the current Mailbox.");

        foreach (var message in thread.Messages)
        {
            await MoveMessageAsync(accountId, message.Id, destinationMailboxId, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task MoveToTrashAsync(
        Guid accountId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        var workGate = AccountWorkGate(accountId);
        await workGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string sourcePath;
            string trashPath;
            string remoteId;
            AccountImapEndpoint endpoint;
            string? secret;
            Guid trashMailboxId;

            await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var message = await RequireStoredMessageAsync(accountId, messageId, cancellationToken)
                    .ConfigureAwait(false);
                var source = await RequireMailboxAsync(accountId, message.MailboxId, cancellationToken)
                    .ConfigureAwait(false);
                var trash = await RequireRoleMailboxAsync(
                        accountId,
                        MailboxRole.Trash,
                        "This Account has no Trash Mailbox.",
                        cancellationToken)
                    .ConfigureAwait(false);
                if (source.Id == trash.Id)
                {
                    return;
                }

                var account = await RequireAccountAsync(accountId, cancellationToken).ConfigureAwait(false);
                sourcePath = source.Path;
                trashPath = trash.Path;
                remoteId = message.RemoteId;
                trashMailboxId = trash.Id;
                (endpoint, secret) = await BindImapEndpointAsync(account, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _dbGate.Release();
            }

            await UsingAuthenticatedImapAsync(
                    endpoint,
                    secret,
                    client => client.MoveAsync(sourcePath, trashPath, remoteId, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);

            await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var message = await _db.Messages
                    .SingleOrDefaultAsync(
                        m => m.AccountId == accountId && m.Id == messageId,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (message is not null)
                {
                    message.MailboxId = trashMailboxId;
                    await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
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

    public async Task RestoreFromTrashAsync(
        Guid accountId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        var workGate = AccountWorkGate(accountId);
        await workGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string trashPath;
            string inboxPath;
            string remoteId;
            AccountImapEndpoint endpoint;
            string? secret;
            Guid inboxMailboxId;

            await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var message = await RequireStoredMessageAsync(accountId, messageId, cancellationToken)
                    .ConfigureAwait(false);
                var source = await RequireMailboxAsync(accountId, message.MailboxId, cancellationToken)
                    .ConfigureAwait(false);
                var trash = await RequireRoleMailboxAsync(
                        accountId,
                        MailboxRole.Trash,
                        "This Account has no Trash Mailbox.",
                        cancellationToken)
                    .ConfigureAwait(false);
                if (source.Id != trash.Id)
                {
                    throw new InvalidOperationException("Message is not in Trash.");
                }

                var inbox = await RequireRoleMailboxAsync(
                        accountId,
                        MailboxRole.Inbox,
                        "This Account has no Inbox Mailbox.",
                        cancellationToken)
                    .ConfigureAwait(false);
                var account = await RequireAccountAsync(accountId, cancellationToken).ConfigureAwait(false);
                trashPath = trash.Path;
                inboxPath = inbox.Path;
                remoteId = message.RemoteId;
                inboxMailboxId = inbox.Id;
                (endpoint, secret) = await BindImapEndpointAsync(account, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _dbGate.Release();
            }

            await UsingAuthenticatedImapAsync(
                    endpoint,
                    secret,
                    client => client.MoveAsync(trashPath, inboxPath, remoteId, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);

            await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var message = await _db.Messages
                    .SingleOrDefaultAsync(
                        m => m.AccountId == accountId && m.Id == messageId,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (message is not null)
                {
                    message.MailboxId = inboxMailboxId;
                    await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
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

    public async Task EmptyTrashAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var workGate = AccountWorkGate(accountId);
        await workGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string trashPath;
            AccountImapEndpoint endpoint;
            string? secret;
            Guid trashMailboxId;

            await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var trash = await RequireRoleMailboxAsync(
                        accountId,
                        MailboxRole.Trash,
                        "This Account has no Trash Mailbox.",
                        cancellationToken)
                    .ConfigureAwait(false);
                var account = await RequireAccountAsync(accountId, cancellationToken).ConfigureAwait(false);
                trashPath = trash.Path;
                trashMailboxId = trash.Id;
                (endpoint, secret) = await BindImapEndpointAsync(account, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _dbGate.Release();
            }

            await UsingAuthenticatedImapAsync(
                    endpoint,
                    secret,
                    client => client.ExpungeAllAsync(trashPath, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);

            await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var messages = await _db.Messages
                    .Where(m => m.AccountId == accountId && m.MailboxId == trashMailboxId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                var messageIds = messages.Select(m => m.Id).ToList();
                var attachments = await _db.Attachments
                    .Where(a => a.AccountId == accountId && messageIds.Contains(a.MessageId))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                foreach (var attachment in attachments)
                {
                    var blobPath = Path.Combine(_appDataDirectory, attachment.BlobRelativePath);
                    if (File.Exists(blobPath))
                    {
                        File.Delete(blobPath);
                    }
                }

                _db.Attachments.RemoveRange(attachments);
                _db.Messages.RemoveRange(messages);
                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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

    private async Task ApplyMessageImapFlagAsync(
        Guid accountId,
        Guid messageId,
        Func<MessageRecord, bool> alreadyApplied,
        Action<MessageRecord> applyLocal,
        Func<IImapClient, string, string, CancellationToken, Task> applyRemote,
        CancellationToken cancellationToken)
    {
        MessageImapTarget? target = null;

        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var message = await RequireStoredMessageAsync(accountId, messageId, cancellationToken)
                .ConfigureAwait(false);
            if (alreadyApplied(message))
            {
                return;
            }

            var mailbox = await RequireMailboxAsync(accountId, message.MailboxId, cancellationToken)
                .ConfigureAwait(false);
            var account = await RequireAccountAsync(accountId, cancellationToken).ConfigureAwait(false);
            applyLocal(message);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var (endpoint, secret) = await BindImapEndpointAsync(account, cancellationToken)
                .ConfigureAwait(false);
            target = new MessageImapTarget(mailbox.Path, message.RemoteId, endpoint, secret);
        }
        finally
        {
            _dbGate.Release();
        }

        var protocolSecret = await ResolveImapSecretAsync(
                target.Endpoint,
                target.Secret,
                reportStatus: true,
                accountId,
                cancellationToken)
            .ConfigureAwait(false);
        if (protocolSecret is null)
        {
            return;
        }

        try
        {
            await UsingImapClientAsync(
                    target.Endpoint,
                    protocolSecret,
                    client => applyRemote(client, target.MailboxPath, target.RemoteId, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            SetStatus(accountId, MapSyncError(ex));
        }
    }

    private async Task<MessageRecord> RequireStoredMessageAsync(
        Guid accountId,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        var message = await _db.Messages
            .SingleOrDefaultAsync(
                m => m.AccountId == accountId && m.Id == messageId,
                cancellationToken)
            .ConfigureAwait(false);
        if (message is null)
        {
            throw new InvalidOperationException($"Message '{messageId}' was not found.");
        }

        return message;
    }

    private async Task<MailboxRecord> RequireMailboxAsync(
        Guid accountId,
        Guid mailboxId,
        CancellationToken cancellationToken)
    {
        var mailbox = await _db.Mailboxes
            .AsNoTracking()
            .SingleOrDefaultAsync(
                m => m.AccountId == accountId && m.Id == mailboxId,
                cancellationToken)
            .ConfigureAwait(false);
        if (mailbox is null)
        {
            throw new InvalidOperationException($"Mailbox '{mailboxId}' was not found.");
        }

        return mailbox;
    }

    private async Task<MailboxRecord> RequireRoleMailboxAsync(
        Guid accountId,
        MailboxRole role,
        string missingMessage,
        CancellationToken cancellationToken)
    {
        var mailbox = await _db.Mailboxes
            .AsNoTracking()
            .SingleOrDefaultAsync(
                m => m.AccountId == accountId && m.Role == role,
                cancellationToken)
            .ConfigureAwait(false);
        if (mailbox is null)
        {
            throw new InvalidOperationException(missingMessage);
        }

        return mailbox;
    }

    private async Task<AccountRecord> RequireAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var account = await _db.Accounts
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == accountId, cancellationToken)
            .ConfigureAwait(false);
        if (account is null)
        {
            throw new InvalidOperationException($"Account '{accountId}' was not found.");
        }

        return account;
    }
}
