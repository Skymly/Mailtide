using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Mailtide.Core;

public static partial class HtmlText
{
    public static string Strip(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var withoutTags = TagPattern().Replace(html, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        return WhitespacePattern().Replace(decoded, " ").Trim();
    }

    public static string Highlight(string? html, string? needle, int currentOccurrence = -1)
    {
        if (string.IsNullOrEmpty(html) || string.IsNullOrWhiteSpace(needle))
        {
            return html ?? string.Empty;
        }

        var n = needle.Trim();
        var sb = new StringBuilder(html.Length + 64);
        var occurrence = 0;
        var i = 0;
        while (i < html.Length)
        {
            if (html[i] != '<')
            {
                var next = html.IndexOf('<', i);
                if (next < 0)
                {
                    next = html.Length;
                }

                AppendHighlighted(sb, html.AsSpan(i, next - i), n, ref occurrence, currentOccurrence);
                i = next;
                continue;
            }

            var end = html.IndexOf('>', i + 1);
            if (end < 0)
            {
                sb.Append(html.AsSpan(i));
                break;
            }

            sb.Append(html, i, end - i + 1);
            i = end + 1;
        }

        return sb.ToString();
    }

    private static void AppendHighlighted(
        StringBuilder sb,
        ReadOnlySpan<char> text,
        string needle,
        ref int occurrence,
        int currentOccurrence)
    {
        var hay = text.ToString();
        var start = 0;
        while (start < hay.Length)
        {
            var found = hay.IndexOf(needle, start, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                sb.Append(hay, start, hay.Length - start);
                return;
            }

            sb.Append(hay, start, found - start);
            sb.Append(occurrence == currentOccurrence
                ? "<mark class=\"mailtide-current\">"
                : "<mark>");
            occurrence++;
            sb.Append(hay, found, needle.Length);
            sb.Append("</mark>");
            start = found + Math.Max(needle.Length, 1);
        }
    }

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();
}
