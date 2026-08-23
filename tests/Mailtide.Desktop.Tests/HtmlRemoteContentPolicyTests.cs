using Mailtide.UI;

namespace Mailtide.Desktop.Tests;

[TestClass]
public sealed class HtmlRemoteContentPolicyTests
{
    [TestMethod]
    public void IsAllowed_permits_about_and_data()
    {
        Assert.IsTrue(HtmlRemoteContentPolicy.IsAllowed("about:blank"));
        Assert.IsTrue(HtmlRemoteContentPolicy.IsAllowed("data:text/html,hi"));
    }

    [TestMethod]
    public void IsAllowed_denies_remote_and_file_schemes()
    {
        Assert.IsFalse(HtmlRemoteContentPolicy.IsAllowed("https://tracker.example/pixel.gif"));
        Assert.IsFalse(HtmlRemoteContentPolicy.IsAllowed("http://example.com/x"));
        Assert.IsFalse(HtmlRemoteContentPolicy.IsAllowed("ws://example.com/s"));
        Assert.IsFalse(HtmlRemoteContentPolicy.IsAllowed("ftp://example.com/f"));
        Assert.IsFalse(HtmlRemoteContentPolicy.IsAllowed(@"file:///C:/secret"));
        Assert.IsFalse(HtmlRemoteContentPolicy.IsAllowed((Uri?)null));
    }

    [TestMethod]
    public void WrapForOfflineRender_injects_CSP_without_dropping_the_body()
    {
        var wrapped = HtmlRemoteContentPolicy.WrapForOfflineRender("<p>Hello <img src=\"https://x/y.png\"></p>");
        StringAssert.Contains(wrapped, HtmlRemoteContentPolicy.OfflineContentSecurityPolicy);
        StringAssert.Contains(wrapped, "<p>Hello <img src=\"https://x/y.png\"></p>");
    }
}
