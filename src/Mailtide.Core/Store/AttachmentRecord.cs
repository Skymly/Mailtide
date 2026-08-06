namespace Mailtide.Core.Store;

internal sealed class AttachmentRecord
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Guid MessageId { get; set; }

    public required string FileName { get; set; }

    public required string ContentType { get; set; }

    /// <summary>
    /// Path relative to the app-data directory for the blob file on disk.
    /// </summary>
    public required string BlobRelativePath { get; set; }
}
