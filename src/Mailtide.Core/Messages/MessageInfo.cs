namespace Mailtide.Core;

public sealed record MessageInfo(
    Guid Id,
    Guid AccountId,
    Guid MailboxId,
    string RemoteId,
    string Subject,
    string FromAddress,
    DateTimeOffset ReceivedAt,
    bool IsRead,
    bool IsFlagged = false)
{
    public string Preview { get; init; } = string.Empty;

    public IReadOnlyList<string> ToAddresses { get; init; } = [];

    public IReadOnlyList<string> CcAddresses { get; init; } = [];

    public IReadOnlyList<string> BccAddresses { get; init; } = [];

    public IReadOnlyList<string> ReplyToAddresses { get; init; } = [];

    public bool HasAttachments { get; init; }

    public long SizeBytes { get; init; }

    public string? InternetMessageId { get; init; }
}
