namespace Mailtide.Core;

public enum OutboxItemState
{
    Queued = 0,
    Sending = 1,
    Failed = 2,
}

public enum OutboxAttemptKind
{
    Queued = 0,
    Attempted = 1,
    Failed = 2,
    Requeued = 3,
}

public sealed record OutboxAttemptInfo(DateTimeOffset At, OutboxAttemptKind Kind, string? Error);

public sealed record OutboxItemInfo(
    Guid Id,
    Guid AccountId,
    OutboxItemState State,
    string Subject,
    string? ErrorMessage,
    DateTimeOffset UpdatedAt)
{
    public IReadOnlyList<string> ToAddresses { get; init; } = [];

    public string BodyText { get; init; } = string.Empty;

    public IReadOnlyList<OutboxAttemptInfo> Attempts { get; init; } = [];
}
