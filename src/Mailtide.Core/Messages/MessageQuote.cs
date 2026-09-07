using System.Linq;

namespace Mailtide.Core;

public sealed record QuotedSplit(string Visible, string Quoted)
{
    public bool HasQuoted => !string.IsNullOrEmpty(Quoted);
}

public static class MessageQuote
{
    public static QuotedSplit Split(string? body)
    {
        var text = (body ?? string.Empty).ReplaceLineEndings("\n");
        if (text.Length == 0)
        {
            return new QuotedSplit(string.Empty, string.Empty);
        }

        var lines = text.Split('\n');
        var start = QuoteStart(lines);
        if (start is null or 0)
        {
            return new QuotedSplit(text.TrimEnd(), string.Empty);
        }

        var split = start.Value;
        while (split > 0 && string.IsNullOrWhiteSpace(lines[split - 1]))
        {
            split--;
        }

        if (split == 0)
        {
            return new QuotedSplit(text.TrimEnd(), string.Empty);
        }

        var visible = string.Join('\n', lines.Take(split)).TrimEnd();
        var quoted = string.Join('\n', lines.Skip(split)).TrimEnd();
        return visible.Length == 0
            ? new QuotedSplit(text.TrimEnd(), string.Empty)
            : new QuotedSplit(visible, quoted);
    }

    private static int? QuoteStart(IReadOnlyList<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (IsQuoteHeader(lines[i].Trim()))
            {
                return i;
            }
        }

        var firstQuoted = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.StartsWith('>'))
            {
                if (firstQuoted < 0)
                {
                    firstQuoted = i;
                }

                continue;
            }

            if (!string.IsNullOrWhiteSpace(line))
            {
                firstQuoted = -1;
            }
        }

        return firstQuoted > 0 ? firstQuoted : null;
    }

    private static bool IsQuoteHeader(string trimmed) =>
        trimmed.Contains(" wrote:", StringComparison.OrdinalIgnoreCase)
        || trimmed.StartsWith("-----Original Message-----", StringComparison.OrdinalIgnoreCase)
        || trimmed.StartsWith("---------- Forwarded Message ----------", StringComparison.Ordinal)
        || trimmed.Equals("Begin forwarded message:", StringComparison.OrdinalIgnoreCase);
}
