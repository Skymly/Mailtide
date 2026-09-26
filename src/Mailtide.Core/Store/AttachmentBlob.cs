using Mailtide.Core.Imap;

namespace Mailtide.Core.Store;

internal static class AttachmentBlob
{
    internal static async Task<AttachmentRecord> StoreAsync(
        Guid accountId,
        Guid messageId,
        RemoteAttachment remote,
        string appDataDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(remote);
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataDirectory);

        var attachmentId = Guid.NewGuid();
        var blobRelativePath = Path.Combine(
            "accounts",
            accountId.ToString("D"),
            "blobs",
            attachmentId.ToString("D"));
        var absolutePath = Path.Combine(appDataDirectory, blobRelativePath);
        var wrote = await TryWriteAsync(remote, absolutePath, cancellationToken).ConfigureAwait(false);
        return new AttachmentRecord
        {
            Id = attachmentId,
            AccountId = accountId,
            MessageId = messageId,
            FileName = remote.FileName,
            ContentType = remote.ContentType,
            BlobRelativePath = blobRelativePath,
            ContentId = remote.ContentId,
            ContentOmitted = !wrote,
        };
    }

    internal static async Task<bool> TryWriteAsync(
        RemoteAttachment attachment,
        string absolutePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        if (ShouldOmitWithoutWriting(attachment))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (attachment.WriteContentAsync is not null)
        {
            var omitted = false;
            await using (var file = new FileStream(
                absolutePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous))
            {
                var bounded = new BoundedWriteStream(file, AttachmentBlobLimits.MaxDecodedBytes);
                try
                {
                    await attachment.WriteContentAsync(bounded, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsDecodeLimit(ex))
                {
                    omitted = true;
                }

                if (bounded.Exceeded)
                {
                    omitted = true;
                }
            }

            if (omitted && File.Exists(absolutePath))
            {
                File.Delete(absolutePath);
            }

            return !omitted;
        }

        await File.WriteAllBytesAsync(absolutePath, attachment.Content, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    private static bool ShouldOmitWithoutWriting(RemoteAttachment attachment)
    {
        if (attachment.ContentOmitted)
        {
            return true;
        }

        if (attachment.DeclaredDecodedBytes is long declared
            && AttachmentBlobLimits.ExceedsLimit(declared))
        {
            return true;
        }

        return attachment.WriteContentAsync is null
            && attachment.Content.LongLength > AttachmentBlobLimits.MaxDecodedBytes;
    }

    private static bool IsDecodeLimit(Exception exception) =>
        exception is AttachmentDecodeLimitException
        || exception.InnerException is AttachmentDecodeLimitException;

    private sealed class AttachmentDecodeLimitException : Exception;

    private sealed class BoundedWriteStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _limit;
        private long _written;

        public BoundedWriteStream(Stream inner, long limit)
        {
            _inner = inner;
            _limit = limit;
        }

        public bool Exceeded { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => _written;

        public override long Position
        {
            get => _written;
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            _inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length == 0)
            {
                return;
            }

            if (Exceeded || _written + buffer.Length > _limit)
            {
                Exceeded = true;
                throw new AttachmentDecodeLimitException();
            }

            _inner.Write(buffer);
            _written += buffer.Length;
        }

        public override void WriteByte(byte value)
        {
            Span<byte> one = stackalloc byte[1];
            one[0] = value;
            Write(one);
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (buffer.Length == 0)
            {
                return ValueTask.CompletedTask;
            }

            if (Exceeded || _written + buffer.Length > _limit)
            {
                Exceeded = true;
                throw new AttachmentDecodeLimitException();
            }

            _written += buffer.Length;
            return _inner.WriteAsync(buffer, cancellationToken);
        }
    }
}
