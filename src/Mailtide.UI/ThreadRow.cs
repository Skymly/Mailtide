using Mailtide.Core;

namespace Mailtide.UI;

public sealed class ThreadRow
{
    public required Guid Id { get; init; }

    public MessageThreadInfo? Thread { get; init; }

    public MessageInfo? SearchHit { get; init; }

    public OutboxItemInfo? OutboxItem { get; init; }

    public required string Sender { get; init; }

    public string FromAddress { get; init; } = string.Empty;

    public required string Subject { get; init; }

    public required string Preview { get; init; }

    public required string DateLabel { get; init; }

    public DateTimeOffset SortDate { get; init; }

    public bool IsGroupHeader { get; init; }

    public string? GroupTitle { get; init; }

    public bool ShowAsThread => !IsGroupHeader;

    public string? AccountBadge { get; init; }

    public bool ShowAccountBadge => !string.IsNullOrEmpty(AccountBadge);

    public bool IsUnread { get; init; }

    public bool IsRead => !IsUnread;

    public bool IsFlagged { get; init; }

    public bool CanFlag => !IsGroupHeader && OutboxItem is null;

    public string FlagLabel => IsFlagged ? "*" : "☆";

    public double FlagOpacity => IsFlagged ? 0.95 : 0.35;

    public bool HasAttachments { get; init; }

    public long SizeBytes { get; init; }

    public int MessageCount { get; init; } = 1;

    public bool ShowCount => MessageCount > 1;

    public string CountLabel => MessageCount.ToString();

    public string CountTip => $"{MessageCount} messages";

    public bool IsFailedOutbox => OutboxItem?.State == OutboxItemState.Failed;

    public string DateTip =>
        MailShellFormatting.DateTip(SortDate, DateLabel, SizeBytes);
}
