namespace Mailtide.Core;

public sealed record DraftAttachmentInfo(
    Guid Id,
    Guid DraftId,
    Guid AccountId,
    string FileName,
    string ContentType);
