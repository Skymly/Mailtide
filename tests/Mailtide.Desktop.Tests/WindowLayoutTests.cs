using Mailtide.UI;

namespace Mailtide.Desktop.Tests;

[TestClass]
public sealed class WindowLayoutTests
{
    [TestMethod]
    public void Encode_round_trips_and_rejects_too_small()
    {
        var encoded = WindowLayout.Encode(40, 80, 1280.5, 800, true);
        Assert.IsTrue(WindowLayout.TryParse(encoded, out var x, out var y, out var width, out var height, out var maximized));
        Assert.AreEqual(40, x);
        Assert.AreEqual(80, y);
        Assert.AreEqual(1280.5, width);
        Assert.AreEqual(800, height);
        Assert.IsTrue(maximized);
        Assert.IsFalse(WindowLayout.TryParse("0,0,100,100,0", out _, out _, out _, out _, out _));
        Assert.IsFalse(WindowLayout.TryParse(null, out _, out _, out _, out _, out _));
    }

    [TestMethod]
    public void PaneLayout_round_trips_and_rejects_out_of_range()
    {
        var encoded = PaneLayout.Encode(200, 280.5);
        Assert.IsTrue(PaneLayout.TryParse(encoded, out var nav, out var list));
        Assert.AreEqual(200, nav);
        Assert.AreEqual(280.5, list);
        Assert.IsFalse(PaneLayout.TryParse("100,280", out _, out _));
        Assert.IsFalse(PaneLayout.TryParse(null, out _, out _));
    }
}
