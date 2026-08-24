using Mailtide.Core.Store;
using Microsoft.EntityFrameworkCore;

namespace Mailtide.Core;

public sealed partial class MailtideApp
{
    public Task<DraftInfo> StartForwardAsync(
        Guid accountId,
        Guid messageId,
        CancellationToken cancellationToken = default) =>
        CreateOriginDraftAsync(
            accountId,
            messageId,
            (_, message) => NewOriginDraft(
                accountId,
                message,
                to: [],
                cc: [],
                subject: ForwardSubject(message.Subject),
                bodyText: FormatForwardedBody(
                    message.FromAddress,
                    message.ReceivedAt,
                    message.Subject,
                    message.BodyText)),
            cancellationToken);

    public Task<DraftInfo> StartReplyAllAsync(
        Guid accountId,
        Guid messageId,
        CancellationToken cancellationToken = default) =>
        CreateOriginDraftAsync(
            accountId,
            messageId,
            (account, message) =>
            {
                var self = account.EmailAddress;
                var to = DistinctAddresses(
                    [message.FromAddress, ..DecodeAddresses(message.ToAddresses)],
                    except: [self]);
                var cc = DistinctAddresses(
                    DecodeAddresses(message.CcAddresses),
                    except: [self, ..to]);
                return NewOriginDraft(
                    accountId,
                    message,
                    to,
                    cc,
                    ReplySubject(message.Subject),
                    QuoteForReply(message.FromAddress, message.ReceivedAt, message.BodyText));
            },
            cancellationToken);

    public Task<DraftInfo> StartReplyAsync(
        Guid accountId,
        Guid messageId,
        CancellationToken cancellationToken = default) =>
        CreateOriginDraftAsync(
            accountId,
            messageId,
            (_, message) => NewOriginDraft(
                accountId,
                message,
                to: [message.FromAddress],
                cc: [],
                subject: ReplySubject(message.Subject),
                bodyText: QuoteForReply(message.FromAddress, message.ReceivedAt, message.BodyText)),
            cancellationToken);

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
                ToAddresses = EncodeAddresses(content.ToAddresses),
                CcAddresses = EncodeAddresses(content.CcAddresses),
                BccAddresses = EncodeAddresses(content.BccAddresses),
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
            ToAddresses = EncodeAddresses(to),
            CcAddresses = EncodeAddresses(cc),
            Subject = subject,
            BodyText = bodyText,
            InReplyTo = message.InternetMessageId,
            ReferencesJson = EncodeReplyReferences(message.ReferencesJson, message.InternetMessageId),
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    private static void ApplyDraftContent(DraftRecord record, DraftContent content, DateTimeOffset now)
    {
        record.ToAddresses = EncodeAddresses(content.ToAddresses);
        record.CcAddresses = EncodeAddresses(content.CcAddresses);
        record.BccAddresses = EncodeAddresses(content.BccAddresses);
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
