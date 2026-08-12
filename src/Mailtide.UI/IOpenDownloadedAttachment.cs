namespace Mailtide.UI;

/// <summary>
/// Host port: write downloaded attachment bytes to a private temp location and open with the OS default app.
/// </summary>
public interface IOpenDownloadedAttachment
{
    Task OpenAsync(
        string fileName,
        string contentType,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default);
}
