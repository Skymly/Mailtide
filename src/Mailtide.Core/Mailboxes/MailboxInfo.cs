namespace Mailtide.Core;

public enum MailboxRole
{
    Inbox = 0,
    Sent = 1,
    Drafts = 2,
    Trash = 3,
    Junk = 4,
}

public sealed record MailboxInfo(
    Guid Id,
    Guid AccountId,
    string Name,
    string Path,
    MailboxRole? Role);
