namespace Mailtide.Desktop.Host;

/// <summary>
/// Raised when the Host secure-storage backend cannot complete a Credential operation.
/// There is no plaintext fallback path.
/// </summary>
internal sealed class SecureStorageException : Exception
{
    public SecureStorageException(string message)
        : base(message)
    {
    }

    public SecureStorageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
