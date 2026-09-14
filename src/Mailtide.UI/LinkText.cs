using Mailtide.Core;

namespace Mailtide.UI;

public static class LinkText
{
    public static bool TryGetUri(string? text, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim().Trim('"', '\'', '<', '>', ',', ';');
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute)
            && HtmlRemoteContentPolicy.IsExternalNavigation(absolute))
        {
            uri = absolute;
            return true;
        }

        if (MailAddresses.TryGetMailbox(trimmed, out var address, out _))
        {
            uri = new Uri("mailto:" + address);
            return true;
        }

        if (trimmed.Contains(' ', StringComparison.Ordinal) || !trimmed.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        if (Uri.TryCreate("https://" + trimmed, UriKind.Absolute, out var https)
            && HtmlRemoteContentPolicy.IsExternalNavigation(https)
            && https.Host.Contains('.', StringComparison.Ordinal))
        {
            uri = https;
            return true;
        }

        return false;
    }

    public static bool TryGetUriAt(string? text, int index, out Uri uri)
    {
        uri = null!;
        var token = TokenAt(text, index);
        return TryGetUri(token, out uri);
    }

    public static string? TokenAt(string? text, int index)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        index = Math.Clamp(index, 0, text.Length);
        if (index == text.Length)
        {
            index--;
        }

        if (index < 0)
        {
            return null;
        }

        if (char.IsWhiteSpace(text[index]))
        {
            if (index > 0 && !char.IsWhiteSpace(text[index - 1]))
            {
                index--;
            }
            else
            {
                return null;
            }
        }

        var start = index;
        while (start > 0 && !char.IsWhiteSpace(text[start - 1]))
        {
            start--;
        }

        var end = index + 1;
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
        {
            end++;
        }

        return text[start..end];
    }
}
