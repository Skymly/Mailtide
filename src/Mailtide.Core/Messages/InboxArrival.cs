namespace Mailtide.Core;

/// <summary>
/// A newly stored unread Inbox-role Message the Person has not opened yet.
/// </summary>
public sealed record InboxArrival(
    Guid MessageId,
    Guid AccountId,
    Guid MailboxId,
    string Subject,
    string FromAddress,
    string AccountDisplayName);