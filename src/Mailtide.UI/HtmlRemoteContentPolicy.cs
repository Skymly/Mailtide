using System.Text.RegularExpressions;
using Mailtide.Core;

namespace Mailtide.UI;

/// <summary>
/// Decides which URIs stored HTML may load in NativeWebView.
/// Remote network schemes are denied so rendering a Message cannot phone home.
/// </summary>
public static class HtmlRemoteContentPolicy
{
    public const string OfflineContentSecurityPolicy =
        "default-src 'none'; img-src data:; style-src 'unsafe-inline'; font-src data:; media-src data:";

    public const string QuotedToggleId = "mailtide-show-quotes";

    public static bool IsAllowed(Uri? uri)
    {
        if (uri is null)
        {
            return false;
        }

        return uri.Scheme.Equals(Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase) is false
            && (uri.Scheme.Equals("about", StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals("data", StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals("blob", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsAllowed(string? uriString) =>
        Uri.TryCreate(uriString, UriKind.Absolute, out var uri) && IsAllowed(uri);

    public static bool IsExternalNavigation(Uri? uri)
    {
        if (uri is null)
        {
            return false;
        }

        return uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals(Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsExternalNavigation(string? uriString) =>
        Uri.TryCreate(uriString, UriKind.Absolute, out var uri) && IsExternalNavigation(uri);

    public static bool HasQuotedHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return false;
        }

        return html.Contains("<blockquote", StringComparison.OrdinalIgnoreCase)
            || html.Contains("gmail_quote", StringComparison.OrdinalIgnoreCase)
            || html.Contains("yahoo_quoted", StringComparison.OrdinalIgnoreCase)
            || html.Contains("moz-cite-prefix", StringComparison.OrdinalIgnoreCase);
    }

    public static bool QuoteCollapsedHidesFind(string? html, string? query)
    {
        if (!HasQuotedHtml(html) || string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        var needle = query.Trim();
        var full = HtmlText.Strip(html);
        if (!full.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var visible = HtmlText.Strip(WithoutQuotedHtml(html));
        return !visible.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    public static string WithoutQuotedHtml(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return html ?? string.Empty;
        }

        var current = html;
        while (true)
        {
            var next = RemoveFirstQuotedElement(current);
            if (next is null)
            {
                return current;
            }

            current = next;
        }
    }

    private static readonly string[] QuotedClassMarkers = ["gmail_quote", "yahoo_quoted", "moz-cite-prefix"];

    private static string? RemoveFirstQuotedElement(string html)
    {
        if (TryFindElement(html, "blockquote", requiredClass: null, out var start, out var end))
        {
            return html[..start] + html[end..];
        }

        foreach (var marker in QuotedClassMarkers)
        {
            if (TryFindElement(html, tag: null, requiredClass: marker, out start, out end))
            {
                return html[..start] + html[end..];
            }
        }

        return null;
    }

    private static bool TryFindElement(
        string html,
        string? tag,
        string? requiredClass,
        out int start,
        out int end)
    {
        start = 0;
        end = 0;
        var i = 0;
        while (i < html.Length)
        {
            var lt = html.IndexOf('<', i);
            if (lt < 0 || lt + 1 >= html.Length)
            {
                return false;
            }

            if (html[lt + 1] is '/' or '!' or '?')
            {
                i = lt + 1;
                continue;
            }

            var nameEnd = lt + 1;
            while (nameEnd < html.Length && char.IsLetterOrDigit(html[nameEnd]))
            {
                nameEnd++;
            }

            if (nameEnd == lt + 1)
            {
                i = lt + 1;
                continue;
            }

            var name = html[(lt + 1)..nameEnd];
            if (tag is not null && !name.Equals(tag, StringComparison.OrdinalIgnoreCase))
            {
                i = nameEnd;
                continue;
            }

            var gt = html.IndexOf('>', nameEnd);
            if (gt < 0)
            {
                return false;
            }

            var attrs = html[nameEnd..gt];
            if (requiredClass is not null && !ClassContains(attrs, requiredClass))
            {
                i = gt + 1;
                continue;
            }

            if (tag is null && requiredClass is null)
            {
                i = gt + 1;
                continue;
            }

            if (attrs.TrimEnd().EndsWith('/'))
            {
                start = lt;
                end = gt + 1;
                return true;
            }

            if (!TryFindMatchingClose(html, name, gt + 1, out var closeEnd))
            {
                i = gt + 1;
                continue;
            }

            start = lt;
            end = closeEnd;
            return true;
        }

        return false;
    }

    private static bool ClassContains(string attrs, string marker)
    {
        var index = attrs.IndexOf("class", StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return false;
        }

        return attrs.Contains(marker, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryFindMatchingClose(string html, string name, int from, out int closeEnd)
    {
        closeEnd = 0;
        var depth = 1;
        var i = from;
        while (i < html.Length)
        {
            var lt = html.IndexOf('<', i);
            if (lt < 0)
            {
                return false;
            }

            var closing = lt + 1 < html.Length && html[lt + 1] == '/';
            var nameStart = closing ? lt + 2 : lt + 1;
            var nameEnd = nameStart;
            while (nameEnd < html.Length && char.IsLetterOrDigit(html[nameEnd]))
            {
                nameEnd++;
            }

            var gt = html.IndexOf('>', nameEnd);
            if (gt < 0)
            {
                return false;
            }

            if (nameEnd > nameStart
                && html[nameStart..nameEnd].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                if (closing)
                {
                    depth--;
                    if (depth == 0)
                    {
                        closeEnd = gt + 1;
                        return true;
                    }
                }
                else if (!html[nameEnd..gt].TrimEnd().EndsWith('/'))
                {
                    depth++;
                }
            }

            i = gt + 1;
        }

        return false;
    }

    public static string StripDisallowedSubresources(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        return SubresourceUrl.Replace(html, static match =>
        {
            var value = match.Groups[1].Value;
            return IsAllowed(value) ? value : "about:blank";
        });
    }

    private static readonly Regex SubresourceUrl = new(
        """(?<=\b(?:src|href|srcset|poster)\s*=\s*["'])([^"']+)(?=["'])""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string WrapForOfflineRender(string html, bool showQuoted = false)
    {
        ArgumentNullException.ThrowIfNull(html);
        html = StripDisallowedSubresources(html);
        var quoteChrome = HasQuotedHtml(html) ? QuotedHtmlChrome(showQuoted) : string.Empty;
        return
            "<!DOCTYPE html><html><head><meta http-equiv=\"Content-Security-Policy\" content=\""
            + OfflineContentSecurityPolicy
            + "\"><style>mark{background:#ffe08a;color:inherit}mark.mailtide-current{background:#ffb347}</style></head><body>"
            + quoteChrome
            + "<div class=\"mailtide-html\">"
            + html
            + "</div></body></html>";
    }

    private static string QuotedHtmlChrome(bool showQuoted)
    {
        var checkedAttr = showQuoted ? " checked" : string.Empty;
        return
            "<style>"
            + "#" + QuotedToggleId + "{position:absolute;clip:rect(0,0,0,0)}"
            + "#" + QuotedToggleId + ":not(:checked)~.mailtide-html blockquote,"
            + "#" + QuotedToggleId + ":not(:checked)~.mailtide-html .gmail_quote,"
            + "#" + QuotedToggleId + ":not(:checked)~.mailtide-html .yahoo_quoted,"
            + "#" + QuotedToggleId + ":not(:checked)~.mailtide-html .moz-cite-prefix{display:none!important}"
            + "label[for=" + QuotedToggleId + "]{position:absolute;clip:rect(0,0,0,0)}"
            + "#" + QuotedToggleId + ":not(:checked)+label[for=" + QuotedToggleId + "]:before{content:\"Show quoted text\"}"
            + "#" + QuotedToggleId + ":checked+label[for=" + QuotedToggleId + "]:before{content:\"Hide quoted text\"}"
            + "</style>"
            + "<input type=\"checkbox\" id=\"" + QuotedToggleId + "\"" + checkedAttr + ">"
            + "<label for=\"" + QuotedToggleId + "\"></label>";
    }

    public static bool HasRemoteImages(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return false;
        }

        return html.Contains("src=\"http", StringComparison.OrdinalIgnoreCase)
            || html.Contains("src='http", StringComparison.OrdinalIgnoreCase)
            || html.Contains("src=http", StringComparison.OrdinalIgnoreCase)
            || html.Contains("src=\"//", StringComparison.OrdinalIgnoreCase)
            || html.Contains("src='//", StringComparison.OrdinalIgnoreCase);
    }
}
