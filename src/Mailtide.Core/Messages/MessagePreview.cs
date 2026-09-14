namespace Mailtide.Core;

public static class MessagePreview
{
    public const int MaxLength = 80;

    public static string FromBodyText(string? bodyText, string? needle = null)
    {
        if (string.IsNullOrWhiteSpace(bodyText))
        {
            return string.Empty;
        }

        var visible = MessageQuote.Split(bodyText).Visible;
        if (string.IsNullOrWhiteSpace(visible))
        {
            visible = bodyText;
        }

        if (!string.IsNullOrWhiteSpace(needle))
        {
            return Snippet(visible, MaxLength, needle);
        }

        var line = visible.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')[0];
        return Snippet(line, MaxLength);
    }

    public static string Snippet(string? text, int maxLength = MaxLength, string? needle = null)
    {
        if (maxLength <= 0 || string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (collapsed.Length == 0)
        {
            return string.Empty;
        }

        var start = 0;
        if (!string.IsNullOrWhiteSpace(needle))
        {
            var n = needle.Trim();
            var found = collapsed.IndexOf(n, StringComparison.OrdinalIgnoreCase);
            if (found >= 0)
            {
                var pad = Math.Max(8, (maxLength - n.Length) / 2);
                start = Math.Max(0, found - pad);
                if (start + maxLength > collapsed.Length)
                {
                    start = Math.Max(0, collapsed.Length - maxLength);
                }
            }
        }

        if (collapsed.Length <= maxLength && start == 0)
        {
            return collapsed;
        }

        var take = Math.Min(maxLength, collapsed.Length - start);
        var slice = collapsed.Substring(start, take).Trim();
        if (start > 0)
        {
            slice = "…" + slice;
        }

        if (start + take < collapsed.Length)
        {
            slice += "…";
        }

        return slice;
    }
}
