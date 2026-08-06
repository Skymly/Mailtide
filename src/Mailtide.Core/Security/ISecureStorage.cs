namespace Mailtide.Core.Security;

/// <summary>
/// Host-provided port for device-bound Credential secrets. No plaintext fallback in Core.
/// </summary>
public interface ISecureStorage
{
    Task StoreSecretAsync(string key, string secret, CancellationToken cancellationToken = default);

    Task<string?> RetrieveSecretAsync(string key, CancellationToken cancellationToken = default);

    Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default);
}
