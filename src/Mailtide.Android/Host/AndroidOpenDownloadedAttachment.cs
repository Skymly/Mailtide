using Android.Content;
using AndroidX.Core.Content;
using Mailtide.UI;

namespace Mailtide.Android;

/// <summary>
/// Android Host: cache temp + FileProvider content URI + ACTION_VIEW.
/// </summary>
internal sealed class AndroidOpenDownloadedAttachment : IOpenDownloadedAttachment
{
    private readonly Context _context;

    public AndroidOpenDownloadedAttachment(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context.ApplicationContext ?? context;
    }

    public async Task OpenAsync(
        string fileName,
        string contentType,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        Java.IO.File? file = null;
        var handedOff = false;
        try
        {
            var cacheRoot = _context.CacheDir
                ?? throw new OpenAttachmentException("Could not open the attachment.");
            var dir = new Java.IO.File(cacheRoot, "mailtide-attachments");
            if (!dir.Exists() && !dir.Mkdirs())
            {
                throw new OpenAttachmentException("Could not open the attachment.");
            }

            var uniqueName = AttachmentTempFileNames.BuildUniqueFileName(fileName, contentType);
            file = new Java.IO.File(dir, uniqueName);
            await System.IO.File.WriteAllBytesAsync(file.AbsolutePath, content.ToArray(), cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var authority = $"{_context.PackageName}.fileprovider";
            var uri = FileProvider.GetUriForFile(_context, authority, file);
            var mime = string.IsNullOrWhiteSpace(contentType)
                ? "application/octet-stream"
                : contentType;

            var intent = new Intent(Intent.ActionView);
            intent.SetDataAndType(uri, mime);
            intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
            _context.StartActivity(intent);
            handedOff = true;
        }
        catch (OpenAttachmentException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new OpenAttachmentException("Could not open the attachment.", ex);
        }
        finally
        {
            if (file is not null && !handedOff)
            {
                try
                {
                    file.Delete();
                }
                catch
                {
                    // Best-effort cleanup only when launch did not hand off the URI.
                }
            }
        }
    }
}
