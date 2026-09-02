using MailKit.Security;

namespace Mailtide.Core;

/// <summary>
/// Forced TLS for IMAP/SMTP. Plaintext is only allowed on loopback so protocol
/// contract tests can run without certificates. Remote servers never fall back
/// to opportunistic STARTTLS or cleartext.
/// </summary>
internal static class MailTls
{
    public static SecureSocketOptions SocketOptions(string host, int port)
    {
        if (IsLoopback(host))
        {
            return SecureSocketOptions.None;
        }

        return port switch
        {
            993 or 465 => SecureSocketOptions.SslOnConnect,
            _ => SecureSocketOptions.StartTls,
        };
    }

    public static bool IsLoopback(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("127.0.0.1", StringComparison.Ordinal)
            || host.Equals("::1", StringComparison.Ordinal))
        {
            return true;
        }

        return System.Net.IPAddress.TryParse(host, out var address)
            && System.Net.IPAddress.IsLoopback(address);
    }
}
