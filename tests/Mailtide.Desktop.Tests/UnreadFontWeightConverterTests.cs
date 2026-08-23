using Avalonia.Media;
using Mailtide.UI;

namespace Mailtide.Desktop.Tests;

[TestClass]
public sealed class UnreadFontWeightConverterTests
{
    [TestMethod]
    public void Unread_Message_maps_to_Bold()
    {
        var weight = UnreadFontWeightConverter.Instance.Convert(
            false,
            typeof(FontWeight),
            parameter: null,
            culture: null);

        Assert.AreEqual(FontWeight.Bold, weight);
    }

    [TestMethod]
    public void Read_Message_maps_to_Normal()
    {
        var weight = UnreadFontWeightConverter.Instance.Convert(
            true,
            typeof(FontWeight),
            parameter: null,
            culture: null);

        Assert.AreEqual(FontWeight.Normal, weight);
    }
}
