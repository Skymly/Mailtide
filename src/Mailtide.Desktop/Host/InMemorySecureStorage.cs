using Mailtide.Core.Security;

namespace Mailtide.Desktop.Host;

/// <summary>
/// Process-local secure storage until Desktop host adapters (#15) land.
/// </summary>
internal sealed class InMemorySecureStorage : ISecureStorage
{
    private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);

    public Task StoreSecretAsync(string key, string secret, CancellationToken cancellationToken = default)
    {
        _secrets[key] = secret;
        return Task.CompletedTask;
    }

    public Task<string?> RetrieveSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        _secrets.TryGetValue(key, out var secret);
        return Task.FromResult<string?>(secret);
    }

    public Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        _secrets.Remove(key);
        return Task.CompletedTask;
    }
}
