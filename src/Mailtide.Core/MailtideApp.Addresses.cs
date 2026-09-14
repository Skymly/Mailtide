using Mailtide.Core.Store;
using Microsoft.EntityFrameworkCore;

namespace Mailtide.Core;

public sealed partial class MailtideApp
{
    public async Task<IReadOnlyList<string>> ListRecentAddressesAsync(
        int limit = 80,
        CancellationToken cancellationToken = default)
    {
        if (limit < 1)
        {
            return [];
        }

        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var messageRows = await _db.Messages
                .AsNoTracking()
                .Select(message => new { message.FromAddress, message.ToAddresses, message.CcAddresses, message.BccAddresses, message.ReceivedAt })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var messages = messageRows
                .OrderByDescending(message => message.ReceivedAt)
                .Take(200)
                .ToList();
            var draftRows = await _db.Drafts
                .AsNoTracking()
                .Select(draft => new { draft.ToAddresses, draft.CcAddresses, draft.BccAddresses, draft.UpdatedAt })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var drafts = draftRows
                .OrderByDescending(draft => draft.UpdatedAt)
                .Take(50)
                .ToList();

            var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var message in messages)
            {
                Consider(seen, message.FromAddress);
                foreach (var item in PackedStringList.Decode(message.ToAddresses))
                {
                    Consider(seen, item);
                }

                foreach (var item in PackedStringList.Decode(message.CcAddresses))
                {
                    Consider(seen, item);
                }

                foreach (var item in PackedStringList.Decode(message.BccAddresses))
                {
                    Consider(seen, item);
                }

                if (seen.Count >= limit)
                {
                    break;
                }
            }

            foreach (var draft in drafts)
            {
                foreach (var item in PackedStringList.Decode(draft.ToAddresses))
                {
                    Consider(seen, item);
                }

                foreach (var item in PackedStringList.Decode(draft.CcAddresses))
                {
                    Consider(seen, item);
                }

                foreach (var item in PackedStringList.Decode(draft.BccAddresses))
                {
                    Consider(seen, item);
                }

                if (seen.Count >= limit)
                {
                    break;
                }
            }

            return seen.Values.Take(limit).ToList();
        }
        finally
        {
            _dbGate.Release();
        }
    }

    private static void Consider(Dictionary<string, string> seen, string? value)
    {
        if (!MailAddresses.TryGetMailbox(value, out var address, out var formatted)
            || seen.ContainsKey(address))
        {
            return;
        }

        seen[address] = formatted;
    }
}
