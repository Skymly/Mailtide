namespace Mailtide.Core;

public sealed record MessageInfo(
    Guid Id,
    Guid AccountId,
    Guid MailboxId,
    string RemoteId,
    string Subject,
    string FromAddress,
    DateTimeOffset ReceivedAt,
    bool IsRead);
