using Android.Content;
using Mailtide.Core;
using Mailtide.Core.Imap;
using Mailtide.Core.Smtp;
using Mailtide.Android.Host;

namespace Mailtide.Android;

internal static class AndroidComposition
{
    public static async Task<MailtideApp> OpenCoreAsync(
        Context context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var appData = context.FilesDir?.AbsolutePath
            ?? throw new SecureStorageException("Android app-private FilesDir is unavailable.");

        return await MailtideApp
            .OpenAsync(
                appData,
                AndroidSecureStorageFactory.Create(appData),
                new AndroidOidcOAuthClient(AndroidOAuthOptions.FromEnvironment()),
                new MailKitImapClientFactory(),
                new MailKitSmtpClientFactory(),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
