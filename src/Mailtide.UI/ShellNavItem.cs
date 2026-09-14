using Mailtide.Core;

namespace Mailtide.UI;

public enum ShellNavKind
{
    UnifiedInbox = 0,
    Account = 1,
    Mailbox = 2,
    Outbox = 3,
    Favorites = 4,
}

public sealed class ShellNavItem
{
    public required ShellNavKind Kind { get; init; }

    public required string Title { get; init; }

    public Guid? AccountId { get; init; }

    public Guid? MailboxId { get; init; }

    public MailboxRole? Role { get; init; }

    public int UnreadCount { get; init; }

    public int OutboxCount { get; init; }

    public int OutboxFailedCount { get; init; }

    public AccountSyncState SyncState { get; init; }

    public IReadOnlyList<ShellNavItem> Children { get; init; } = [];

    public bool IsFavoritePin { get; init; }

    public bool IsExpanded { get; set; } = true;

    public bool HasUnread => UnreadCount > 0;

    public bool HasOutboxBadge => Kind == ShellNavKind.Outbox && OutboxCount > 0 && OutboxFailedCount == 0;

    public bool HasOutboxFailed => Kind == ShellNavKind.Outbox && OutboxFailedCount > 0;

    public bool IsSyncing => SyncState == AccountSyncState.Syncing;

    public bool IsError => SyncState == AccountSyncState.Error;
}
