namespace Mailtide.Android.Tests;

[TestClass]
public sealed class AndroidOpenAttachmentGuardTests
{
    [TestMethod]
    public void AndroidOpenDownloadedAttachment_refuses_unsafe_extensions()
    {
        var source = File.ReadAllText(
            Path.Combine(FindAndroidHostDirectory(), "AndroidOpenDownloadedAttachment.cs"));
        Assert.Contains("AttachmentTempFileNames.IsUnsafeToOpen", source, StringComparison.Ordinal);
        Assert.Contains("OpenAttachmentException", source, StringComparison.Ordinal);
    }

    private static string FindAndroidHostDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Mailtide.Android", "Host");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        Assert.Fail("Could not locate Mailtide.Android Host from the test output directory.");
        return null!;
    }
}
