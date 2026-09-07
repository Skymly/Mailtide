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

    Task SetUnseenAsync(
        string mailboxPath,
        string remoteId,
        CancellationToken cancellationToken = default);

    Task SetFlaggedAsync(
        string mailboxPath,
        string remoteId,
        bool flagged,
        CancellationToken cancellationToken = default);

    Task WaitForMailboxChangeAsync(
        string mailboxPath,
        CancellationToken cancellationToken = default);

    Task MoveAsync(
        string sourceMailboxPath,
        string destinationMailboxPath,
        string remoteId,
        CancellationToken cancellationToken = default);

    Task<string> CopyAsync(
        string sourceMailboxPath,
        string destinationMailboxPath,
        string remoteId,
        CancellationToken cancellationToken = default);

    Task<string> CreateMailboxAsync(
        string name,
        CancellationToken cancellationToken = default);

    Task<string> RenameMailboxAsync(
        string mailboxPath,
        string newName,
        CancellationToken cancellationToken = default);

    Task DeleteMailboxAsync(
        string mailboxPath,
        CancellationToken cancellationToken = default);

    Task ExpungeAllAsync(
        string mailboxPath,
        CancellationToken cancellationToken = default);

    Task ExpungeAsync(
        string mailboxPath,
        string remoteId,
        CancellationToken cancellationToken = default);
}

public sealed record RemoteMessageSummary(
    string RemoteId,
    bool IsRead,
    string Subject,
    string FromAddress,
    DateTimeOffset ReceivedAt,
    bool IsFlagged = false)
{
    public long SizeBytes { get; init; }
}

public sealed record RemoteMailbox(
    string Name,
    string Path,
    MailboxRole? Role)
{
    public uint UidValidity { get; init; }
}

public sealed record RemoteAttachment(
    string FileName,
    string ContentType,
    byte[] Content)
{
    public string? ContentId { get; init; }
}

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

    public IReadOnlyList<string> BccAddresses { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ReplyToAddresses { get; init; } = Array.Empty<string>();

    public string? BodyHtml { get; init; }

    public string? InternetMessageId { get; init; }

    public bool IsFlagged { get; init; }

    public IReadOnlyList<string> References { get; init; } = Array.Empty<string>();

    public long SizeBytes { get; init; }

    public IReadOnlyList<RemoteAttachment> Attachments { get; init; } =
        Array.Empty<RemoteAttachment>();
}
