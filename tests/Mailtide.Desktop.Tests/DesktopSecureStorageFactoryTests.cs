using Mailtide.Core.Security;
using Mailtide.Desktop.Host;

namespace Mailtide.Desktop.Tests;

[TestClass]
public sealed class DesktopSecureStorageFactoryTests
{
    [TestMethod]
    public void Create_on_Windows_returns_DpapiSecureStorage()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("This assertion is Windows-specific.");
        }

        var appData = Path.Combine(Path.GetTempPath(), "mailtide-secure-factory", Guid.NewGuid().ToString("N"));
        try
        {
            ISecureStorage storage = DesktopSecureStorageFactory.Create(appData);
            Assert.AreEqual("DpapiSecureStorage", storage.GetType().Name);
        }
        finally
        {
            if (Directory.Exists(appData))
            {
                Directory.Delete(appData, recursive: true);
            }
        }
    }

    [TestMethod]
    public void Create_on_Linux_returns_LibsecretSecureStorage()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("This assertion is Linux-specific.");
        }

        ISecureStorage storage = DesktopSecureStorageFactory.Create(
            Path.Combine(Path.GetTempPath(), "mailtide-secure-factory-linux"));
        Assert.AreEqual("LibsecretSecureStorage", storage.GetType().Name);
    }
}
