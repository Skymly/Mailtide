namespace Mailtide.Core;

public enum OutboxItemState
{
    Queued = 0,
    Sending = 1,
    Failed = 2,
}

public sealed record OutboxItemInfo(
    Guid Id,
    Guid AccountId,
    OutboxItemState State,
    string Subject,
    string? ErrorMessage,
    DateTimeOffset UpdatedAt);
