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

    public string? ContentId { get; set; }

    /// <summary>
    /// True when the part was over the attachment size limit and no blob was stored.
    /// </summary>
    public bool ContentOmitted { get; set; }
}
