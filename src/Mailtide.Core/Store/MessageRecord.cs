namespace Mailtide.Core.Store;

internal sealed class MessageRecord
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Guid MailboxId { get; set; }

    public required string RemoteId { get; set; }

    public required string Subject { get; set; }

    public required string FromAddress { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    public bool IsRead { get; set; }

    public required string BodyText { get; set; }
}
