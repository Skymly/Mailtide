using Mailtide.Core.Store;

namespace Mailtide.Core;

/// <summary>
/// Groups stored Messages into reply threads from Internet-Message-Id and References.
/// </summary>
internal static class ReplyThreadIndex
{
    public static IReadOnlyList<MessageThreadInfo> Group(
        IReadOnlyList<MessageRecord> records,
        Func<MessageRecord, MessageInfo> toInfo)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(toInfo);

        if (records.Count == 0)
        {
            return [];
        }

        var parent = records.ToDictionary(record => record.Id, record => record.Id);

        Guid Find(Guid id)
        {
            while (parent[id] != id)
            {
                parent[id] = parent[parent[id]];
                id = parent[id];
            }

            return id;
        }

        void Union(Guid left, Guid right)
        {
            left = Find(left);
            right = Find(right);
            if (left != right)
            {
                parent[right] = left;
            }
        }

        var tokenOwner = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            foreach (var token in Tokens(record))
            {
                if (tokenOwner.TryGetValue(token, out var owner))
                {
                    Union(record.Id, owner);
                }
                else
                {
                    tokenOwner[token] = record.Id;
                }
            }
        }

        return records
            .GroupBy(record => Find(record.Id))
            .Select(group =>
            {
                var ordered = group
                    .Select(toInfo)
                    .OrderByDescending(message => message.ReceivedAt)
                    .ThenBy(message => message.Subject)
                    .ToList();
                return new MessageThreadInfo(ordered[0], ordered);
            })
            .OrderByDescending(thread => thread.Latest.ReceivedAt)
            .ThenBy(thread => thread.Latest.Subject)
            .ToList();
    }

    private static IEnumerable<string> Tokens(MessageRecord record)
    {
        var own = Normalize(record.InternetMessageId);
        if (own is not null)
        {
            yield return own;
        }

        foreach (var reference in PackedStringList.Decode(record.ReferencesJson))
        {
            var token = Normalize(reference);
            if (token is not null)
            {
                yield return token;
            }
        }
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '<' && trimmed[^1] == '>')
        {
            trimmed = trimmed[1..^1];
        }

        return trimmed.Length == 0 ? null : trimmed;
    }
}
