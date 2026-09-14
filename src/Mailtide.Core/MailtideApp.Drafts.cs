using Mailtide.Core.Store;
using Microsoft.EntityFrameworkCore;

namespace Mailtide.Core;

public sealed partial class MailtideApp
{
    public async Task<DraftInfo> StartForwardAsync(
        Guid accountId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        var draft = await CreateOriginDraftAsync(
                accountId,
                messageId,
                (account, message) => NewOriginDraft(
                    accountId,
                    message,
                    to: [],
                    cc: [],
                    subject: ForwardSubject(message.Subject),
                    bodyText: MailSignature.Apply(
                        FormatForwardedBody(
                            message.FromAddress,
                            message.ReceivedAt,
                            message.Subject,
                            PlainBody(message)),
                        account.Signature,
                        beforeQuoted: true)),
                cancellationToken)
            .ConfigureAwait(false);
        await CopyMessageAttachmentsToDraftAsync(accountId, messageId, draft.Id, cancellationToken)
            .ConfigureAwait(false);
        return draft;
    }

    public async Task<DraftInfo> StartForwardAsAttachmentAsync(
        Guid accountId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        var fileName = "message.eml";
        var draft = await CreateOriginDraftAsync(
                accountId,
                messageId,
                (account, message) =>
                {
                    fileName = MessageRfc822.FileName(message.Subject);
                    return new DraftRecord
                    {
                        Id = Guid.NewGuid(),
                        AccountId = accountId,
                        ToAddresses = PackedStringList.Encode([]),
                        Subject = ForwardSubject(message.Subject),
                        BodyText = MailSignature.Apply(string.Empty, account.Signature),
                        InReplyTo = null,
                        ReferencesJson = "[]",
                        UpdatedAt = DateTimeOffset.UtcNow,
                    };
                },
                cancellationToken)
            .ConfigureAwait(false);

        using var stream = new MemoryStream();
        await WriteMessageRfc822Async(accountId, messageId, stream, cancellationToken)
            .ConfigureAwait(false);
        await AddDraftAttachmentAsync(
                accountId,
                draft.Id,
                fileName,
                "message/rfc822",
                stream.ToArray(),
                cancellationToken)
            .ConfigureAwait(false);
        return draft;
    }

    public Task<DraftInfo> StartReplyAllAsync(
        Guid accountId,
        Guid messageId,
        CancellationToken cancellationToken = default) =>
        StartReplyAllAsync(accountId, messageId, quoteBody: null, cancellationToken);

    public Task<DraftInfo> StartReplyAllAsync(
        Guid accountId,
        Guid messageId,
        string? quoteBody,
        CancellationToken cancellationToken = default) =>
        CreateOriginDraftAsync(
            accountId,
            messageId,
            (account, message) =>
            {
                var self = account.EmailAddress;
                var to = DistinctAddresses(
                    [..ReplyDestinations(account, message), ..PackedStringList.Decode(message.ToAddresses)],
                    except: [self]);
                var cc = DistinctAddresses(
                    PackedStringList.Decode(message.CcAddresses),
                    except: [self, ..to]);
                return NewOriginDraft(
                    accountId,
                    message,
                    to,
                    cc,
                    ReplySubject(message.Subject),
                    MailSignature.Apply(
                        QuoteForReply(
                            message.FromAddress,
                            message.ReceivedAt,
                            ReplyQuoteBody(message, quoteBody)),
                        account.Signature,
                        beforeQuoted: true));
            },
            cancellationToken);

    public Task<DraftInfo> StartReplyAsync(
        Guid accountId,
        Guid messageId,
        CancellationToken cancellationToken = default) =>
        StartReplyAsync(accountId, messageId, quoteBody: null, cancellationToken);

    public Task<DraftInfo> StartReplyAsync(
        Guid accountId,
        Guid messageId,
        string? quoteBody,
        CancellationToken cancellationToken = default) =>
        CreateOriginDraftAsync(
            accountId,
            messageId,
            (account, message) => NewOriginDraft(
                accountId,
                message,
                to: ReplyDestinations(account, message),
                cc: [],
                subject: ReplySubject(message.Subject),
                bodyText: MailSignature.Apply(
                    QuoteForReply(
                        message.FromAddress,
                        message.ReceivedAt,
                        ReplyQuoteBody(message, quoteBody)),
                    account.Signature,
                    beforeQuoted: true)),
            cancellationToken);

    private static string ReplyQuoteBody(MessageRecord message, string? quoteBody) =>
        string.IsNullOrWhiteSpace(quoteBody) ? PlainBody(message) : quoteBody.Trim();

    public async Task<DraftInfo> StartEditAsNewAsync(
        Guid accountId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        var draft = await CreateOriginDraftAsync(
                accountId,
                messageId,
                (_, message) => new DraftRecord
                {
                    Id = Guid.NewGuid(),
                    AccountId = accountId,
                    ToAddresses = message.ToAddresses,
                    CcAddresses = message.CcAddresses,
                    BccAddresses = message.BccAddresses,
                    Subject = message.Subject,
                    BodyText = PlainBody(message),
                    BodyHtml = message.BodyHtml,
                    InReplyTo = null,
                    ReferencesJson = "[]",
                    UpdatedAt = DateTimeOffset.UtcNow,
                },
                cancellationToken)
            .ConfigureAwait(false);
        await CopyMessageAttachmentsToDraftAsync(accountId, messageId, draft.Id, cancellationToken)
            .ConfigureAwait(false);
        return draft;
    }

    public async Task<DraftInfo> SaveDraftAsync(
        Guid accountId,
        DraftContent content,
        Guid? draftId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(content.ToAddresses);

        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RequireAccountExistsAsync(accountId, cancellationToken).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            if (draftId is { } existingId)
            {
                var existing = await _db.Drafts
                    .SingleOrDefaultAsync(
                        d => d.AccountId == accountId && d.Id == existingId,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (existing is null)
                {
                    throw new InvalidOperationException($"Draft '{existingId}' was not found.");
                }

                ApplyDraftContent(existing, content, now);
                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return ToDraftInfo(existing);
            }

            var record = new DraftRecord
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                ToAddresses = PackedStringList.Encode(content.ToAddresses),
                CcAddresses = PackedStringList.Encode(content.CcAddresses),
                BccAddresses = PackedStringList.Encode(content.BccAddresses),
                Subject = content.Subject,
                BodyText = content.BodyText,
                BodyHtml = content.BodyHtml,
                UpdatedAt = now,
            };
            _db.Drafts.Add(record);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return ToDraftInfo(record);
        }
        finally
        {
            _dbGate.Release();
        }
    }

    public async Task SendAsync(
        Guid accountId,
        Guid draftId,
        CancellationToken cancellationToken = default)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnqueueDraftLockedAsync(accountId, draftId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _dbGate.Release();
        }
    }

    private async Task<DraftInfo> CreateOriginDraftAsync(
        Guid accountId,
        Guid messageId,
        Func<AccountRecord, MessageRecord, DraftRecord> compose,
        CancellationToken cancellationToken)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var account = await RequireAccountAsync(accountId, cancellationToken).ConfigureAwait(false);
            var message = await _db.Messages
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    m => m.AccountId == accountId && m.Id == messageId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (message is null)
            {
                throw new InvalidOperationException($"Message '{messageId}' was not found.");
            }

            var record = compose(account, message);
            _db.Drafts.Add(record);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return ToDraftInfo(record);
        }
        finally
        {
            _dbGate.Release();
        }
    }

    private static DraftRecord NewOriginDraft(
        Guid accountId,
        MessageRecord message,
        IReadOnlyList<string> to,
        IReadOnlyList<string> cc,
        string subject,
        string bodyText) =>
        new()
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            ToAddresses = PackedStringList.Encode(to),
            CcAddresses = PackedStringList.Encode(cc),
            Subject = subject,
            BodyText = bodyText,
            InReplyTo = message.InternetMessageId,
            ReferencesJson = EncodeReplyReferences(message.ReferencesJson, message.InternetMessageId),
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    private static void ApplyDraftContent(DraftRecord record, DraftContent content, DateTimeOffset now)
    {
        record.ToAddresses = PackedStringList.Encode(content.ToAddresses);
        record.CcAddresses = PackedStringList.Encode(content.CcAddresses);
        record.BccAddresses = PackedStringList.Encode(content.BccAddresses);
        record.Subject = content.Subject;
        record.BodyText = content.BodyText;
        record.BodyHtml = content.BodyHtml;
        record.UpdatedAt = now;
    }

    private async Task EnqueueDraftLockedAsync(
        Guid accountId,
        Guid draftId,
        CancellationToken cancellationToken)
    {
        var draft = await _db.Drafts
            .SingleOrDefaultAsync(d => d.AccountId == accountId && d.Id == draftId, cancellationToken)
            .ConfigureAwait(false);
        if (draft is null)
        {
            throw new InvalidOperationException($"Draft '{draftId}' was not found.");
        }

        var to = PackedStringList.Decode(draft.ToAddresses);
        var cc = PackedStringList.Decode(draft.CcAddresses);
        var bcc = PackedStringList.Decode(draft.BccAddresses);
        if (to.Count == 0 && cc.Count == 0 && bcc.Count == 0)
        {
            throw new InvalidOperationException("Add at least one recipient before sending.");
        }

        var invalid = MailAddresses.Invalid([..to, ..cc, ..bcc]);
        if (invalid.Count > 0)
        {
            throw new InvalidOperationException(
                invalid.Count == 1
                    ? $"This address looks invalid: {invalid[0]}"
                    : "These addresses look invalid: " + string.Join(", ", invalid));
        }

        var now = DateTimeOffset.UtcNow;
        var outboxItemId = Guid.NewGuid();
        _db.OutboxItems.Add(new OutboxItemRecord
        {
            Id = outboxItemId,
            AccountId = accountId,
            ToAddresses = draft.ToAddresses,
            CcAddresses = draft.CcAddresses,
            BccAddresses = draft.BccAddresses,
            Subject = draft.Subject,
            BodyText = draft.BodyText,
            BodyHtml = draft.BodyHtml,
            InReplyTo = draft.InReplyTo,
            ReferencesJson = draft.ReferencesJson,
            State = OutboxItemState.Queued,
            ErrorMessage = null,
            UpdatedAt = now,
        });
        var draftAttachments = await _db.DraftAttachments
            .Where(a => a.AccountId == accountId && a.DraftId == draftId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var attachment in draftAttachments)
        {
            _db.OutboxAttachments.Add(new OutboxAttachmentRecord
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                OutboxItemId = outboxItemId,
                FileName = attachment.FileName,
                ContentType = attachment.ContentType,
                BlobRelativePath = attachment.BlobRelativePath,
            });
        }

        _db.DraftAttachments.RemoveRange(draftAttachments);
        _db.Drafts.Remove(draft);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task CopyMessageAttachmentsToDraftAsync(
        Guid accountId,
        Guid messageId,
        Guid draftId,
        CancellationToken cancellationToken)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var attachments = await _db.Attachments
                .AsNoTracking()
                .Where(a => a.AccountId == accountId && a.MessageId == messageId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (attachments.Count == 0)
            {
                return;
            }

            foreach (var attachment in attachments)
            {
                var sourcePath = Path.Combine(_appDataDirectory, attachment.BlobRelativePath);
                if (!File.Exists(sourcePath))
                {
                    continue;
                }

                var attachmentId = Guid.NewGuid();
                var blobRelativePath = BlobRelativePath(accountId, attachmentId);
                var blobPath = Path.Combine(_appDataDirectory, blobRelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(blobPath)!);
                File.Copy(sourcePath, blobPath, overwrite: true);
                _db.DraftAttachments.Add(
                    new DraftAttachmentRecord
                    {
                        Id = attachmentId,
                        AccountId = accountId,
                        DraftId = draftId,
                        FileName = attachment.FileName,
                        ContentType = attachment.ContentType,
                        BlobRelativePath = blobRelativePath,
                    });
            }

            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _dbGate.Release();
        }
    }

    private async Task RequireAccountExistsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var accountExists = await _db.Accounts
            .AsNoTracking()
            .AnyAsync(a => a.Id == accountId, cancellationToken)
            .ConfigureAwait(false);
        if (!accountExists)
        {
            throw new InvalidOperationException($"Account '{accountId}' was not found.");
        }
    }
}
