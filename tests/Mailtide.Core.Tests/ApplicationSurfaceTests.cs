using Mailtide.Core;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class ApplicationSurfaceTests
{
    [TestMethod]
    public async Task Application_surface_is_available_to_hosts()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await MailtideApp.OpenAsync(
            fixture.AppDataDirectory,
            fixture.SecureStorage,
            fixture.Imap,
            fixture.Smtp);

        Assert.IsNotNull(app);
    }
}
