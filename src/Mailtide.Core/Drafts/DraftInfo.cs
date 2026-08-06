namespace Mailtide.Core;

public sealed record DraftContent(
    IReadOnlyList<string> ToAddresses,
    string Subject,
    string BodyText);

public sealed record DraftInfo(
    Guid Id,
    Guid AccountId,
    IReadOnlyList<string> ToAddresses,
    string Subject,
    string BodyText,
    DateTimeOffset UpdatedAt);
