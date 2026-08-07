using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using MimeKit;

namespace Mailtide.Core.Imap;

/// <summary>
/// Creates MailKitLite-backed IMAP protocol clients.
/// </summary>
public sealed class MailKitImapClientFactory : IImapClientFactory
{
    public IImapClient Create() => new MailKitImapClient();
}

internal sealed class MailKitImapClient : IImapClient
{
    private ImapClient? _client;

    public async Task ConnectAndAuthenticateAsync(
        string host,
        int port,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(password);

        _client?.Dispose();
        _client = new ImapClient();

        try
        {
            await _client
                .ConnectAsync(host, port, SocketOptionsForPort(port), cancellationToken)
                .ConfigureAwait(false);
            await _client
                .AuthenticateAsync(username, password, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (AuthenticationException ex)
        {
            await DisposeClientAsync().ConfigureAwait(false);
            throw new ImapAuthenticationException("IMAP authentication failed.", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ImapAuthenticationException)
        {
            await DisposeClientAsync().ConfigureAwait(false);
            throw new ImapProtocolException("IMAP protocol failure.", ex);
        }
    }

    public async Task<IReadOnlyList<RemoteMailbox>> ListMailboxesAsync(
        CancellationToken cancellationToken = default)
    {
        var client = EnsureAuthenticated();

        try
        {
            var folders = new Dictionary<string, IMailFolder>(StringComparer.OrdinalIgnoreCase);

            void Add(IMailFolder folder)
            {
                folders[folder.FullName] = folder;
            }

            Add(client.Inbox);

            foreach (var ns in client.PersonalNamespaces)
            {
                foreach (var folder in await client
                             .GetFoldersAsync(ns, StatusItems.None, subscribedOnly: false, cancellationToken)
                             .ConfigureAwait(false))
                {
                    Add(folder);
                }
            }

            return folders.Values
                .Select(folder => new RemoteMailbox(
                    Name: folder.Name,
                    Path: folder.FullName,
                    Role: MapRole(folder.Attributes)))
                .OrderBy(m => m.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (AuthenticationException ex)
        {
            throw new ImapAuthenticationException("IMAP authentication failed.", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ImapAuthenticationException)
        {
            throw new ImapProtocolException("IMAP protocol failure.", ex);
        }
    }

    public async Task<IReadOnlyList<RemoteMessage>> FetchMessagesAsync(
        string mailboxPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mailboxPath);
        var client = EnsureAuthenticated();

        try
        {
            var folder = await client.GetFolderAsync(mailboxPath, cancellationToken).ConfigureAwait(false);
            await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken).ConfigureAwait(false);

            if (folder.Count == 0)
            {
                return [];
            }

            var summaries = await folder
                .FetchAsync(
                    0,
                    -1,
                    MessageSummaryItems.UniqueId
                    | MessageSummaryItems.Flags
                    | MessageSummaryItems.InternalDate,
                    cancellationToken)
                .ConfigureAwait(false);

            var messages = new List<RemoteMessage>(summaries.Count);
            foreach (var summary in summaries)
            {
                var mime = await folder.GetMessageAsync(summary.UniqueId, cancellationToken)
                    .ConfigureAwait(false);

                var remote = new RemoteMessage(
                    RemoteId: summary.UniqueId.Id.ToString(),
                    Subject: mime.Subject ?? string.Empty,
                    FromAddress: mime.From.Mailboxes.FirstOrDefault()?.Address ?? string.Empty,
                    ReceivedAt: summary.InternalDate ?? mime.Date,
                    IsRead: summary.Flags?.HasFlag(MessageFlags.Seen) == true,
                    BodyText: ExtractBodyText(mime))
                {
                    Attachments = ExtractAttachments(mime),
                };
                messages.Add(remote);
            }

            return messages;
        }
        catch (AuthenticationException ex)
        {
            throw new ImapAuthenticationException("IMAP authentication failed.", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ImapAuthenticationException)
        {
            throw new ImapProtocolException("IMAP protocol failure.", ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeClientAsync().ConfigureAwait(false);
    }

    private async Task DisposeClientAsync()
    {
        if (_client is null)
        {
            return;
        }

        try
        {
            if (_client.IsConnected)
            {
                await _client.DisconnectAsync(quit: true).ConfigureAwait(false);
            }
        }
        catch
        {
            // Best-effort disconnect on dispose.
        }
        finally
        {
            _client.Dispose();
            _client = null;
        }
    }

    private ImapClient EnsureAuthenticated()
    {
        if (_client is null || !_client.IsAuthenticated)
        {
            throw new InvalidOperationException("IMAP client is not authenticated.");
        }

        return _client;
    }

    private static SecureSocketOptions SocketOptionsForPort(int port) =>
        port switch
        {
            993 or 465 => SecureSocketOptions.SslOnConnect,
            143 or 587 => SecureSocketOptions.StartTlsWhenAvailable,
            _ => SecureSocketOptions.None,
        };

    private static MailboxRole? MapRole(FolderAttributes attributes)
    {
        if (attributes.HasFlag(FolderAttributes.Inbox))
        {
            return MailboxRole.Inbox;
        }

        if (attributes.HasFlag(FolderAttributes.Sent))
        {
            return MailboxRole.Sent;
        }

        if (attributes.HasFlag(FolderAttributes.Drafts))
        {
            return MailboxRole.Drafts;
        }

        if (attributes.HasFlag(FolderAttributes.Trash))
        {
            return MailboxRole.Trash;
        }

        if (attributes.HasFlag(FolderAttributes.Junk))
        {
            return MailboxRole.Junk;
        }

        return null;
    }

    private static string ExtractBodyText(MimeMessage mime)
    {
        if (!string.IsNullOrWhiteSpace(mime.TextBody))
        {
            return NormalizeBody(mime.TextBody);
        }

        if (string.IsNullOrWhiteSpace(mime.HtmlBody))
        {
            return string.Empty;
        }

        // BodyText is plain text — strip markup rather than storing raw HTML.
        var withoutTags = System.Text.RegularExpressions.Regex.Replace(mime.HtmlBody, "<[^>]+>", " ");
        var decoded = System.Net.WebUtility.HtmlDecode(withoutTags);
        return NormalizeBody(
            System.Text.RegularExpressions.Regex.Replace(decoded, @"\s+", " ").Trim());
    }

    private static string NormalizeBody(string? body) =>
        (body ?? string.Empty).TrimEnd('\r', '\n');

    private static IReadOnlyList<RemoteAttachment> ExtractAttachments(MimeMessage mime)
    {
        var attachments = new List<RemoteAttachment>();
        foreach (var part in mime.BodyParts.OfType<MimePart>())
        {
            if (!part.IsAttachment && string.IsNullOrEmpty(part.FileName))
            {
                continue;
            }

            if (part.Content is null)
            {
                continue;
            }

            using var memory = new MemoryStream();
            part.Content.DecodeTo(memory);
            attachments.Add(
                new RemoteAttachment(
                    FileName: part.FileName ?? "attachment",
                    ContentType: part.ContentType.MimeType,
                    Content: memory.ToArray()));
        }

        return attachments;
    }
}
