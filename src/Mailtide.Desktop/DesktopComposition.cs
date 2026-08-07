using Mailtide.Core;
using Mailtide.Desktop.Host;

namespace Mailtide.Desktop;

internal static class DesktopComposition
{
    public static async Task<MailtideApp> OpenCoreAsync(CancellationToken cancellationToken = default)
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Mailtide");

        return await MailtideApp
            .OpenAsync(
                appData,
                new InMemorySecureStorage(),
                new PlaceholderImapClientFactory(),
                new PlaceholderSmtpClientFactory(),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
