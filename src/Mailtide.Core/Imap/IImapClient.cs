namespace Mailtide.Core.Imap;

/// <summary>
/// Host/test-provided factory for protocol clients. Core never constructs real IMAP sockets.
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
}

public sealed record RemoteMailbox(
    string Name,
    string Path,
    MailboxRole? Role);

public sealed record RemoteMessage(
    string RemoteId,
    string Subject,
    string FromAddress,
    DateTimeOffset ReceivedAt,
    bool IsRead,
    string BodyText);
