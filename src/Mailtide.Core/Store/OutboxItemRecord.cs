namespace Mailtide.Core.Store;

internal sealed class OutboxItemRecord
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public required string ToAddresses { get; set; }

    public required string Subject { get; set; }

    public required string BodyText { get; set; }

    public OutboxItemState State { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
