using Mailtide.Desktop;
using Mailtide.UI;

namespace Mailtide.Desktop.Tests;

[TestClass]
public sealed class DesktopOpenDownloadedAttachmentTests
{
    [TestMethod]
    public async Task OpenAsync_writes_unique_temp_file_and_invokes_OS_open_boundary()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "MailtideTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string? openedPath = null;
        byte[]? bytesAtOpen = null;
        string? cleanupPath = null;

        try
        {
            var opener = new DesktopOpenDownloadedAttachment(
                tempDirectory: tempRoot,
                openPath: path =>
                {
                    openedPath = path;
                    bytesAtOpen = File.ReadAllBytes(path);
                },
                scheduleCleanup: path =>
                {
                    cleanupPath = path;
                    return Task.CompletedTask;
                });

            var payload = "hello-attachment"u8.ToArray();
            await opener.OpenAsync("report.pdf", "application/pdf", payload);

            Assert.IsNotNull(openedPath);
            Assert.StartsWith(tempRoot, openedPath!);
            Assert.Contains("-report.pdf", Path.GetFileName(openedPath!), StringComparison.Ordinal);
            CollectionAssert.AreEqual(payload, bytesAtOpen);
            Assert.AreEqual(openedPath, cleanupPath);
            Assert.IsTrue(File.Exists(openedPath!), "File should remain until deferred cleanup runs.");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [TestMethod]
    public void CreateLinuxOpenStartInfo_passes_path_with_spaces_as_single_argument()
    {
        var path = "/tmp/Mailtide/attachments/abcd-Q2 Report.pdf";
        var startInfo = DesktopOpenDownloadedAttachment.CreateLinuxOpenStartInfo(path);

        Assert.AreEqual("xdg-open", startInfo.FileName);
        Assert.IsFalse(startInfo.UseShellExecute);
        Assert.HasCount(1, startInfo.ArgumentList);
        Assert.AreEqual(path, startInfo.ArgumentList[0]);
    }

    [TestMethod]
    public async Task OpenAsync_sanitizes_path_segments_and_falls_back_filename()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "MailtideTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string? openedPath = null;

        try
        {
            var opener = new DesktopOpenDownloadedAttachment(
                tempDirectory: tempRoot,
                openPath: path => openedPath = path,
                scheduleCleanup: _ => Task.CompletedTask);

            await opener.OpenAsync(@"..\evil\..\", "text/plain", "x"u8.ToArray());

            var name = Path.GetFileName(openedPath!);
            Assert.DoesNotContain("..", name, StringComparison.Ordinal);
            Assert.Contains("attachment", name, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(".txt", name, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task OpenAsync_wraps_launch_failures_as_OpenAttachmentException()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "MailtideTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var opener = new DesktopOpenDownloadedAttachment(
                tempDirectory: tempRoot,
                openPath: _ => throw new InvalidOperationException("viewer missing"));

            var ex = await Assert.ThrowsAsync<OpenAttachmentException>(
                () => opener.OpenAsync("a.txt", "text/plain", "z"u8.ToArray()));

            Assert.AreEqual("Could not open the attachment.", ex.Message);
            Assert.IsInstanceOfType<InvalidOperationException>(ex.InnerException);
            Assert.IsFalse(Directory.EnumerateFiles(tempRoot).Any(), "Failed open should clean up temp immediately.");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
