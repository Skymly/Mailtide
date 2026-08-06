namespace Mailtide.Core;

public sealed record AttachmentInfo(
    Guid Id,
    Guid MessageId,
    Guid AccountId,
    string FileName,
    string ContentType);

public sealed record AttachmentContent(
    Guid Id,
    string FileName,
    string ContentType,
    byte[] Content);
