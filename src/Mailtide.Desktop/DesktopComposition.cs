using Mailtide.Core;
using Mailtide.Core.Auth;
using Mailtide.Core.Imap;
using Mailtide.Core.Smtp;
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
                new UnsupportedOAuthClient(),
                new MailKitImapClientFactory(),
                new MailKitSmtpClientFactory(),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
