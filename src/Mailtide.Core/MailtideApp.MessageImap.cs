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

    private readonly record struct MessageRelocatePrep(
        bool Unchanged,
        string SourcePath,
        string DestinationPath,
        string RemoteId,
        Guid DestinationMailboxId,
        AccountImapEndpoint Endpoint,
        string? Secret);

    private async Task RelocateMessageAsync(
        Guid accountId,
        Guid messageId,
        Func<CancellationToken, Task<MessageRelocatePrep>> prepare,
        CancellationToken cancellationToken)
    {
        var workGate = AccountWorkGate(accountId);
        await workGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            MessageRelocatePrep prep;
            await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                prep = await prepare(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _dbGate.Release();
            }

            if (prep.Unchanged)
            {
                return;
            }

            await UsingAuthenticatedImapAsync(
                    prep.Endpoint,
                    prep.Secret,
                    client => client.MoveAsync(
                        prep.SourcePath,
                        prep.DestinationPath,
                        prep.RemoteId,
                        cancellationToken),
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
                    message.MailboxId = prep.DestinationMailboxId;
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
        await RelocateMessageAsync(
                accountId,
                messageId,
                async ct =>
                {
                    var message = await RequireStoredMessageAsync(accountId, messageId, ct)
                        .ConfigureAwait(false);
                    var source = await RequireMailboxAsync(accountId, message.MailboxId, ct)
                        .ConfigureAwait(false);
                    var destination = await RequireMailboxAsync(accountId, destinationMailboxId, ct)
                        .ConfigureAwait(false);
                    if (source.Id == destination.Id)
                    {
                        return new MessageRelocatePrep(true, "", "", "", default, default, null);
                    }

                    var account = await RequireAccountAsync(accountId, ct).ConfigureAwait(false);
                    var (endpoint, secret) = await BindImapEndpointAsync(account, ct).ConfigureAwait(false);
                    return new MessageRelocatePrep(
                        false,
                        source.Path,
                        destination.Path,
                        message.RemoteId,
                        destination.Id,
                        endpoint,
                        secret);
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Guid> CopyMessageAsync(
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
            Guid destinationId;
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
                    return message.Id;
                }

                var account = await RequireAccountAsync(accountId, cancellationToken).ConfigureAwait(false);
                (endpoint, secret) = await BindImapEndpointAsync(account, cancellationToken)
                    .ConfigureAwait(false);
                sourcePath = source.Path;
                destinationPath = destination.Path;
                remoteId = message.RemoteId;
                destinationId = destination.Id;
            }
            finally
            {
                _dbGate.Release();
            }

            string? copiedRemoteId = null;
            await UsingAuthenticatedImapAsync(
                    endpoint,
                    secret,
                    async client =>
                    {
                        copiedRemoteId = await client
                            .CopyAsync(sourcePath, destinationPath, remoteId, cancellationToken)
                            .ConfigureAwait(false);
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(copiedRemoteId))
            {
                throw new ImapProtocolException("IMAP COPY did not return a destination UID.");
            }

            await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var message = await _db.Messages
                    .SingleOrDefaultAsync(
                        item => item.AccountId == accountId && item.Id == messageId,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (message is null)
                {
                    return messageId;
                }

                var copyId = Guid.NewGuid();
                _db.Messages.Add(
                    new MessageRecord
                    {
                        Id = copyId,
                        AccountId = message.AccountId,
                        MailboxId = destinationId,
                        RemoteId = copiedRemoteId,
                        Subject = message.Subject,
                        FromAddress = message.FromAddress,
                        ReceivedAt = message.ReceivedAt,
                        IsRead = message.IsRead,
                        IsFlagged = message.IsFlagged,
                        BodyText = message.BodyText,
                        BodyHtml = message.BodyHtml,
                        ToAddresses = message.ToAddresses,
                        CcAddresses = message.CcAddresses,
                        BccAddresses = message.BccAddresses,
                        ReplyToAddresses = message.ReplyToAddresses,
                        InternetMessageId = message.InternetMessageId,
                        ReferencesJson = message.ReferencesJson,
                        SizeBytes = message.SizeBytes,
                    });

                var attachments = await _db.Attachments
                    .AsNoTracking()
                    .Where(item => item.AccountId == accountId && item.MessageId == messageId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                foreach (var attachment in attachments)
                {
                    var sourceBlob = Path.Combine(_appDataDirectory, attachment.BlobRelativePath);
                    if (!File.Exists(sourceBlob))
                    {
                        continue;
                    }

                    var attachmentId = Guid.NewGuid();
                    var blobRelativePath = BlobRelativePath(accountId, attachmentId);
                    var blobPath = Path.Combine(_appDataDirectory, blobRelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(blobPath)!);
                    File.Copy(sourceBlob, blobPath, overwrite: true);
                    _db.Attachments.Add(
                        new AttachmentRecord
                        {
                            Id = attachmentId,
                            AccountId = accountId,
                            MessageId = copyId,
                            FileName = attachment.FileName,
                            ContentType = attachment.ContentType,
                            BlobRelativePath = blobRelativePath,
                            ContentId = attachment.ContentId,
                        });
                }

                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await _db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await MessageSearchIndex
                        .UpsertAsync(
                            _db,
                            copyId,
                            message.Subject,
                            message.FromAddress,
                            message.BodyText,
                            message.BodyHtml,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    await _db.Database.CloseConnectionAsync().ConfigureAwait(false);
                }

                return copyId;
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

    public Task MoveToTrashAsync(
        Guid accountId,
        Guid messageId,
        CancellationToken cancellationToken = default) =>
        RelocateToRoleAsync(
            accountId,
            messageId,
            MailboxRole.Trash,
            "This Account has no Trash Mailbox.",
            cancellationToken);

    public Task MoveToJunkAsync(
        Guid accountId,
        Guid messageId,
        CancellationToken cancellationToken = default) =>
        RelocateToRoleAsync(
            accountId,
            messageId,
            MailboxRole.Junk,
            "This Account has no Junk Mailbox.",
            cancellationToken);

    public Task MoveToArchiveAsync(
        Guid accountId,
        Guid messageId,
        CancellationToken cancellationToken = default) =>
        RelocateToRoleAsync(
            accountId,
            messageId,
            MailboxRole.Archive,
            "This Account has no Archive Mailbox.",
            cancellationToken);

    public Task RestoreFromTrashAsync(
        Guid accountId,
        Guid messageId,
        CancellationToken cancellationToken = default) =>
        RestoreFromRoleToInboxAsync(
            accountId,
            messageId,
            MailboxRole.Trash,
            "This Account has no Trash Mailbox.",
            "Message is not in Trash.",
            cancellationToken);

    public Task RestoreFromJunkAsync(
        Guid accountId,
        Guid messageId,
        CancellationToken cancellationToken = default) =>
        RestoreFromRoleToInboxAsync(
            accountId,
            messageId,
            MailboxRole.Junk,
            "This Account has no Junk Mailbox.",
            "Message is not in Junk.",
            cancellationToken);

    private Task RelocateToRoleAsync(
        Guid accountId,
        Guid messageId,
        MailboxRole role,
        string missingMailboxMessage,
        CancellationToken cancellationToken) =>
        RelocateMessageAsync(
            accountId,
            messageId,
            async ct =>
            {
                var message = await RequireStoredMessageAsync(accountId, messageId, ct)
                    .ConfigureAwait(false);
                var source = await RequireMailboxAsync(accountId, message.MailboxId, ct)
                    .ConfigureAwait(false);
                var destination = await RequireRoleMailboxAsync(
                        accountId,
                        role,
                        missingMailboxMessage,
                        ct)
                    .ConfigureAwait(false);
                if (source.Id == destination.Id)
                {
                    return new MessageRelocatePrep(true, "", "", "", default, default, null);
                }

                var account = await RequireAccountAsync(accountId, ct).ConfigureAwait(false);
                var (endpoint, secret) = await BindImapEndpointAsync(account, ct).ConfigureAwait(false);
                return new MessageRelocatePrep(
                    false,
                    source.Path,
                    destination.Path,
                    message.RemoteId,
                    destination.Id,
                    endpoint,
                    secret);
            },
            cancellationToken);

    private Task RestoreFromRoleToInboxAsync(
        Guid accountId,
        Guid messageId,
        MailboxRole role,
        string missingMailboxMessage,
        string notInRoleMessage,
        CancellationToken cancellationToken) =>
        RelocateMessageAsync(
            accountId,
            messageId,
            async ct =>
            {
                var message = await RequireStoredMessageAsync(accountId, messageId, ct)
                    .ConfigureAwait(false);
                var source = await RequireMailboxAsync(accountId, message.MailboxId, ct)
                    .ConfigureAwait(false);
                var roleMailbox = await RequireRoleMailboxAsync(
                        accountId,
                        role,
                        missingMailboxMessage,
                        ct)
                    .ConfigureAwait(false);
                if (source.Id != roleMailbox.Id)
                {
                    throw new InvalidOperationException(notInRoleMessage);
                }

                var inbox = await RequireRoleMailboxAsync(
                        accountId,
                        MailboxRole.Inbox,
                        "This Account has no Inbox Mailbox.",
                        ct)
                    .ConfigureAwait(false);
                var account = await RequireAccountAsync(accountId, ct).ConfigureAwait(false);
                var (endpoint, secret) = await BindImapEndpointAsync(account, ct).ConfigureAwait(false);
                return new MessageRelocatePrep(
                    false,
                    roleMailbox.Path,
                    inbox.Path,
                    message.RemoteId,
                    inbox.Id,
                    endpoint,
                    secret);
            },
            cancellationToken);

    public Task EmptyTrashAsync(
        Guid accountId,
        CancellationToken cancellationToken = default) =>
        EmptyRoleMailboxAsync(
            accountId,
            MailboxRole.Trash,
            "This Account has no Trash Mailbox.",
            cancellationToken);

    public Task EmptyJunkAsync(
        Guid accountId,
        CancellationToken cancellationToken = default) =>
        EmptyRoleMailboxAsync(
            accountId,
            MailboxRole.Junk,
            "This Account has no Junk Mailbox.",
            cancellationToken);

    private async Task EmptyRoleMailboxAsync(
        Guid accountId,
        MailboxRole role,
        string missingMailboxMessage,
        CancellationToken cancellationToken)
    {
        var workGate = AccountWorkGate(accountId);
        await workGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string mailboxPath;
            AccountImapEndpoint endpoint;
            string? secret;
            Guid mailboxId;

            await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var mailbox = await RequireRoleMailboxAsync(
                        accountId,
                        role,
                        missingMailboxMessage,
                        cancellationToken)
                    .ConfigureAwait(false);
                var account = await RequireAccountAsync(accountId, cancellationToken).ConfigureAwait(false);
                mailboxPath = mailbox.Path;
                mailboxId = mailbox.Id;
                (endpoint, secret) = await BindImapEndpointAsync(account, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _dbGate.Release();
            }

            await UsingAuthenticatedImapAsync(
                    endpoint,
                    secret,
                    client => client.ExpungeAllAsync(mailboxPath, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);

            await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var messages = await _db.Messages
                    .Where(m => m.AccountId == accountId && m.MailboxId == mailboxId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                await RemoveStoredMessagesAsync(accountId, messages, cancellationToken)
                    .ConfigureAwait(false);
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

    public async Task PermanentlyDeleteMessageAsync(
        Guid accountId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        var workGate = AccountWorkGate(accountId);
        await workGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string mailboxPath;
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
                var account = await RequireAccountAsync(accountId, cancellationToken).ConfigureAwait(false);
                mailboxPath = source.Path;
                remoteId = message.RemoteId;
                (endpoint, secret) = await BindImapEndpointAsync(account, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                _dbGate.Release();
            }

            await UsingAuthenticatedImapAsync(
                    endpoint,
                    secret,
                    client => client.ExpungeAsync(mailboxPath, remoteId, cancellationToken),
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
                    await RemoveStoredMessagesAsync(accountId, [message], cancellationToken)
                        .ConfigureAwait(false);
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

    private async Task RemoveStoredMessagesAsync(
        Guid accountId,
        IReadOnlyList<MessageRecord> messages,
        CancellationToken cancellationToken)
    {
        if (messages.Count == 0)
        {
            return;
        }

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
