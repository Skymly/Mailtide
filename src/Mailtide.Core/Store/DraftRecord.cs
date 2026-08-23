namespace Mailtide.Core.Store;

internal sealed class DraftRecord
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public required string ToAddresses { get; set; }

    public string CcAddresses { get; set; } = "[]";

    public string BccAddresses { get; set; } = "[]";

    public required string Subject { get; set; }

    public required string BodyText { get; set; }

    public string? BodyHtml { get; set; }

    public string? InReplyTo { get; set; }

    public string ReferencesJson { get; set; } = "[]";

    public DateTimeOffset UpdatedAt { get; set; }
}
