using Mailtide.Core.Imap;
using Mailtide.Core.Store;
using Microsoft.EntityFrameworkCore;

namespace Mailtide.Core.Sync;

internal sealed record RemoteMailboxSnapshot(
    RemoteMailbox Mailbox,
    IReadOnlyList<RemoteMessageSummary> Summaries,
    IReadOnlyDictionary<string, RemoteMessage> FetchedByRemoteId);

internal sealed class KnownRemoteMailbox
{
    public KnownRemoteMailbox(string path, uint uidValidity, HashSet<string> remoteIds)
    {
        Path = path;
        UidValidity = uidValidity;
        RemoteIds = remoteIds;
    }

    public string Path { get; }

    public uint UidValidity { get; }

    public HashSet<string> RemoteIds { get; }
}

internal sealed class RemoteSnapshotSync
{
    private readonly MailtideDbContext _db;
    private readonly string _appDataDirectory;

    public RemoteSnapshotSync(MailtideDbContext db, string appDataDirectory)
    {
        _db = db;
        _appDataDirectory = appDataDirectory;
    }

    public async Task<IReadOnlyDictionary<string, KnownRemoteMailbox>> LoadKnownAsync(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var mailboxes = await _db.Mailboxes
            .AsNoTracking()
            .Where(m => m.AccountId == accountId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var messages = await _db.Messages
            .AsNoTracking()
            .Where(m => m.AccountId == accountId)
            .Select(m => new { m.MailboxId, m.RemoteId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byId = mailboxes.ToDictionary(m => m.Id);
        var known = new Dictionary<string, KnownRemoteMailbox>(StringComparer.Ordinal);
        foreach (var mailbox in mailboxes)
        {
            known[mailbox.Path] = new KnownRemoteMailbox(
                mailbox.Path,
                mailbox.UidValidity,
                new HashSet<string>(StringComparer.Ordinal));
        }

        foreach (var message in messages)
        {
            if (byId.TryGetValue(message.MailboxId, out var mailbox))
            {
                known[mailbox.Path].RemoteIds.Add(message.RemoteId);
            }
        }

        return known;
    }

    public static async Task<IReadOnlyList<RemoteMailboxSnapshot>> FetchAsync(
        IImapClient client,
        IReadOnlyDictionary<string, KnownRemoteMailbox> known,
        CancellationToken cancellationToken)
    {
        var remoteMailboxes = await client
            .ListMailboxesAsync(cancellationToken)
            .ConfigureAwait(false);

        var snapshot = new List<RemoteMailboxSnapshot>(remoteMailboxes.Count);
        foreach (var mailbox in remoteMailboxes)
        {
            var summaries = await client
                .FetchMessageSummariesAsync(mailbox.Path, cancellationToken)
                .ConfigureAwait(false);
            var knownIds = KnownRemoteIds(known, mailbox);
            var missing = summaries
                .Select(s => s.RemoteId)
                .Where(id => !knownIds.Contains(id))
                .ToList();
            var fetched = missing.Count == 0
                ? Array.Empty<RemoteMessage>()
                : await client
                    .FetchMessagesAsync(mailbox.Path, missing, cancellationToken)
                    .ConfigureAwait(false);
            snapshot.Add(new RemoteMailboxSnapshot(
                mailbox,
                summaries,
                fetched.ToDictionary(m => m.RemoteId, StringComparer.Ordinal)));
        }

        return snapshot;
    }


    public async Task<IReadOnlyList<InboxArrival>> PersistAsync(
        Guid accountId,
        IReadOnlyList<RemoteMailboxSnapshot> snapshot,
        CancellationToken cancellationToken)
    {
        var existingMailboxes = await _db.Mailboxes
            .Where(m => m.AccountId == accountId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var mailboxByPath = existingMailboxes.ToDictionary(m => m.Path, StringComparer.Ordinal);

        var existingMessages = await _db.Messages
            .Where(m => m.AccountId == accountId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var existingAttachments = await _db.Attachments
            .Where(a => a.AccountId == accountId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var existingInboxMailboxIds = existingMailboxes
            .Where(m => m.Role == MailboxRole.Inbox)
            .Select(m => m.Id)
            .ToHashSet();
        var accountDisplayName = await _db.Accounts
            .AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => a.DisplayName)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
        var arrivals = new List<InboxArrival>();
        var seenMailboxIds = new HashSet<Guid>();
        var seenMessageIds = new HashSet<Guid>();
        var searchUpserts = new List<MessageRecord>();

        foreach (var entry in snapshot)
        {
            if (!mailboxByPath.TryGetValue(entry.Mailbox.Path, out var mailbox))
            {
                mailbox = new MailboxRecord
                {
                    Id = Guid.NewGuid(),
                    AccountId = accountId,
                    Name = entry.Mailbox.Name,
                    Path = entry.Mailbox.Path,
                    Role = entry.Mailbox.Role,
                    UidValidity = entry.Mailbox.UidValidity,
                };
                _db.Mailboxes.Add(mailbox);
                mailboxByPath[entry.Mailbox.Path] = mailbox;
            }
            else
            {
                if (UidValidityChanged(mailbox.UidValidity, entry.Mailbox.UidValidity))
                {
                    InvalidateMailboxMessages(
                        mailbox.Id,
                        existingMessages,
                        existingAttachments);
                }

                mailbox.Name = entry.Mailbox.Name;
                mailbox.Role = entry.Mailbox.Role;
                if (entry.Mailbox.UidValidity != 0)
                {
                    mailbox.UidValidity = entry.Mailbox.UidValidity;
                }
            }

            seenMailboxIds.Add(mailbox.Id);

            var messagesByRemote = existingMessages
                .Where(m => m.MailboxId == mailbox.Id)
                .ToDictionary(m => m.RemoteId, StringComparer.Ordinal);

            foreach (var summary in entry.Summaries)
            {
                entry.FetchedByRemoteId.TryGetValue(summary.RemoteId, out var fetched);
                if (!messagesByRemote.TryGetValue(summary.RemoteId, out var message))
                {
                    if (fetched is null)
                    {
                        continue;
                    }

                    var messageId = Guid.NewGuid();
                    message = new MessageRecord
                    {
                        Id = messageId,
                        AccountId = accountId,
                        MailboxId = mailbox.Id,
                        RemoteId = fetched.RemoteId,
                        Subject = fetched.Subject,
                        FromAddress = fetched.FromAddress,
                        ReceivedAt = fetched.ReceivedAt,
                        IsRead = summary.IsRead,
                        IsFlagged = summary.IsFlagged,
                        BodyText = fetched.BodyText,
                        BodyHtml = fetched.BodyHtml,
                        InternetMessageId = fetched.InternetMessageId,
                        ReferencesJson = PackedStringList.Encode(fetched.References),
                        ToAddresses = PackedStringList.Encode(fetched.ToAddresses),
                        CcAddresses = PackedStringList.Encode(fetched.CcAddresses),
                        BccAddresses = PackedStringList.Encode(fetched.BccAddresses),
                        ReplyToAddresses = PackedStringList.Encode(fetched.ReplyToAddresses),
                        SizeBytes = fetched.SizeBytes != 0 ? fetched.SizeBytes : summary.SizeBytes,
                    };
                    _db.Messages.Add(message);
                    existingMessages.Add(message);
                    if (mailbox.Role == MailboxRole.Inbox
                        && existingInboxMailboxIds.Contains(mailbox.Id)
                        && !summary.IsRead)
                    {
                        arrivals.Add(new InboxArrival(
                            message.Id,
                            accountId,
                            mailbox.Id,
                            message.Subject,
                            message.FromAddress,
                            accountDisplayName));
                    }

                    foreach (var remoteAttachment in fetched.Attachments)
                    {
                        var attachmentId = Guid.NewGuid();
                        var blobRelativePath = BlobRelativePath(accountId, attachmentId);
                        var blobAbsolutePath = Path.Combine(_appDataDirectory, blobRelativePath);
                        Directory.CreateDirectory(Path.GetDirectoryName(blobAbsolutePath)!);
                        await File
                            .WriteAllBytesAsync(blobAbsolutePath, remoteAttachment.Content, cancellationToken)
                            .ConfigureAwait(false);

                        _db.Attachments.Add(new AttachmentRecord
                        {
                            Id = attachmentId,
                            AccountId = accountId,
                            MessageId = messageId,
                            FileName = remoteAttachment.FileName,
                            ContentType = remoteAttachment.ContentType,
                            BlobRelativePath = blobRelativePath,
                            ContentId = remoteAttachment.ContentId,
                        });
                    }
                }
                else
                {
                    message.IsRead = summary.IsRead;
                    message.IsFlagged = summary.IsFlagged;
                    message.Subject = summary.Subject;
                    message.FromAddress = summary.FromAddress;
                    message.ReceivedAt = summary.ReceivedAt;
                    message.SizeBytes = fetched is { SizeBytes: not 0 } ? fetched.SizeBytes : summary.SizeBytes;
                    if (fetched is not null)
                    {
                        message.BodyText = fetched.BodyText;
                        message.BodyHtml = fetched.BodyHtml;
                        message.InternetMessageId = fetched.InternetMessageId;
                        message.ReferencesJson = PackedStringList.Encode(fetched.References);
                        message.ToAddresses = PackedStringList.Encode(fetched.ToAddresses);
                        message.CcAddresses = PackedStringList.Encode(fetched.CcAddresses);
                        message.BccAddresses = PackedStringList.Encode(fetched.BccAddresses);
                        message.ReplyToAddresses = PackedStringList.Encode(fetched.ReplyToAddresses);
                    }
                }

                seenMessageIds.Add(message.Id);
                searchUpserts.Add(message);
            }
        }

        var messagesToRemove = existingMessages
            .Where(m => !seenMessageIds.Contains(m.Id))
            .ToList();
        var removedMessageIds = messagesToRemove.Select(m => m.Id).ToHashSet();
        var attachmentsToRemove = existingAttachments
            .Where(a => removedMessageIds.Contains(a.MessageId))
            .ToList();

        foreach (var attachment in attachmentsToRemove)
        {
            var blobAbsolutePath = Path.Combine(_appDataDirectory, attachment.BlobRelativePath);
            if (File.Exists(blobAbsolutePath))
            {
                File.Delete(blobAbsolutePath);
            }
        }

        _db.Attachments.RemoveRange(attachmentsToRemove);
        _db.Messages.RemoveRange(messagesToRemove);
        _db.Mailboxes.RemoveRange(existingMailboxes.Where(m => !seenMailboxIds.Contains(m.Id)));

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var removed in messagesToRemove)
            {
                await MessageSearchIndex
                    .DeleteAsync(_db, removed.Id, cancellationToken)
                    .ConfigureAwait(false);
            }

            foreach (var message in searchUpserts)
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
        }
        finally
        {
            await _db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }

        return arrivals;
    }

    private void InvalidateMailboxMessages(
        Guid mailboxId,
        List<MessageRecord> existingMessages,
        List<AttachmentRecord> existingAttachments)
    {
        var doomed = existingMessages.Where(m => m.MailboxId == mailboxId).ToList();
        var doomedIds = doomed.Select(m => m.Id).ToHashSet();
        var attachments = existingAttachments.Where(a => doomedIds.Contains(a.MessageId)).ToList();
        foreach (var attachment in attachments)
        {
            var blobAbsolutePath = Path.Combine(_appDataDirectory, attachment.BlobRelativePath);
            if (File.Exists(blobAbsolutePath))
            {
                File.Delete(blobAbsolutePath);
            }
        }

        _db.Attachments.RemoveRange(attachments);
        _db.Messages.RemoveRange(doomed);
        existingAttachments.RemoveAll(a => doomedIds.Contains(a.MessageId));
        existingMessages.RemoveAll(m => m.MailboxId == mailboxId);
    }

    public static bool UidValidityChanged(uint stored, uint remote) =>
        stored != 0 && remote != 0 && stored != remote;

    private static IReadOnlySet<string> KnownRemoteIds(
        IReadOnlyDictionary<string, KnownRemoteMailbox> known,
        RemoteMailbox mailbox)
    {
        if (!known.TryGetValue(mailbox.Path, out var local))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        if (UidValidityChanged(local.UidValidity, mailbox.UidValidity))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return local.RemoteIds;
    }

    private static string BlobRelativePath(Guid accountId, Guid attachmentId) =>
        Path.Combine("accounts", accountId.ToString("D"), "blobs", attachmentId.ToString("D"));
}
