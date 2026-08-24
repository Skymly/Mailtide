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
            var scoped = _db.Messages
                .AsNoTracking()
                .Where(m => m.AccountId == accountId && m.MailboxId == mailboxId);
            return await ExecuteSearchAsync(scoped, query, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _dbGate.Release();
        }
    }

    private async Task<IReadOnlyList<MessageInfo>> SearchUnifiedAsync(
        string query,
        CancellationToken cancellationToken)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var inboxMailboxIds = await _db.Mailboxes
                .AsNoTracking()
                .Where(m => m.Role == MailboxRole.Inbox)
                .Select(m => m.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (inboxMailboxIds.Count == 0)
            {
                return [];
            }

            var scoped = _db.Messages
                .AsNoTracking()
                .Where(m => inboxMailboxIds.Contains(m.MailboxId));
            return await ExecuteSearchAsync(scoped, query, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _dbGate.Release();
        }
    }

    private async Task<IReadOnlyList<MessageInfo>> ExecuteSearchAsync(
        IQueryable<MessageRecord> scoped,
        string query,
        CancellationToken cancellationToken)
    {
        var parsed = MessageSearch.Parse(query);
        if (parsed.FlaggedOnly)
        {
            scoped = scoped.Where(m => m.IsFlagged);
        }

        if (parsed.UnreadOnly)
        {
            scoped = scoped.Where(m => !m.IsRead);
        }

        var records = await scoped.ToListAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(parsed.Text))
        {
            return records
                .OrderByDescending(m => m.ReceivedAt)
                .ThenBy(m => m.Subject)
                .Select(ToMessageInfo)
                .ToList();
        }

        return FilterMessages(records, query);
    }

    private static IReadOnlyList<MessageInfo> FilterMessages(
        IReadOnlyList<MessageRecord> records,
        string query)
    {
        return records
            .Where(record => MessageMatches(record, query))
            .OrderByDescending(m => m.ReceivedAt)
            .ThenBy(m => m.Subject)
            .Select(ToMessageInfo)
            .ToList();
    }

    private static bool MessageMatches(MessageRecord record, string query) =>
        MessageSearch.Matches(
            record.IsFlagged,
            record.IsRead,
            record.Subject,
            record.FromAddress,
            record.BodyText,
            record.BodyHtml,
            query);
}
