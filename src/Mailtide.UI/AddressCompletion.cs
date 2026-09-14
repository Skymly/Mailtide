using Mailtide.Core;

namespace Mailtide.UI;

public static class AddressCompletion
{
    public static (int Start, int Length, string Token) CurrentToken(string? text, int caret)
    {
        text ??= string.Empty;
        caret = Math.Clamp(caret, 0, text.Length);
        var start = 0;
        var inQuotes = false;
        var angleDepth = 0;
        for (var i = 0; i < caret; i++)
        {
            var ch = text[i];
            if (ch == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes)
            {
                if (ch == '<')
                {
                    angleDepth++;
                }
                else if (ch == '>' && angleDepth > 0)
                {
                    angleDepth--;
                }
                else if (angleDepth == 0 && (ch == ',' || ch == ';'))
                {
                    start = i + 1;
                }
            }
        }

        while (start < caret && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        return (start, caret - start, text[start..caret]);
    }

    public static IReadOnlyList<string> RecipientsToExclude(
        string? currentText,
        int tokenStart,
        int tokenLength,
        params string?[] siblings)
    {
        currentText ??= string.Empty;
        tokenStart = Math.Clamp(tokenStart, 0, currentText.Length);
        var tokenEnd = Math.Clamp(tokenStart + Math.Max(tokenLength, 0), tokenStart, currentText.Length);
        var rest = currentText[..tokenStart] + currentText[tokenEnd..];
        var excluded = new List<string>();
        excluded.AddRange(MailAddresses.Parse(rest));
        foreach (var sibling in siblings)
        {
            excluded.AddRange(MailAddresses.Parse(sibling));
        }

        return excluded;
    }

    public static IReadOnlyList<string> Suggest(
        string token,
        IReadOnlyList<string> recent,
        IEnumerable<string>? exclude = null,
        int take = 8)
    {
        ArgumentNullException.ThrowIfNull(recent);
        if (take < 1 || string.IsNullOrWhiteSpace(token))
        {
            return [];
        }

        var needle = token.Trim();
        var excluded = Mailboxes(exclude);
        return recent
            .Where(item =>
                item.Contains(needle, StringComparison.OrdinalIgnoreCase)
                && !item.Equals(needle, StringComparison.OrdinalIgnoreCase)
                && !IsExcluded(item, excluded))
            .Take(take)
            .ToList();
    }

    private static HashSet<string> Mailboxes(IEnumerable<string>? addresses)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (addresses is null)
        {
            return set;
        }

        foreach (var address in addresses)
        {
            if (MailAddresses.TryGetMailbox(address, out var mailbox, out _))
            {
                set.Add(mailbox);
            }
        }

        return set;
    }

    private static bool IsExcluded(string item, HashSet<string> excluded) =>
        excluded.Count > 0
        && MailAddresses.TryGetMailbox(item, out var mailbox, out _)
        && excluded.Contains(mailbox);

    public static string ReplaceCurrentToken(
        string? text,
        int caret,
        string suggestion,
        out int newCaret,
        bool appendSeparator = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestion);
        text ??= string.Empty;
        var (start, length, _) = CurrentToken(text, caret);
        var prefix = text[..start];
        var suffix = text[(start + length)..];
        var insert = suggestion;
        if (appendSeparator)
        {
            var rest = suffix.TrimStart();
            if (rest.Length == 0 || (rest[0] != ',' && rest[0] != ';'))
            {
                insert += ", ";
            }
        }

        newCaret = prefix.Length + insert.Length;
        return prefix + insert + suffix;
    }
}
