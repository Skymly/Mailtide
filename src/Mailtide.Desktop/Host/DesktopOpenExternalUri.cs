using Mailtide.UI;

namespace Mailtide.Desktop.Host;

/// <summary>
/// Opens http(s)/mailto URIs with the OS default handler.
/// </summary>
public sealed class DesktopOpenExternalUri : IOpenExternalUri
{
    public Task OpenAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        cancellationToken.ThrowIfCancellationRequested();
        DesktopUpdateLauncher.OpenSystemUrl(uri.AbsoluteUri);
        return Task.CompletedTask;
    }
}
