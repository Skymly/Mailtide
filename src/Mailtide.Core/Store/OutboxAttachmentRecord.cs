namespace Mailtide.Core.Store;

internal sealed class OutboxAttachmentRecord
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Guid OutboxItemId { get; set; }

    public required string FileName { get; set; }

    public required string ContentType { get; set; }

    public required string BlobRelativePath { get; set; }
}
