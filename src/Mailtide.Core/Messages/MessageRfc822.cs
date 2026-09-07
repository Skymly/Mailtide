using MimeKit;

namespace Mailtide.Core;

public static class MessageRfc822
{
    public static string FileName(string? subject)
    {
        var name = string.IsNullOrWhiteSpace(subject) ? "message" : subject.Trim();
        name = name.ReplaceLineEndings(" ").Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars().Concat([':', '*', '?', '"', '<', '>', '|']))
        {
            name = name.Replace(invalid, '_');
        }

        while (name.Contains("  ", StringComparison.Ordinal))
        {
            name = name.Replace("  ", " ", StringComparison.Ordinal);
        }

        name = name.Trim(' ', '.');
        if (name.Length == 0)
        {
            name = "message";
        }

        if (name.Length > 80)
        {
            name = name[..80].Trim();
        }

        return name + ".eml";
    }

    public static void Write(
        Stream destination,
        string fromAddress,
        IReadOnlyList<string> toAddresses,
        IReadOnlyList<string> ccAddresses,
        IReadOnlyList<string> bccAddresses,
        IReadOnlyList<string> replyToAddresses,
        string subject,
        DateTimeOffset date,
        string? bodyText,
        string? bodyHtml,
        string? internetMessageId,
        IReadOnlyList<string> references,
        IReadOnlyList<(string FileName, string ContentType, byte[] Content)> attachments)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(toAddresses);
        ArgumentNullException.ThrowIfNull(ccAddresses);
        ArgumentNullException.ThrowIfNull(bccAddresses);
        ArgumentNullException.ThrowIfNull(replyToAddresses);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(attachments);

        var mime = new MimeMessage();
        AddAddresses(mime.From, [fromAddress]);
        AddAddresses(mime.To, toAddresses);
        AddAddresses(mime.Cc, ccAddresses);
        AddAddresses(mime.Bcc, bccAddresses);
        AddAddresses(mime.ReplyTo, replyToAddresses);
        mime.Subject = subject ?? string.Empty;
        mime.Date = date;
        if (!string.IsNullOrWhiteSpace(internetMessageId))
        {
            mime.MessageId = internetMessageId.Trim().Trim('<', '>');
        }

        foreach (var reference in references)
        {
            if (!string.IsNullOrWhiteSpace(reference))
            {
                mime.References.Add(reference.Trim().Trim('<', '>'));
            }
        }

        var text = bodyText ?? string.Empty;
        if (attachments.Count == 0 && string.IsNullOrEmpty(bodyHtml))
        {
            mime.Body = new TextPart("plain") { Text = text };
        }
        else
        {
            var builder = new BodyBuilder
            {
                TextBody = text,
                HtmlBody = string.IsNullOrEmpty(bodyHtml) ? null : bodyHtml,
            };
            foreach (var attachment in attachments)
            {
                var type = string.IsNullOrWhiteSpace(attachment.ContentType)
                    ? new ContentType("application", "octet-stream")
                    : ContentType.Parse(attachment.ContentType);
                builder.Attachments.Add(
                    string.IsNullOrWhiteSpace(attachment.FileName) ? "attachment" : attachment.FileName,
                    attachment.Content,
                    type);
            }

            mime.Body = builder.ToMessageBody();
        }

        mime.WriteTo(destination);
    }

    private static void AddAddresses(InternetAddressList list, IEnumerable<string> addresses)
    {
        foreach (var address in addresses)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                continue;
            }

            if (MailboxAddress.TryParse(address, out var parsed))
            {
                list.Add(parsed);
            }
        }
    }
}
