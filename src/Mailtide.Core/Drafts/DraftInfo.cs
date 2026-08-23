namespace Mailtide.Core;

public sealed record DraftContent(
    IReadOnlyList<string> ToAddresses,
    string Subject,
    string BodyText)
{
    public IReadOnlyList<string> CcAddresses { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> BccAddresses { get; init; } = Array.Empty<string>();

    public string? BodyHtml { get; init; }
}

public sealed record DraftInfo(
    Guid Id,
    Guid AccountId,
    IReadOnlyList<string> ToAddresses,
    string Subject,
    string BodyText,
    DateTimeOffset UpdatedAt)
{
    public IReadOnlyList<string> CcAddresses { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> BccAddresses { get; init; } = Array.Empty<string>();

    public string? InReplyTo { get; init; }

    public IReadOnlyList<string> References { get; init; } = Array.Empty<string>();

    public string? BodyHtml { get; init; }
}
