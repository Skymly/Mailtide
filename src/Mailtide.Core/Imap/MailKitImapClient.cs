using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
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
                .ConnectAsync(host, port, MailTls.SocketOptions(host, port), cancellationToken)
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

            foreach (var folder in folders.Values)
            {
                try
                {
                    await folder.StatusAsync(StatusItems.UidValidity, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Leave UidValidity at 0 when STATUS UIDVALIDITY is unavailable.
                    _ = ex;
                }
            }

            return folders.Values
                .Select(folder => new RemoteMailbox(
                    Name: folder.Name,
                    Path: folder.FullName,
                    Role: MapRole(folder.Attributes, folder.Name))
                {
                    UidValidity = folder.UidValidity,
                })
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
                    | MessageSummaryItems.InternalDate
                    | MessageSummaryItems.Size,
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
                    FromAddress: FormatMailbox(mime.From.Mailboxes.FirstOrDefault()),
                    ReceivedAt: summary.InternalDate ?? mime.Date,
                    IsRead: summary.Flags?.HasFlag(MessageFlags.Seen) == true,
                    BodyText: ExtractBodyText(mime))
                {
                    ToAddresses = ExtractAddresses(mime.To),
                    CcAddresses = ExtractAddresses(mime.Cc),
                    BccAddresses = ExtractAddresses(mime.Bcc),
                    ReplyToAddresses = ExtractAddresses(mime.ReplyTo),
                    BodyHtml = ExtractBodyHtml(mime),
                    IsFlagged = summary.Flags?.HasFlag(MessageFlags.Flagged) == true,
                    InternetMessageId = ExtractInternetMessageId(mime),
                    References = ExtractReferences(mime),
                    Attachments = ExtractAttachments(mime),
                    SizeBytes = SummarySize(summary),
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

    public async Task<IReadOnlyList<RemoteMessageSummary>> FetchMessageSummariesAsync(
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
                    MessageSummaryItems.UniqueId | MessageSummaryItems.Flags | MessageSummaryItems.Envelope | MessageSummaryItems.InternalDate | MessageSummaryItems.Size,
                    cancellationToken)
                .ConfigureAwait(false);

            return summaries
                .Select(summary => new RemoteMessageSummary(
                    RemoteId: summary.UniqueId.Id.ToString(),
                    IsRead: summary.Flags?.HasFlag(MessageFlags.Seen) == true,
                    IsFlagged: summary.Flags?.HasFlag(MessageFlags.Flagged) == true,
                    Subject: summary.Envelope?.Subject ?? string.Empty,
                    FromAddress: FormatMailbox(summary.Envelope?.From.Mailboxes.FirstOrDefault()),
                    ReceivedAt: summary.InternalDate ?? default)
                {
                    SizeBytes = SummarySize(summary),
                })
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
        IReadOnlyList<string> remoteIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mailboxPath);
        ArgumentNullException.ThrowIfNull(remoteIds);
        if (remoteIds.Count == 0)
        {
            return [];
        }

        var client = EnsureAuthenticated();
        try
        {
            var folder = await client.GetFolderAsync(mailboxPath, cancellationToken).ConfigureAwait(false);
            await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken).ConfigureAwait(false);
            var uids = new List<UniqueId>(remoteIds.Count);
            foreach (var remoteId in remoteIds)
            {
                if (!uint.TryParse(remoteId, out var uidValue))
                {
                    throw new ImapProtocolException("IMAP protocol failure.", new FormatException($"RemoteId '{remoteId}' is not a UID."));
                }

                uids.Add(new UniqueId(uidValue));
            }

            var summaries = await folder
                .FetchAsync(
                    uids,
                    MessageSummaryItems.UniqueId
                    | MessageSummaryItems.Flags
                    | MessageSummaryItems.InternalDate
                    | MessageSummaryItems.Size,
                    cancellationToken)
                .ConfigureAwait(false);

            var messages = new List<RemoteMessage>(summaries.Count);
            foreach (var summary in summaries)
            {
                var mime = await folder.GetMessageAsync(summary.UniqueId, cancellationToken)
                    .ConfigureAwait(false);
                messages.Add(new RemoteMessage(
                    RemoteId: summary.UniqueId.Id.ToString(),
                    Subject: mime.Subject ?? string.Empty,
                    FromAddress: FormatMailbox(mime.From.Mailboxes.FirstOrDefault()),
                    ReceivedAt: summary.InternalDate ?? mime.Date,
                    IsRead: summary.Flags?.HasFlag(MessageFlags.Seen) == true,
                    BodyText: ExtractBodyText(mime))
                {
                    ToAddresses = ExtractAddresses(mime.To),
                    CcAddresses = ExtractAddresses(mime.Cc),
                    BccAddresses = ExtractAddresses(mime.Bcc),
                    ReplyToAddresses = ExtractAddresses(mime.ReplyTo),
                    BodyHtml = ExtractBodyHtml(mime),
                    IsFlagged = summary.Flags?.HasFlag(MessageFlags.Flagged) == true,
                    InternetMessageId = ExtractInternetMessageId(mime),
                    References = ExtractReferences(mime),
                    Attachments = ExtractAttachments(mime),
                    SizeBytes = SummarySize(summary),
                });
            }

            return messages;
        }
        catch (AuthenticationException ex)
        {
            throw new ImapAuthenticationException("IMAP authentication failed.", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ImapAuthenticationException and not ImapProtocolException)
        {
            throw new ImapProtocolException("IMAP protocol failure.", ex);
        }
    }

    public async Task SetSeenAsync(
        string mailboxPath,
        string remoteId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mailboxPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteId);
        var client = EnsureAuthenticated();

        try
        {
            if (!uint.TryParse(remoteId, out var uidValue))
            {
                throw new ImapProtocolException("IMAP protocol failure.", new FormatException($"RemoteId '{remoteId}' is not a UID."));
            }

            var folder = await client.GetFolderAsync(mailboxPath, cancellationToken).ConfigureAwait(false);
            await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken).ConfigureAwait(false);
            await folder
                .AddFlagsAsync(new UniqueId(uidValue), MessageFlags.Seen, silent: true, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (AuthenticationException ex)
        {
            throw new ImapAuthenticationException("IMAP authentication failed.", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ImapAuthenticationException and not ImapProtocolException)
        {
            throw new ImapProtocolException("IMAP protocol failure.", ex);
        }
    }

    public async Task SetUnseenAsync(
        string mailboxPath,
        string remoteId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mailboxPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteId);
        var client = EnsureAuthenticated();

        try
        {
            if (!uint.TryParse(remoteId, out var uidValue))
            {
                throw new ImapProtocolException("IMAP protocol failure.", new FormatException($"RemoteId '{remoteId}' is not a UID."));
            }

            var folder = await client.GetFolderAsync(mailboxPath, cancellationToken).ConfigureAwait(false);
            await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken).ConfigureAwait(false);
            await folder
                .RemoveFlagsAsync(new UniqueId(uidValue), MessageFlags.Seen, silent: true, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (AuthenticationException ex)
        {
            throw new ImapAuthenticationException("IMAP authentication failed.", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ImapAuthenticationException and not ImapProtocolException)
        {
            throw new ImapProtocolException("IMAP protocol failure.", ex);
        }
    }

    public async Task SetFlaggedAsync(
        string mailboxPath,
        string remoteId,
        bool flagged,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mailboxPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteId);
        var client = EnsureAuthenticated();
        try
        {
            if (!uint.TryParse(remoteId, out var uidValue))
            {
                throw new ImapProtocolException("IMAP protocol failure.", new FormatException($"RemoteId '{remoteId}' is not a UID."));
            }

            var folder = await client.GetFolderAsync(mailboxPath, cancellationToken).ConfigureAwait(false);
            await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken).ConfigureAwait(false);
            if (flagged)
            {
                await folder
                    .AddFlagsAsync(new UniqueId(uidValue), MessageFlags.Flagged, silent: true, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await folder
                    .RemoveFlagsAsync(new UniqueId(uidValue), MessageFlags.Flagged, silent: true, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (AuthenticationException ex)
        {
            throw new ImapAuthenticationException("IMAP authentication failed.", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ImapAuthenticationException and not ImapProtocolException)
        {
            throw new ImapProtocolException("IMAP protocol failure.", ex);
        }
    }

    public async Task<string?> MoveAsync(
        string sourceMailboxPath,
        string destinationMailboxPath,
        string remoteId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceMailboxPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationMailboxPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteId);
        var client = EnsureAuthenticated();
        try
        {
            if (!uint.TryParse(remoteId, out var uidValue))
            {
                throw new ImapProtocolException("IMAP protocol failure.", new FormatException($"RemoteId '{remoteId}' is not a UID."));
            }

            var source = await client.GetFolderAsync(sourceMailboxPath, cancellationToken).ConfigureAwait(false);
            await source.OpenAsync(FolderAccess.ReadWrite, cancellationToken).ConfigureAwait(false);
            var destination = await client.GetFolderAsync(destinationMailboxPath, cancellationToken).ConfigureAwait(false);
            var moved = await source
                .MoveToAsync(new UniqueId(uidValue), destination, cancellationToken)
                .ConfigureAwait(false);
            return moved is { IsValid: true } uid ? uid.Id.ToString() : null;
        }
        catch (AuthenticationException ex)
        {
            throw new ImapAuthenticationException("IMAP authentication failed.", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ImapAuthenticationException and not ImapProtocolException)
        {
            throw new ImapProtocolException("IMAP protocol failure.", ex);
        }
    }

    public async Task<string> CopyAsync(
        string sourceMailboxPath,
        string destinationMailboxPath,
        string remoteId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceMailboxPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationMailboxPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteId);
        var client = EnsureAuthenticated();
        try
        {
            if (!uint.TryParse(remoteId, out var uidValue))
            {
                throw new ImapProtocolException("IMAP protocol failure.", new FormatException($"RemoteId '{remoteId}' is not a UID."));
            }

            var sourceUid = new UniqueId(uidValue);
            var source = await client.GetFolderAsync(sourceMailboxPath, cancellationToken).ConfigureAwait(false);
            await source.OpenAsync(FolderAccess.ReadWrite, cancellationToken).ConfigureAwait(false);
            var destination = await client.GetFolderAsync(destinationMailboxPath, cancellationToken).ConfigureAwait(false);
            var destUid = await source.CopyToAsync(sourceUid, destination, cancellationToken).ConfigureAwait(false);
            if (destUid is not { Id: not 0 } copied)
            {
                throw new ImapProtocolException("IMAP COPY did not return a destination UID.");
            }

            return copied.Id.ToString();
        }
        catch (AuthenticationException ex)
        {
            throw new ImapAuthenticationException("IMAP authentication failed.", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ImapAuthenticationException and not ImapProtocolException)
        {
            throw new ImapProtocolException("IMAP protocol failure.", ex);
        }
    }

    public async Task<string> CreateMailboxAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var client = EnsureAuthenticated();
        try
        {
            IMailFolder parent;
            if (client.PersonalNamespaces.Count > 0)
            {
                parent = client.GetFolder(client.PersonalNamespaces[0]);
            }
            else
            {
                parent = client.Inbox;
            }

            var created = await parent
                .CreateAsync(name.Trim(), true, cancellationToken)
                .ConfigureAwait(false);
            if (created is null)
            {
                throw new ImapProtocolException("IMAP protocol failure.");
            }

            return created.FullName;
        }
        catch (AuthenticationException ex)
        {
            throw new ImapAuthenticationException("IMAP authentication failed.", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ImapAuthenticationException and not ImapProtocolException)
        {
            throw new ImapProtocolException("IMAP protocol failure.", ex);
        }
    }

    public async Task<string> RenameMailboxAsync(
        string mailboxPath,
        string newName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mailboxPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        var client = EnsureAuthenticated();
        try
        {
            var folder = await client.GetFolderAsync(mailboxPath, cancellationToken).ConfigureAwait(false);
            IMailFolder parent;
            if (folder.ParentFolder is not null)
            {
                parent = folder.ParentFolder;
            }
            else if (client.PersonalNamespaces.Count > 0)
            {
                parent = client.GetFolder(client.PersonalNamespaces[0]);
            }
            else
            {
                parent = client.Inbox;
            }

            await folder.RenameAsync(parent, newName.Trim(), cancellationToken).ConfigureAwait(false);
            return folder.FullName;
        }
        catch (AuthenticationException ex)
        {
            throw new ImapAuthenticationException("IMAP authentication failed.", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ImapAuthenticationException and not ImapProtocolException)
        {
            throw new ImapProtocolException("IMAP protocol failure.", ex);
        }
    }

    public async Task DeleteMailboxAsync(
        string mailboxPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mailboxPath);
        var client = EnsureAuthenticated();
        try
        {
            var folder = await client.GetFolderAsync(mailboxPath, cancellationToken).ConfigureAwait(false);
            await folder.DeleteAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (AuthenticationException ex)
        {
            throw new ImapAuthenticationException("IMAP authentication failed.", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ImapAuthenticationException and not ImapProtocolException)
        {
            throw new ImapProtocolException("IMAP protocol failure.", ex);
        }
    }

    public async Task ExpungeAllAsync(
        string mailboxPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mailboxPath);
        var client = EnsureAuthenticated();
        try
        {
            var folder = await client.GetFolderAsync(mailboxPath, cancellationToken).ConfigureAwait(false);
            await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken).ConfigureAwait(false);
            var uids = await folder.SearchAsync(SearchQuery.All, cancellationToken).ConfigureAwait(false);
            if (uids.Count > 0)
            {
                await folder
                    .AddFlagsAsync(uids, MessageFlags.Deleted, silent: true, cancellationToken)
                    .ConfigureAwait(false);
            }

            await folder.ExpungeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (AuthenticationException ex)
        {
            throw new ImapAuthenticationException("IMAP authentication failed.", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ImapAuthenticationException and not ImapProtocolException)
        {
            throw new ImapProtocolException("IMAP protocol failure.", ex);
        }
    }

    public async Task ExpungeAsync(
        string mailboxPath,
        string remoteId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mailboxPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteId);
        var client = EnsureAuthenticated();
        try
        {
            if (!uint.TryParse(remoteId, out var uidValue))
            {
                throw new ImapProtocolException("IMAP protocol failure.", new FormatException($"RemoteId '{remoteId}' is not a UID."));
            }

            var folder = await client.GetFolderAsync(mailboxPath, cancellationToken).ConfigureAwait(false);
            await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken).ConfigureAwait(false);
            var uid = new UniqueId(uidValue);
            await folder
                .AddFlagsAsync(uid, MessageFlags.Deleted, silent: true, cancellationToken)
                .ConfigureAwait(false);
            await folder.ExpungeAsync(new[] { uid }, cancellationToken).ConfigureAwait(false);
        }
        catch (AuthenticationException ex)
        {
            throw new ImapAuthenticationException("IMAP authentication failed.", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ImapAuthenticationException and not ImapProtocolException)
        {
            throw new ImapProtocolException("IMAP protocol failure.", ex);
        }
    }

    public async Task WaitForMailboxChangeAsync(
        string mailboxPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mailboxPath);
        var client = EnsureAuthenticated();
        try
        {
            var folder = await client.GetFolderAsync(mailboxPath, cancellationToken).ConfigureAwait(false);
            await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken).ConfigureAwait(false);

            var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<EventArgs> handler = (_, _) => arrived.TrySetResult();
            folder.CountChanged += handler;
            try
            {
                using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                using var registration = cancellationToken.Register(() => arrived.TrySetCanceled(cancellationToken));
                var idle = client.IdleAsync(idleCts.Token);
                await arrived.Task.ConfigureAwait(false);
                await idleCts.CancelAsync().ConfigureAwait(false);
                try
                {
                    await idle.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }
            finally
            {
                folder.CountChanged -= handler;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AuthenticationException ex)
        {
            throw new ImapAuthenticationException("IMAP authentication failed.", ex);
        }
        catch (Exception ex) when (ex is not ImapAuthenticationException)
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

    private static MailboxRole? MapRole(FolderAttributes attributes, string name)
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

        if (attributes.HasFlag(FolderAttributes.Archive)
            || string.Equals(name, "Archive", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Archives", StringComparison.OrdinalIgnoreCase))
        {
            return MailboxRole.Archive;
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

    private static string? ExtractBodyHtml(MimeMessage mime)
    {
        if (string.IsNullOrWhiteSpace(mime.HtmlBody))
        {
            return null;
        }

        return NormalizeBody(mime.HtmlBody);
    }

    private static long SummarySize(IMessageSummary summary) =>
        summary.Size is { } size ? size : 0;

    private static string? ExtractInternetMessageId(MimeMessage mime) =>
        string.IsNullOrWhiteSpace(mime.MessageId) ? null : mime.MessageId.Trim();

    private static IReadOnlyList<string> ExtractReferences(MimeMessage mime) =>
        mime.References
            .Select(id => id.Trim())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();

    private static string NormalizeBody(string? body) =>
        (body ?? string.Empty).TrimEnd('\r', '\n');

    private static IReadOnlyList<string> ExtractAddresses(InternetAddressList list) =>
        list.Mailboxes
            .Select(FormatMailbox)
            .Where(address => address.Length > 0)
            .ToList();

    private static string FormatMailbox(MailboxAddress? mailbox) =>
        mailbox is null || string.IsNullOrWhiteSpace(mailbox.Address)
            ? string.Empty
            : MailAddresses.Format(mailbox);
    private static IReadOnlyList<RemoteAttachment> ExtractAttachments(MimeMessage mime)
    {
        var attachments = new List<RemoteAttachment>();
        foreach (var part in mime.BodyParts.OfType<MimePart>())
        {
            var contentId = string.IsNullOrWhiteSpace(part.ContentId)
                ? null
                : part.ContentId.Trim().Trim('<', '>');
            if (!part.IsAttachment
                && string.IsNullOrEmpty(part.FileName)
                && string.IsNullOrEmpty(contentId))
            {
                continue;
            }

            if (!part.IsAttachment
                && (part.ContentType.IsMimeType("text", "plain")
                    || part.ContentType.IsMimeType("text", "html")))
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
                    FileName: part.FileName ?? (contentId is null ? "attachment" : "inline"),
                    ContentType: part.ContentType.MimeType,
                    Content: memory.ToArray())
                {
                    ContentId = contentId,
                });
        }

        return attachments;
    }
}
