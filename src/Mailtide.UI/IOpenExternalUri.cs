namespace Mailtide.UI;

/// <summary>
/// Host port: open an http(s)/mailto URI with the OS default handler, not inside NativeWebView.
/// </summary>
public interface IOpenExternalUri
{
    Task OpenAsync(Uri uri, CancellationToken cancellationToken = default);
}
