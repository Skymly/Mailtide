using System.Runtime.Versioning;
using Mailtide.Core.Security;

namespace Mailtide.Desktop.Host;

/// <summary>
/// Linux Credential store via Freedesktop Secret Service (libsecret). No plaintext fallback.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class LibsecretSecureStorage : ISecureStorage, IDisposable
{
    private const string SchemaName = "org.mailtide.Credentials";
    private const string AttributeName = "key";
    private readonly IntPtr _schema;
    private bool _disposed;

    public LibsecretSecureStorage()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("LibsecretSecureStorage requires Linux.");
        }

        try
        {
            _schema = LibsecretNative.secret_schema_new(
                SchemaName,
                LibsecretNative.SchemaNone,
                AttributeName,
                LibsecretNative.SchemaAttributeString,
                IntPtr.Zero);
        }
        catch (DllNotFoundException ex)
        {
            throw new SecureStorageException(
                "libsecret is not available; Credential cannot be stored (no plaintext fallback).",
                ex);
        }

        if (_schema == IntPtr.Zero)
        {
            throw new SecureStorageException("Failed to create libsecret schema for Mailtide Credentials.");
        }
    }

    public Task StoreSecretAsync(string key, string secret, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(secret);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            var error = IntPtr.Zero;
            if (!LibsecretNative.secret_password_store_sync(
                    _schema,
                    LibsecretNative.DefaultCollection,
                    "Mailtide Credential",
                    secret,
                    IntPtr.Zero,
                    ref error,
                    AttributeName,
                    key,
                    IntPtr.Zero))
            {
                throw ToException(error, "Failed to store Credential in Secret Service.");
            }

            FreeError(error);
        }
        catch (DllNotFoundException ex)
        {
            throw Unavailable(ex, "stored");
        }

        return Task.CompletedTask;
    }

    public Task<string?> RetrieveSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);

        IntPtr passwordPtr = IntPtr.Zero;
        try
        {
            var error = IntPtr.Zero;
            passwordPtr = LibsecretNative.secret_password_lookup_sync(
                _schema,
                IntPtr.Zero,
                ref error,
                AttributeName,
                key,
                IntPtr.Zero);

            if (error != IntPtr.Zero)
            {
                throw ToException(error, "Failed to retrieve Credential from Secret Service.");
            }

            return Task.FromResult(LibsecretNative.ReadUtf8(passwordPtr));
        }
        catch (DllNotFoundException ex)
        {
            throw Unavailable(ex, "retrieved");
        }
        finally
        {
            if (passwordPtr != IntPtr.Zero)
            {
                LibsecretNative.secret_password_free(passwordPtr);
            }
        }
    }

    public Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            var error = IntPtr.Zero;
            // clear_sync returns false when nothing matched; that is success for Delete.
            if (!LibsecretNative.secret_password_clear_sync(
                    _schema,
                    IntPtr.Zero,
                    ref error,
                    AttributeName,
                    key,
                    IntPtr.Zero)
                && error != IntPtr.Zero)
            {
                throw ToException(error, "Failed to delete Credential from Secret Service.");
            }

            FreeError(error);
        }
        catch (DllNotFoundException ex)
        {
            throw Unavailable(ex, "deleted");
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_schema != IntPtr.Zero)
        {
            LibsecretNative.secret_schema_unref(_schema);
        }

        _disposed = true;
    }

    private static SecureStorageException Unavailable(DllNotFoundException ex, string action) =>
        new(
            $"libsecret is not available; Credential cannot be {action} (no plaintext fallback).",
            ex);

    private static void FreeError(IntPtr error)
    {
        if (error != IntPtr.Zero)
        {
            LibsecretNative.g_error_free(error);
        }
    }

    private static SecureStorageException ToException(IntPtr error, string fallbackMessage)
    {
        var message = LibsecretNative.ReadGErrorMessage(error) ?? fallbackMessage;
        FreeError(error);
        return new SecureStorageException(message);
    }
}
