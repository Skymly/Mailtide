using System.Runtime.Versioning;
using Mailtide.Core.Security;

namespace Mailtide.Desktop.Host;

/// <summary>
/// Creates the OS-backed Credential store for the Desktop Host. No plaintext fallback.
/// </summary>
internal static class DesktopSecureStorageFactory
{
    public static ISecureStorage Create(string appDataDirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            return CreateWindows(appDataDirectory);
        }

        if (OperatingSystem.IsLinux())
        {
            return CreateLinux();
        }

        throw new PlatformNotSupportedException(
            "Desktop secure storage requires Windows DPAPI or Linux Secret Service (libsecret).");
    }

    [SupportedOSPlatform("windows")]
    private static ISecureStorage CreateWindows(string appDataDirectory) =>
        new DpapiSecureStorage(appDataDirectory);

    [SupportedOSPlatform("linux")]
    private static ISecureStorage CreateLinux() =>
        new LibsecretSecureStorage();
}
