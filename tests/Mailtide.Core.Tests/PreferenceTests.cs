using Mailtide.Core;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class PreferenceTests
{
    [TestMethod]
    public async Task Preference_round_trips_and_overwrites()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await fixture.OpenAppAsync();

        Assert.IsNull(await app.GetPreferenceAsync("browse.listNewestFirst"));

        await app.SetPreferenceAsync("browse.listNewestFirst", "0");
        Assert.AreEqual("0", await app.GetPreferenceAsync("browse.listNewestFirst"));

        await app.SetPreferenceAsync("browse.listNewestFirst", "1");
        Assert.AreEqual("1", await app.GetPreferenceAsync("browse.listNewestFirst"));
        Assert.IsNull(await app.GetPreferenceAsync("browse.nav"));
    }
}
