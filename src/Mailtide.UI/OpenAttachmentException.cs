namespace Mailtide.UI;

/// <summary>
/// Person-facing failure when an already-downloaded attachment cannot be opened via the Host.
/// UI catches this type and shows short copy — never raw platform messages.
/// </summary>
public sealed class OpenAttachmentException : Exception
{
    public OpenAttachmentException(string message)
        : base(message)
    {
    }

    public OpenAttachmentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
