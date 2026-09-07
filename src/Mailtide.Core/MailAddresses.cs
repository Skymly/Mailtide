using System.Text;
using MimeKit;

namespace Mailtide.Core;

public static class MailAddresses
{
    public static IReadOnlyList<string> Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var normalized = NormalizeSeparators(raw);
        if (InternetAddressList.TryParse(normalized, out var list))
        {
            var parsed = new List<string>();
            foreach (var address in list)
            {
                if (address is MailboxAddress mailbox)
                {
                    parsed.Add(Format(mailbox));
                }
            }

            if (parsed.Count > 0)
            {
                return parsed;
            }
        }

        return normalized.Split(
            [',', ';'],
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    public static IReadOnlyList<string> Invalid(IEnumerable<string> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        return addresses
            .Where(address => !IsMailbox(address))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool IsMailbox(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !MailboxAddress.TryParse(value, out var mailbox))
        {
            return false;
        }

        var address = mailbox.Address;
        var at = address.LastIndexOf('@');
        return at > 0 && at < address.Length - 1;
    }

    public static string Format(MailboxAddress mailbox)
    {
        ArgumentNullException.ThrowIfNull(mailbox);
        return string.IsNullOrWhiteSpace(mailbox.Name) ? mailbox.Address : mailbox.ToString();
    }

    public static bool TryGetMailbox(string? value, out string address, out string formatted)
    {
        address = string.Empty;
        formatted = string.Empty;
        if (!IsMailbox(value) || !MailboxAddress.TryParse(value, out var mailbox))
        {
            return false;
        }

        address = mailbox.Address;
        formatted = Format(mailbox);
        return true;
    }

    private static string NormalizeSeparators(string raw)
    {
        var builder = new StringBuilder(raw.Length);
        var inQuotes = false;
        var angleDepth = 0;
        foreach (var ch in raw)
        {
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
            }

            builder.Append(!inQuotes && angleDepth == 0 && ch == ';' ? ',' : ch);
        }

        return builder.ToString();
    }

    public static bool TryParseMailto(Uri? uri, out MailtoCompose compose)
    {
        compose = new MailtoCompose("", "", "", "", "");
        if (uri is null || !uri.Scheme.Equals(Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var absolute = uri.AbsoluteUri;
        const string prefix = "mailto:";
        if (!absolute.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = absolute[prefix.Length..];
        var queryStart = rest.IndexOf('?');
        var toRaw = queryStart < 0 ? rest : rest[..queryStart];
        var query = queryStart < 0 ? string.Empty : rest[(queryStart + 1)..];
        var to = DecodeMailtoPart(toRaw);
        var cc = string.Empty;
        var bcc = string.Empty;
        var subject = string.Empty;
        var body = string.Empty;
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var key = eq < 0 ? part : part[..eq];
            var value = DecodeMailtoPart(eq < 0 ? string.Empty : part[(eq + 1)..]);
            if (key.Equals("to", StringComparison.OrdinalIgnoreCase))
            {
                to = string.IsNullOrEmpty(to) ? value : to + ", " + value;
            }
            else if (key.Equals("cc", StringComparison.OrdinalIgnoreCase))
            {
                cc = value;
            }
            else if (key.Equals("bcc", StringComparison.OrdinalIgnoreCase))
            {
                bcc = value;
            }
            else if (key.Equals("subject", StringComparison.OrdinalIgnoreCase))
            {
                subject = value;
            }
            else if (key.Equals("body", StringComparison.OrdinalIgnoreCase))
            {
                body = value;
            }
        }

        compose = new MailtoCompose(to, cc, bcc, subject, body);
        return true;
    }

    private static string DecodeMailtoPart(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return Uri.UnescapeDataString(value.Replace('+', ' '));
    }
}

public sealed record MailtoCompose(
    string To,
    string Cc,
    string Bcc,
    string Subject,
    string Body);
