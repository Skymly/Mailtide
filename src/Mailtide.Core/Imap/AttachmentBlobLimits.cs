namespace Mailtide.Core.Imap;

internal static class AttachmentBlobLimits
{
    /// <summary>
    /// Decoded attachment parts larger than this are stored as a visible omission
    /// instead of a blob. 25 MiB keeps a single part from being decoded into the
    /// process working set during sync; typical documents and photos still land on disk.
    /// </summary>
    internal const int MaxDecodedBytes = 25 * 1024 * 1024;

    internal static bool ExceedsLimit(long decodedBytes) => decodedBytes > MaxDecodedBytes;
}
