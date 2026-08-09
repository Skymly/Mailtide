using Mailtide.Core.Security;

namespace Mailtide.Android.Host;

internal static class AndroidSecureStorageFactory
{
    public static ISecureStorage Create(string appDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataDirectory);
        return new KeystoreSecureStorage(appDataDirectory);
    }
}
