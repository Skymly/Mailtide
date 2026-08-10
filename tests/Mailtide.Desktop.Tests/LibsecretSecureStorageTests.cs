using Mailtide.Desktop.Host;

namespace Mailtide.Desktop.Tests;

[TestClass]
public sealed class LibsecretSecureStorageTests
{
    [TestMethod]
    public async Task Store_then_Retrieve_returns_the_Credential_secret()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("libsecret secure storage is Linux-only.");
        }

        LibsecretStorageHandle? storage = null;
        var key = $"account:libsecret-test-{Guid.NewGuid():N}:credential";

        try
        {
            storage = CreateLinuxStorage();
            await storage.StoreSecretAsync(key, "linux-s3cret");
            Assert.AreEqual("linux-s3cret", await storage.RetrieveSecretAsync(key));
        }
        catch (DllNotFoundException)
        {
            Assert.Inconclusive("libsecret is not available in this environment.");
        }
        catch (SecureStorageException ex) when (ex.Message.Contains("Secret Service", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("libsecret", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Inconclusive("Secret Service is not available in this environment.");
        }
        finally
        {
            if (storage is not null)
            {
                try
                {
                    await storage.DeleteSecretAsync(key);
                }
                catch
                {
                    // Best-effort cleanup when the service is missing or locked.
                }

                await storage.DisposeAsync();
            }
        }
    }

    [TestMethod]
    public async Task Delete_removes_the_Credential_so_Retrieve_returns_null()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("libsecret secure storage is Linux-only.");
        }

        LibsecretStorageHandle? storage = null;
        var key = $"account:libsecret-delete-{Guid.NewGuid():N}:credential";

        try
        {
            storage = CreateLinuxStorage();
            await storage.StoreSecretAsync(key, "remove-me-linux");
            await storage.DeleteSecretAsync(key);
            Assert.IsNull(await storage.RetrieveSecretAsync(key));
        }
        catch (DllNotFoundException)
        {
            Assert.Inconclusive("libsecret is not available in this environment.");
        }
        catch (SecureStorageException ex) when (ex.Message.Contains("Secret Service", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("libsecret", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Inconclusive("Secret Service is not available in this environment.");
        }
        finally
        {
            if (storage is not null)
            {
                await storage.DisposeAsync();
            }
        }
    }

    [TestMethod]
    public void Missing_backend_has_no_plaintext_fallback_type()
    {
        // Guardrail: Host assembly must expose libsecret adapter, never a plaintext store.
        var assembly = typeof(DesktopSecureStorageFactory).Assembly;
        Assert.IsNotNull(assembly.GetType("Mailtide.Desktop.Host.LibsecretSecureStorage"));
        Assert.IsNull(assembly.GetType("Mailtide.Desktop.Host.InMemorySecureStorage"));
    }

    private static LibsecretStorageHandle CreateLinuxStorage() =>
        new(DesktopSecureStorageFactory.Create(Path.GetTempPath()));

    /// <summary>
    /// Holds the Linux adapter as <see cref="Mailtide.Core.Security.ISecureStorage"/> so tests
    /// do not construct the OS-specific type directly (avoids CA1416 on Windows builds).
    /// </summary>
    private sealed class LibsecretStorageHandle : IAsyncDisposable
    {
        private readonly Mailtide.Core.Security.ISecureStorage _storage;

        public LibsecretStorageHandle(Mailtide.Core.Security.ISecureStorage storage) =>
            _storage = storage;

        public Task StoreSecretAsync(string key, string secret) =>
            _storage.StoreSecretAsync(key, secret);

        public Task<string?> RetrieveSecretAsync(string key) =>
            _storage.RetrieveSecretAsync(key);

        public Task DeleteSecretAsync(string key) =>
            _storage.DeleteSecretAsync(key);

        public ValueTask DisposeAsync()
        {
            if (_storage is IDisposable disposable)
            {
                disposable.Dispose();
            }

            return ValueTask.CompletedTask;
        }
    }
}
