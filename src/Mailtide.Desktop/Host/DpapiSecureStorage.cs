using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Mailtide.Core.Security;

namespace Mailtide.Desktop.Host;

/// <summary>
/// Windows Credential store: DPAPI (ProtectedData, CurrentUser) ciphertext under app data.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class DpapiSecureStorage : ISecureStorage
{
    private readonly string _credentialsDirectory;

    public DpapiSecureStorage(string appDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataDirectory);
        _credentialsDirectory = Path.Combine(appDataDirectory, "credentials");
    }

    public Task StoreSecretAsync(string key, string secret, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(secret);
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(_credentialsDirectory);

        var plainBytes = Encoding.UTF8.GetBytes(secret);
        var cipherBytes = ProtectedData.Protect(plainBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(CredentialPath(key), cipherBytes);
        return Task.CompletedTask;
    }

    public Task<string?> RetrieveSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();

        var path = CredentialPath(key);
        if (!File.Exists(path))
        {
            return Task.FromResult<string?>(null);
        }

        var cipherBytes = File.ReadAllBytes(path);
        var plainBytes = ProtectedData.Unprotect(cipherBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Task.FromResult<string?>(Encoding.UTF8.GetString(plainBytes));
    }

    public Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();

        var path = CredentialPath(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string CredentialPath(string key)
    {
        // Credential handles are like "account:{guid}:credential" — hash keeps the path safe.
        var fileName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
        return Path.Combine(_credentialsDirectory, fileName);
    }
}
