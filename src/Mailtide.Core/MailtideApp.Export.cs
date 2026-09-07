using Mailtide.Core.Store;
using Microsoft.EntityFrameworkCore;

namespace Mailtide.Core;

public sealed partial class MailtideApp
{
    public async Task WriteMessageRfc822Async(
        Guid accountId,
        Guid messageId,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);

        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var message = await _db.Messages
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.AccountId == accountId && item.Id == messageId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (message is null)
            {
                throw new InvalidOperationException($"Message '{messageId}' was not found.");
            }

            var attachments = await _db.Attachments
                .AsNoTracking()
                .Where(item => item.AccountId == accountId && item.MessageId == messageId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var parts = new List<(string FileName, string ContentType, byte[] Content)>();
            foreach (var attachment in attachments)
            {
                var path = Path.Combine(_appDataDirectory, attachment.BlobRelativePath);
                if (!File.Exists(path))
                {
                    continue;
                }

                var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
                parts.Add((attachment.FileName, attachment.ContentType, bytes));
            }

            MessageRfc822.Write(
                destination,
                message.FromAddress,
                PackedStringList.Decode(message.ToAddresses),
                PackedStringList.Decode(message.CcAddresses),
                PackedStringList.Decode(message.BccAddresses),
                PackedStringList.Decode(message.ReplyToAddresses),
                message.Subject,
                message.ReceivedAt,
                message.BodyText,
                message.BodyHtml,
                message.InternetMessageId,
                PackedStringList.Decode(message.ReferencesJson),
                parts);
        }
        finally
        {
            _dbGate.Release();
        }
    }
}
