using Mailtide.Core;
using Mailtide.Core.Security;

namespace Mailtide.Core.Tests;

internal sealed class CoreAppFixture : IDisposable
{
    private readonly string _appDataDirectory =
        Path.Combine(Path.GetTempPath(), "mailtide-tests", Guid.NewGuid().ToString("N"));

    public FakeSecureStorage SecureStorage { get; } = new();

    public string AppDataDirectory => _appDataDirectory;

    public Task<MailtideApp> OpenAppAsync() =>
        MailtideApp.OpenAsync(_appDataDirectory, SecureStorage);

    public void Dispose()
    {
        if (Directory.Exists(_appDataDirectory))
        {
            Directory.Delete(_appDataDirectory, recursive: true);
        }
    }
}

internal sealed class FakeSecureStorage : ISecureStorage
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
