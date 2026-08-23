namespace Mailtide.Core;

public static class MessageSearch
{
    public const string FlaggedOperator = "is:flagged";
    public const string UnreadOperator = "is:unread";

    public static bool Matches(
        bool isFlagged,
        bool isRead,
        string subject,
        string fromAddress,
        string? bodyText,
        string? bodyHtml,
        string query)
    {
        var parsed = Parse(query);
        if (parsed.FlaggedOnly && !isFlagged)
        {
            return false;
        }

        if (parsed.UnreadOnly && isRead)
        {
            return false;
        }

        if (string.IsNullOrEmpty(parsed.Text))
        {
            return true;
        }

        return ContainsIgnoreCase(subject, parsed.Text)
            || ContainsIgnoreCase(fromAddress, parsed.Text)
            || ContainsIgnoreCase(bodyText, parsed.Text)
            || ContainsIgnoreCase(bodyHtml, parsed.Text);
    }

    public static (bool FlaggedOnly, bool UnreadOnly, string Text) Parse(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return (false, false, string.Empty);
        }

        var flaggedOnly = false;
        var unreadOnly = false;
        var textParts = new List<string>();
        foreach (var token in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Equals(FlaggedOperator, StringComparison.OrdinalIgnoreCase))
            {
                flaggedOnly = true;
                continue;
            }

            if (token.Equals(UnreadOperator, StringComparison.OrdinalIgnoreCase))
            {
                unreadOnly = true;
                continue;
            }

            textParts.Add(token);
        }

        return (flaggedOnly, unreadOnly, string.Join(' ', textParts));
    }

    private static bool ContainsIgnoreCase(string? value, string query) =>
        !string.IsNullOrEmpty(value)
        && value.Contains(query, StringComparison.OrdinalIgnoreCase);
}
