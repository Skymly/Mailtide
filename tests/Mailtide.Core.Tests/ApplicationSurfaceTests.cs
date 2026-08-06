using Mailtide.Core;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class ApplicationSurfaceTests
{
    [TestMethod]
    public void Application_surface_is_available_to_hosts()
    {
        var app = new MailtideApp();

        Assert.IsNotNull(app);
    }
}
