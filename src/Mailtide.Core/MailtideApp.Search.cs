using Mailtide.Core.Store;
using Microsoft.EntityFrameworkCore;

namespace Mailtide.Core;

public sealed partial class MailtideApp
{
    public Task<IReadOnlyList<MessageInfo>> SearchMessagesAsync(
        Guid accountId,
        Guid mailboxId,
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return ListMessagesAsync(accountId, mailboxId, cancellationToken);
        }

        return SearchMailboxAsync(accountId, mailboxId, query, cancellationToken);
    }

    public Task<IReadOnlyList<MessageInfo>> SearchUnifiedInboxAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return ListUnifiedInboxAsync(cancellationToken);
        }

        return SearchUnifiedAsync(query, cancellationToken);
    }

    private async Task<IReadOnlyList<MessageInfo>> SearchMailboxAsync(
        Guid accountId,
        Guid mailboxId,
        string query,
        CancellationToken cancellationToken)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var clauses = MessageSearch.OrClauses(query);
            if (clauses.Count > 1)
            {
                var seen = new HashSet<Guid>();
                var merged = new List<MessageInfo>();
                foreach (var clause in clauses)
                {
                    foreach (var item in await SearchMailboxClauseAsync(
                                 accountId, mailboxId, clause, cancellationToken)
                                 .ConfigureAwait(false))
                    {
                        if (seen.Add(item.Id))
                        {
                            merged.Add(item);
                        }
                    }
                }

                return merged
                    .OrderByDescending(item => item.ReceivedAt)
                    .ThenBy(item => item.Subject)
                    .ToList();
            }

            return await SearchMailboxClauseAsync(accountId, mailboxId, query, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _dbGate.Release();
        }
    }

    private async Task<IReadOnlyList<MessageInfo>> SearchMailboxClauseAsync(
        Guid accountId,
        Guid mailboxId,
        string query,
        CancellationToken cancellationToken)
    {
        var parsed = MessageSearch.Parse(query);
        var scoped = _db.Messages.AsNoTracking().Where(m => m.AccountId == accountId);
        if (!parsed.InAnywhere)
        {
            if (!string.IsNullOrEmpty(parsed.InMailboxContains) || parsed.InRole is not null)
            {
                var mailboxIds = await MailboxIdsMatchingAsync(accountId, parsed, cancellationToken)
                    .ConfigureAwait(false);
                if (mailboxIds.Count == 0)
                {
                    return [];
                }

                scoped = scoped.Where(m => mailboxIds.Contains(m.MailboxId));
            }
            else
            {
                scoped = scoped.Where(m => m.MailboxId == mailboxId);
            }
        }

        return await ExecuteSearchAsync(scoped, query, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<MessageInfo>> SearchUnifiedAsync(
        string query,
        CancellationToken cancellationToken)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var clauses = MessageSearch.OrClauses(query);
            if (clauses.Count > 1)
            {
                var seen = new HashSet<Guid>();
                var merged = new List<MessageInfo>();
                foreach (var clause in clauses)
                {
                    foreach (var item in await SearchUnifiedClauseAsync(clause, cancellationToken)
                                 .ConfigureAwait(false))
                    {
                        if (seen.Add(item.Id))
                        {
                            merged.Add(item);
                        }
                    }
                }

                return merged
                    .OrderByDescending(item => item.ReceivedAt)
                    .ThenBy(item => item.Subject)
                    .ToList();
            }

            return await SearchUnifiedClauseAsync(query, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _dbGate.Release();
        }
    }

    private async Task<IReadOnlyList<MessageInfo>> SearchUnifiedClauseAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var parsed = MessageSearch.Parse(query);
        IQueryable<MessageRecord> scoped;
        if (parsed.InAnywhere)
        {
            scoped = _db.Messages.AsNoTracking();
        }
        else
        {
            var mailboxIds = await MailboxIdsMatchingAsync(accountId: null, parsed, cancellationToken)
                .ConfigureAwait(false);
            if (mailboxIds.Count == 0)
            {
                return [];
            }

            scoped = _db.Messages
                .AsNoTracking()
                .Where(m => mailboxIds.Contains(m.MailboxId));
        }

        return await ExecuteSearchAsync(scoped, query, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<Guid>> MailboxIdsMatchingAsync(
        Guid? accountId,
        MessageSearch.Parsed parsed,
        CancellationToken cancellationToken)
    {
        var query = _db.Mailboxes.AsNoTracking();
        if (accountId is { } id)
        {
            query = query.Where(mailbox => mailbox.AccountId == id);
        }

        if (string.IsNullOrEmpty(parsed.InMailboxContains))
        {
            var role = parsed.InRole ?? MailboxRole.Inbox;
            return await query
                .Where(mailbox => mailbox.Role == role)
                .Select(mailbox => mailbox.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var listed = await query
            .Select(mailbox => new { mailbox.Id, mailbox.Name, mailbox.Path })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return listed
            .Where(mailbox => MessageSearch.MailboxMatchesIn(mailbox.Name, mailbox.Path, parsed.InMailboxContains))
            .Select(mailbox => mailbox.Id)
            .ToList();
    }

    private async Task<IReadOnlyList<MessageInfo>> ExecuteSearchAsync(
        IQueryable<MessageRecord> scoped,
        string query,
        CancellationToken cancellationToken)
    {
        var clauses = MessageSearch.OrClauses(query);
        if (clauses.Count > 1)
        {
            var seen = new HashSet<Guid>();
            var merged = new List<MessageInfo>();
            foreach (var clause in clauses)
            {
                foreach (var item in await ExecuteSearchAsync(scoped, clause, cancellationToken)
                             .ConfigureAwait(false))
                {
                    if (seen.Add(item.Id))
                    {
                        merged.Add(item);
                    }
                }
            }

            return merged
                .OrderByDescending(item => item.ReceivedAt)
                .ThenBy(item => item.Subject)
                .ToList();
        }

        var parsed = MessageSearch.Parse(query);
        if (parsed.FlaggedOnly)
        {
            scoped = scoped.Where(m => m.IsFlagged);
        }

        if (parsed.NotFlagged)
        {
            scoped = scoped.Where(m => !m.IsFlagged);
        }

        if (parsed.UnreadOnly)
        {
            scoped = scoped.Where(m => !m.IsRead);
        }

        if (parsed.ReadOnly)
        {
            scoped = scoped.Where(m => m.IsRead);
        }

        if (!string.IsNullOrEmpty(parsed.FromContains))
        {
            var fromNeedle = parsed.FromContains.ToLowerInvariant();
            scoped = scoped.Where(m => m.FromAddress.ToLower().Contains(fromNeedle));
        }

        if (!string.IsNullOrEmpty(parsed.ToContains))
        {
            var toNeedle = parsed.ToContains.ToLowerInvariant();
            scoped = scoped.Where(m => m.ToAddresses.ToLower().Contains(toNeedle));
        }

        if (!string.IsNullOrEmpty(parsed.CcContains))
        {
            var ccNeedle = parsed.CcContains.ToLowerInvariant();
            scoped = scoped.Where(m => m.CcAddresses.ToLower().Contains(ccNeedle));
        }

        if (!string.IsNullOrEmpty(parsed.BccContains))
        {
            var bccNeedle = parsed.BccContains.ToLowerInvariant();
            scoped = scoped.Where(m => m.BccAddresses.ToLower().Contains(bccNeedle));
        }

        if (!string.IsNullOrEmpty(parsed.SubjectContains))
        {
            var subjectNeedle = parsed.SubjectContains.ToLowerInvariant();
            scoped = scoped.Where(m => m.Subject.ToLower().Contains(subjectNeedle));
        }

        if (parsed.AttachmentOnly)
        {
            var attachmentIds = await AttachmentMessageIdsContainingAsync(
                    null,
                    cancellationToken)
                .ConfigureAwait(false);
            scoped = scoped.Where(m => attachmentIds.Contains(m.Id));
        }

        if (!string.IsNullOrEmpty(parsed.FilenameContains))
        {
            var filenameIds = await AttachmentMessageIdsContainingAsync(
                    parsed.FilenameContains,
                    cancellationToken)
                .ConfigureAwait(false);
            scoped = scoped.Where(m => filenameIds.Contains(m.Id));
        }

        if (!string.IsNullOrEmpty(parsed.FromExclude))
        {
            var fromExclude = parsed.FromExclude.ToLowerInvariant();
            scoped = scoped.Where(m => !m.FromAddress.ToLower().Contains(fromExclude));
        }

        if (!string.IsNullOrEmpty(parsed.ToExclude))
        {
            var toExclude = parsed.ToExclude.ToLowerInvariant();
            scoped = scoped.Where(m => !m.ToAddresses.ToLower().Contains(toExclude));
        }

        if (!string.IsNullOrEmpty(parsed.CcExclude))
        {
            var ccExclude = parsed.CcExclude.ToLowerInvariant();
            scoped = scoped.Where(m => !m.CcAddresses.ToLower().Contains(ccExclude));
        }

        if (!string.IsNullOrEmpty(parsed.BccExclude))
        {
            var bccExclude = parsed.BccExclude.ToLowerInvariant();
            scoped = scoped.Where(m => !m.BccAddresses.ToLower().Contains(bccExclude));
        }

        if (!string.IsNullOrEmpty(parsed.SubjectExclude))
        {
            var subjectExclude = parsed.SubjectExclude.ToLowerInvariant();
            scoped = scoped.Where(m => !m.Subject.ToLower().Contains(subjectExclude));
        }

        if (parsed.WithoutAttachment)
        {
            var attachmentIds = await AttachmentMessageIdsContainingAsync(null, cancellationToken)
                .ConfigureAwait(false);
            scoped = scoped.Where(m => !attachmentIds.Contains(m.Id));
        }

        if (!string.IsNullOrEmpty(parsed.FilenameExclude))
        {
            var excludedIds = await AttachmentMessageIdsContainingAsync(
                    parsed.FilenameExclude,
                    cancellationToken)
                .ConfigureAwait(false);
            scoped = scoped.Where(m => !excludedIds.Contains(m.Id));
        }

        if (parsed.LargerThan is { } largerThan)
        {
            scoped = scoped.Where(m => m.SizeBytes > largerThan);
        }

        if (parsed.SmallerThan is { } smallerThan)
        {
            scoped = scoped.Where(m => m.SizeBytes < smallerThan);
        }

        if (parsed.TextExcludes is { Count: > 0 } textExcludes)
        {
            foreach (var exclude in textExcludes)
            {
                var needle = exclude.ToLowerInvariant();
                scoped = scoped.Where(m =>
                    !m.Subject.ToLower().Contains(needle)
                    && !m.FromAddress.ToLower().Contains(needle)
                    && !m.BodyText.ToLower().Contains(needle));
            }
        }

        if (!string.IsNullOrEmpty(parsed.Text))
        {
            var needle = parsed.Text.ToLowerInvariant();
            IReadOnlyList<Guid> ftsIds = [];
            await _db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ftsIds = await MessageSearchIndex.SearchIdsAsync(_db, parsed.Text, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                await _db.Database.CloseConnectionAsync().ConfigureAwait(false);
            }

            var filenameIds = await AttachmentMessageIdsContainingAsync(
                    parsed.Text,
                    cancellationToken)
                .ConfigureAwait(false);

            scoped = scoped.Where(m =>
                ftsIds.Contains(m.Id)
                || filenameIds.Contains(m.Id)
                || m.Subject.ToLower().Contains(needle)
                || m.FromAddress.ToLower().Contains(needle)
                || m.BodyText.ToLower().Contains(needle));
        }

        var records = await scoped
            .Select(m => new MessageRecord
            {
                Id = m.Id,
                AccountId = m.AccountId,
                MailboxId = m.MailboxId,
                RemoteId = m.RemoteId,
                Subject = m.Subject,
                FromAddress = m.FromAddress,
                ReceivedAt = m.ReceivedAt,
                IsRead = m.IsRead,
                IsFlagged = m.IsFlagged,
                BodyText = m.BodyText,
                ToAddresses = m.ToAddresses,
                CcAddresses = m.CcAddresses,
                BccAddresses = m.BccAddresses,
                ReplyToAddresses = m.ReplyToAddresses,
                InternetMessageId = m.InternetMessageId,
                SizeBytes = m.SizeBytes,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (parsed.After is { } after)
        {
            records = records.Where(m => m.ReceivedAt >= after).ToList();
        }

        if (parsed.Before is { } before)
        {
            records = records.Where(m => m.ReceivedAt < before).ToList();
        }

        if (!string.IsNullOrEmpty(parsed.MessageIdContains))
        {
            records = records
                .Where(m => MessageSearch.MatchesMessageId(m.InternetMessageId, parsed.MessageIdContains))
                .ToList();
        }

        if (parsed.FromMe || parsed.ToMe)
        {
            var self = await SelfMailboxAddressesAsync(cancellationToken).ConfigureAwait(false);
            if (parsed.FromMe)
            {
                records = records
                    .Where(m => MessageSearch.ContainsSelf(m.FromAddress, self))
                    .ToList();
            }

            if (parsed.ToMe)
            {
                records = records
                    .Where(m => MessageSearch.ContainsSelf(m.ToAddresses, self)
                        || MessageSearch.ContainsSelf(m.CcAddresses, self)
                        || MessageSearch.ContainsSelf(m.BccAddresses, self))
                    .ToList();
            }
        }

        var attachments = await AttachmentMessageIdsAsync(records, cancellationToken)
            .ConfigureAwait(false);
        return records
            .OrderByDescending(m => m.ReceivedAt)
            .ThenBy(m => m.Subject)
            .Select(record =>
            {
                var info = ToMessageInfo(record, attachments);
                if (string.IsNullOrWhiteSpace(parsed.Text))
                {
                    return info;
                }

                var body = string.IsNullOrWhiteSpace(record.BodyText)
                    ? HtmlText.Strip(record.BodyHtml)
                    : record.BodyText;
                return info with { Preview = MessagePreview.FromBodyText(body, parsed.Text) };
            })
            .ToList();
    }

    private async Task<IReadOnlyList<string>> SelfMailboxAddressesAsync(CancellationToken cancellationToken)
    {
        var emails = await _db.Accounts
            .AsNoTracking()
            .Select(account => account.EmailAddress)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var addresses = new List<string>();
        foreach (var email in emails)
        {
            if (MailAddresses.TryGetMailbox(email, out var address, out _))
            {
                addresses.Add(address);
            }
            else if (!string.IsNullOrWhiteSpace(email))
            {
                addresses.Add(email.Trim());
            }
        }

        return addresses;
    }

    private async Task<IReadOnlyList<Guid>> AttachmentMessageIdsContainingAsync(
        string? fileNameContains,
        CancellationToken cancellationToken)
    {
        var query = _db.Attachments.AsNoTracking();
        if (!string.IsNullOrEmpty(fileNameContains))
        {
            var needle = fileNameContains.ToLowerInvariant();
            query = query.Where(attachment => attachment.FileName.ToLower().Contains(needle));
        }

        return await query
            .Select(attachment => attachment.MessageId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
