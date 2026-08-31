namespace Mailtide.UI;

public sealed record InboxArrivalNotification(
    Guid MessageId,
    Guid AccountId,
    Guid MailboxId,
    string Title,
    string Body,
    string? AccountDisplayName);