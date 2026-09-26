using Mailtide.Core.Imap;
using Mailtide.Core.Store;
using Microsoft.EntityFrameworkCore;

namespace Mailtide.Core;

public sealed partial class MailtideApp
{
    public async Task FetchMessageBodyAsync(
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
                var message = await _db.Messages
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        item => item.AccountId == accountId && item.Id == messageId,
                        cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Message '{messageId}' was not found.");
                var mailbox = await _db.Mailboxes
                    .AsNoTracking()
                    .SingleOrDefaultAsync(item => item.Id == message.MailboxId, cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Mailbox was not found.");
                var account = await RequireAccountAsync(accountId, cancellationToken).ConfigureAwait(false);
                mailboxPath = mailbox.Path;
                remoteId = message.RemoteId;
                (endpoint, secret) = await BindImapEndpointAsync(account, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _dbGate.Release();
            }

            IReadOnlyList<RemoteMessage> fetched = [];
            await UsingAuthenticatedImapAsync(
                    endpoint,
                    secret,
                    async client =>
                    {
                        fetched = await client
                            .FetchMessagesAsync(mailboxPath, [remoteId], cancellationToken)
                            .ConfigureAwait(false);
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            var remote = fetched.FirstOrDefault()
                ?? throw new InvalidOperationException("The Message body could not be downloaded.");

            await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var message = await _db.Messages
                    .SingleAsync(
                        item => item.AccountId == accountId && item.Id == messageId,
                        cancellationToken)
                    .ConfigureAwait(false);
                message.BodyText = remote.BodyText;
                message.BodyHtml = remote.BodyHtml;
                message.Subject = remote.Subject;
                message.FromAddress = remote.FromAddress;
                message.ToAddresses = PackedStringList.Encode(remote.ToAddresses);
                message.CcAddresses = PackedStringList.Encode(remote.CcAddresses);
                message.BccAddresses = PackedStringList.Encode(remote.BccAddresses);
                message.ReplyToAddresses = PackedStringList.Encode(remote.ReplyToAddresses);
                message.InternetMessageId = remote.InternetMessageId;
                message.ReferencesJson = PackedStringList.Encode(remote.References);

                var hasAttachments = await _db.Attachments
                    .AnyAsync(item => item.MessageId == messageId, cancellationToken)
                    .ConfigureAwait(false);
                if (!hasAttachments)
                {
                    foreach (var remoteAttachment in remote.Attachments)
                    {
                        _db.Attachments.Add(await AttachmentBlob
                            .StoreAsync(
                                accountId,
                                messageId,
                                remoteAttachment,
                                _appDataDirectory,
                                cancellationToken)
                            .ConfigureAwait(false));
                    }
                }

                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await _db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await MessageSearchIndex
                        .UpsertAsync(
                            _db,
                            message.Id,
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
}
