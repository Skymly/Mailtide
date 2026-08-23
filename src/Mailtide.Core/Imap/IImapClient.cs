namespace Mailtide.Core.Imap;

/// <summary>
/// Host/test-provided factory for protocol clients.
/// Core ships <see cref="MailKitImapClientFactory"/>; hosts wire it (or a fake in tests).
/// </summary>
public interface IImapClientFactory
{
    IImapClient Create();
}

/// <summary>
/// Protocol port for discovering Mailboxes and fetching Message snapshots.
/// </summary>
public interface IImapClient : IAsyncDisposable
{
    Task ConnectAndAuthenticateAsync(
        string host,
        int port,
        string username,
        string password,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RemoteMailbox>> ListMailboxesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RemoteMessage>> FetchMessagesAsync(
        string mailboxPath,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RemoteMessageSummary>> FetchMessageSummariesAsync(
        string mailboxPath,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RemoteMessage>> FetchMessagesAsync(
        string mailboxPath,
        IReadOnlyList<string> remoteIds,
        CancellationToken cancellationToken = default);

    Task SetSeenAsync(
        string mailboxPath,
        string remoteId,
        CancellationToken cancellationToken = default);
}

public sealed record RemoteMessageSummary(
    string RemoteId,
    bool IsRead,
    string Subject,
    string FromAddress,
    DateTimeOffset ReceivedAt);

public sealed record RemoteMailbox(
    string Name,
    string Path,
    MailboxRole? Role);

public sealed record RemoteAttachment(
    string FileName,
    string ContentType,
    byte[] Content);

public sealed record RemoteMessage(
    string RemoteId,
    string Subject,
    string FromAddress,
    DateTimeOffset ReceivedAt,
    bool IsRead,
    string BodyText)
{
    public IReadOnlyList<string> ToAddresses { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> CcAddresses { get; init; } = Array.Empty<string>();

    public string? BodyHtml { get; init; }

    public IReadOnlyList<RemoteAttachment> Attachments { get; init; } =
        Array.Empty<RemoteAttachment>();
}
