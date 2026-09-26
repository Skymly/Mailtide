namespace Mailtide.Core;

public sealed record AttachmentInfo(
    Guid Id,
    Guid MessageId,
    Guid AccountId,
    string FileName,
    string ContentType)
{
    public bool ContentOmitted { get; init; }

    public string ListLabel =>
        ContentOmitted ? FileName + " (omitted, over size limit)" : FileName;
}

public sealed record AttachmentContent(
    Guid Id,
    string FileName,
    string ContentType,
    byte[] Content);
