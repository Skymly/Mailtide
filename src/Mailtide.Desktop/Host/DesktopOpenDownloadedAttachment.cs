using System.Diagnostics;
using System.Runtime.InteropServices;
using Mailtide.UI;

namespace Mailtide.Desktop;

/// <summary>
/// Desktop Host: private temp file + OS default associated app. Open path is injectable for tests.
/// </summary>
internal sealed class DesktopOpenDownloadedAttachment : IOpenDownloadedAttachment
{
    private readonly string _tempDirectory;
    private readonly Action<string> _openPath;
    private readonly Func<string, Task>? _scheduleCleanup;

    public DesktopOpenDownloadedAttachment(
        string? tempDirectory = null,
        Action<string>? openPath = null,
        Func<string, Task>? scheduleCleanup = null)
    {
        _tempDirectory = tempDirectory
            ?? Path.Combine(Path.GetTempPath(), "Mailtide", "attachments");
        _openPath = openPath ?? OpenWithOsDefault;
        _scheduleCleanup = scheduleCleanup;
    }

    public async Task OpenAsync(
        string fileName,
        string contentType,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        string? path = null;
        try
        {
            Directory.CreateDirectory(_tempDirectory);
            var uniqueName = AttachmentTempFileNames.BuildUniqueFileName(fileName, contentType);
            path = Path.Combine(_tempDirectory, uniqueName);
            await File.WriteAllBytesAsync(path, content.ToArray(), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            _openPath(path);
            ScheduleBestEffortCleanup(path);
            path = null;
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
            if (path is not null)
            {
                TryDelete(path);
            }
        }
    }

    private void ScheduleBestEffortCleanup(string path)
    {
        if (_scheduleCleanup is not null)
        {
            _ = _scheduleCleanup(path);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                TryDelete(path);
            }
            catch
            {
                // Best-effort cleanup after the OS has had time to open the file.
            }
        });
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best-effort.
        }
    }

    private static void OpenWithOsDefault(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Process.Start(new ProcessStartInfo("xdg-open", path) { UseShellExecute = false });
            return;
        }

        throw new PlatformNotSupportedException("Opening attachments is only supported on Windows and Linux.");
    }
}
