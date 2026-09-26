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
            .AsNoTracking()
            .Where(m => m.AccountId == accountId)
            .Select(m => new KnownMessageRow
            {
                Id = m.Id,
                MailboxId = m.MailboxId,
                RemoteId = m.RemoteId,
                Subject = m.Subject,
                FromAddress = m.FromAddress,
                ReceivedAt = m.ReceivedAt,
                IsRead = m.IsRead,
                IsFlagged = m.IsFlagged,
                SizeBytes = m.SizeBytes,
            })
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
        var envelopeUpdates = new List<(Guid Id, string Subject, string FromAddress)>();

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
                    // 0 means STATUS/SELECT did not report an epoch — not a real UIDVALIDITY.
                    UidValidity = entry.Mailbox.UidValidity,
                };
                _db.Mailboxes.Add(mailbox);
                mailboxByPath[entry.Mailbox.Path] = mailbox;
            }
            else
            {
                var hasLocalMapping = existingMessages.Exists(m => m.MailboxId == mailbox.Id);
                if (UidValidityChanged(
                    mailbox.UidValidity,
                    entry.Mailbox.UidValidity,
                    hasLocalMapping))
                {
                    await InvalidateMailboxMessagesAsync(
                            mailbox.Id,
                            existingMessages,
                            existingAttachments,
                            cancellationToken)
                        .ConfigureAwait(false);
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
                Guid seenId;
                if (!messagesByRemote.TryGetValue(summary.RemoteId, out var known))
                {
                    if (fetched is null)
                    {
                        continue;
                    }

                    var messageId = Guid.NewGuid();
                    var inserted = new MessageRecord
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
                    _db.Messages.Add(inserted);
                    existingMessages.Add(new KnownMessageRow
                    {
                        Id = inserted.Id,
                        MailboxId = inserted.MailboxId,
                        RemoteId = inserted.RemoteId,
                        Subject = inserted.Subject,
                        FromAddress = inserted.FromAddress,
                        ReceivedAt = inserted.ReceivedAt,
                        IsRead = inserted.IsRead,
                        IsFlagged = inserted.IsFlagged,
                        SizeBytes = inserted.SizeBytes,
                    });
                    searchUpserts.Add(inserted);
                    if (mailbox.Role == MailboxRole.Inbox
                        && existingInboxMailboxIds.Contains(mailbox.Id)
                        && !summary.IsRead)
                    {
                        arrivals.Add(new InboxArrival(
                            inserted.Id,
                            accountId,
                            mailbox.Id,
                            inserted.Subject,
                            inserted.FromAddress,
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

                    seenId = inserted.Id;
                }
                else
                {
                    var sizeBytes = fetched is { SizeBytes: not 0 } ? fetched.SizeBytes : summary.SizeBytes;
                    var envelopeChanged = !string.Equals(known.Subject, summary.Subject, StringComparison.Ordinal)
                        || !string.Equals(known.FromAddress, summary.FromAddress, StringComparison.Ordinal);
                    if (fetched is null)
                    {
                        await _db.Messages
                            .Where(m => m.Id == known.Id)
                            .ExecuteUpdateAsync(
                                setters => setters
                                    .SetProperty(m => m.IsRead, summary.IsRead)
                                    .SetProperty(m => m.IsFlagged, summary.IsFlagged)
                                    .SetProperty(m => m.Subject, summary.Subject)
                                    .SetProperty(m => m.FromAddress, summary.FromAddress)
                                    .SetProperty(m => m.ReceivedAt, summary.ReceivedAt)
                                    .SetProperty(m => m.SizeBytes, sizeBytes),
                                cancellationToken)
                            .ConfigureAwait(false);
                        if (envelopeChanged)
                        {
                            envelopeUpdates.Add((known.Id, summary.Subject, summary.FromAddress));
                        }
                    }
                    else
                    {
                        await _db.Messages
                            .Where(m => m.Id == known.Id)
                            .ExecuteUpdateAsync(
                                setters => setters
                                    .SetProperty(m => m.IsRead, summary.IsRead)
                                    .SetProperty(m => m.IsFlagged, summary.IsFlagged)
                                    .SetProperty(m => m.Subject, summary.Subject)
                                    .SetProperty(m => m.FromAddress, summary.FromAddress)
                                    .SetProperty(m => m.ReceivedAt, summary.ReceivedAt)
                                    .SetProperty(m => m.SizeBytes, sizeBytes)
                                    .SetProperty(m => m.BodyText, fetched.BodyText)
                                    .SetProperty(m => m.BodyHtml, fetched.BodyHtml)
                                    .SetProperty(m => m.InternetMessageId, fetched.InternetMessageId)
                                    .SetProperty(m => m.ReferencesJson, PackedStringList.Encode(fetched.References))
                                    .SetProperty(m => m.ToAddresses, PackedStringList.Encode(fetched.ToAddresses))
                                    .SetProperty(m => m.CcAddresses, PackedStringList.Encode(fetched.CcAddresses))
                                    .SetProperty(m => m.BccAddresses, PackedStringList.Encode(fetched.BccAddresses))
                                    .SetProperty(m => m.ReplyToAddresses, PackedStringList.Encode(fetched.ReplyToAddresses)),
                                cancellationToken)
                            .ConfigureAwait(false);
                        searchUpserts.Add(new MessageRecord
                        {
                            Id = known.Id,
                            AccountId = accountId,
                            MailboxId = known.MailboxId,
                            RemoteId = known.RemoteId,
                            Subject = summary.Subject,
                            FromAddress = summary.FromAddress,
                            ReceivedAt = summary.ReceivedAt,
                            IsRead = summary.IsRead,
                            IsFlagged = summary.IsFlagged,
                            BodyText = fetched.BodyText,
                            BodyHtml = fetched.BodyHtml,
                        });
                    }

                    seenId = known.Id;
                }

                seenMessageIds.Add(seenId);
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
        if (removedMessageIds.Count > 0)
        {
            await _db.Messages
                .Where(m => removedMessageIds.Contains(m.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

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

            foreach (var update in envelopeUpdates)
            {
                await MessageSearchIndex
                    .UpdateEnvelopeAsync(
                        _db,
                        update.Id,
                        update.Subject,
                        update.FromAddress,
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

    private async Task InvalidateMailboxMessagesAsync(
        Guid mailboxId,
        List<KnownMessageRow> existingMessages,
        List<AttachmentRecord> existingAttachments,
        CancellationToken cancellationToken)
    {
        var doomedIds = existingMessages
            .Where(m => m.MailboxId == mailboxId)
            .Select(m => m.Id)
            .ToHashSet();
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
        if (doomedIds.Count > 0)
        {
            await _db.Messages
                .Where(m => doomedIds.Contains(m.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        existingAttachments.RemoveAll(a => doomedIds.Contains(a.MessageId));
        existingMessages.RemoveAll(m => m.MailboxId == mailboxId);
    }

    public static bool UidValidityChanged(
        uint stored,
        uint remote,
        bool hasLocalMapping = false)
    {
        if (remote == 0 || stored == 0)
        {
            // Unknown epoch is not a confirmed UIDVALIDITY. Reusing RemoteIds while
            // STATUS fails (0, 0) or when 0 later becomes a real epoch would keep
            // old bodies under new envelopes.
            return hasLocalMapping;
        }

        return stored != remote;
    }

    private static IReadOnlySet<string> KnownRemoteIds(
        IReadOnlyDictionary<string, KnownRemoteMailbox> known,
        RemoteMailbox mailbox)
    {
        if (!known.TryGetValue(mailbox.Path, out var local))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        if (UidValidityChanged(
            local.UidValidity,
            mailbox.UidValidity,
            local.RemoteIds.Count > 0))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return local.RemoteIds;
    }

    private sealed class KnownMessageRow
    {
        public Guid Id { get; set; }

        public Guid MailboxId { get; set; }

        public string RemoteId { get; set; } = string.Empty;

        public string Subject { get; set; } = string.Empty;

        public string FromAddress { get; set; } = string.Empty;

        public DateTimeOffset ReceivedAt { get; set; }

        public bool IsRead { get; set; }

        public bool IsFlagged { get; set; }

        public long SizeBytes { get; set; }
    }

    private static string BlobRelativePath(Guid accountId, Guid attachmentId) =>
        Path.Combine("accounts", accountId.ToString("D"), "blobs", attachmentId.ToString("D"));
}
