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
        var wrapped = HtmlRemoteContentPolicy.WrapForOfflineRender(
            "<p>Hello <img src=\"https://x/y.png\"><img src=\"data:image/png;base64,abc\"></p>");
        StringAssert.Contains(wrapped, HtmlRemoteContentPolicy.OfflineContentSecurityPolicy);
        StringAssert.Contains(wrapped, "<style>mark{background:#ffe08a;color:inherit}mark.mailtide-current{background:#ffb347}</style>");
        StringAssert.Contains(wrapped, "<p>Hello");
        Assert.DoesNotContain("https://x/y.png", wrapped, StringComparison.Ordinal);
        StringAssert.Contains(wrapped, "data:image/png;base64,abc");
    }

    [TestMethod]
    public void IsExternalNavigation_allows_http_https_and_mailto_only()
    {
        Assert.IsTrue(HtmlRemoteContentPolicy.IsExternalNavigation("https://example.com/a"));
        Assert.IsTrue(HtmlRemoteContentPolicy.IsExternalNavigation("http://example.com/a"));
        Assert.IsTrue(HtmlRemoteContentPolicy.IsExternalNavigation("mailto:bob@example.com"));
        Assert.IsFalse(HtmlRemoteContentPolicy.IsExternalNavigation("about:blank"));
        Assert.IsFalse(HtmlRemoteContentPolicy.IsExternalNavigation("data:text/html,hi"));
        Assert.IsFalse(HtmlRemoteContentPolicy.IsExternalNavigation(@"file:///C:/secret"));
        Assert.IsFalse(HtmlRemoteContentPolicy.IsExternalNavigation("javascript:alert(1)"));
        Assert.IsFalse(HtmlRemoteContentPolicy.IsExternalNavigation((Uri?)null));
    }

    [TestMethod]
    public void HasRemoteImages_detects_http_img_sources()
    {
        Assert.IsTrue(HtmlRemoteContentPolicy.HasRemoteImages("<p><img src=\"https://x/y.png\"></p>"));
        Assert.IsFalse(HtmlRemoteContentPolicy.HasRemoteImages("<p><img src=\"data:image/png;base64,abc\"></p>"));
        Assert.IsFalse(HtmlRemoteContentPolicy.HasRemoteImages(null));
    }

    [TestMethod]
    public void WrapForOfflineRender_collapses_HTML_quotes_without_script()
    {
        Assert.IsTrue(HtmlRemoteContentPolicy.HasQuotedHtml("<blockquote>old</blockquote>"));
        Assert.IsTrue(HtmlRemoteContentPolicy.HasQuotedHtml("<div class='gmail_quote'>q</div>"));
        Assert.IsFalse(HtmlRemoteContentPolicy.HasQuotedHtml("<p>hello</p>"));
        Assert.IsFalse(HtmlRemoteContentPolicy.HasQuotedHtml(null));

        var quoted = HtmlRemoteContentPolicy.WrapForOfflineRender("<p>new</p><blockquote>old</blockquote>");
        StringAssert.Contains(quoted, HtmlRemoteContentPolicy.QuotedToggleId);
        StringAssert.Contains(quoted, "Show quoted text");
        StringAssert.Contains(quoted, "mailtide-html");
        StringAssert.Contains(quoted, "<p>new</p><blockquote>old</blockquote>");

        var plain = HtmlRemoteContentPolicy.WrapForOfflineRender("<p>hello</p>");
        Assert.IsFalse(plain.Contains(HtmlRemoteContentPolicy.QuotedToggleId, StringComparison.Ordinal));
        StringAssert.Contains(plain, "<p>hello</p>");

        var collapsed = HtmlRemoteContentPolicy.WrapForOfflineRender("<p>new</p><blockquote>old</blockquote>");
        Assert.IsFalse(collapsed.Contains("id=\"" + HtmlRemoteContentPolicy.QuotedToggleId + "\" checked", StringComparison.Ordinal));
        var expanded = HtmlRemoteContentPolicy.WrapForOfflineRender(
            "<p>new</p><blockquote>old</blockquote>",
            showQuoted: true);
        StringAssert.Contains(expanded, "id=\"" + HtmlRemoteContentPolicy.QuotedToggleId + "\" checked");
    }

    [TestMethod]
    public void WithoutQuotedHtml_strips_blockquote_and_gmail_quote()
    {
        var nested = "<p>Hello</p><blockquote><blockquote>old</blockquote>mid</blockquote>";
        Assert.AreEqual("<p>Hello</p>", HtmlRemoteContentPolicy.WithoutQuotedHtml(nested));

        var gmail = "<p>Hi</p><div class=\"gmail_quote\">On wrote:<blockquote>old</blockquote></div>";
        Assert.AreEqual("<p>Hi</p>", HtmlRemoteContentPolicy.WithoutQuotedHtml(gmail));

        Assert.AreEqual("<p>plain</p>", HtmlRemoteContentPolicy.WithoutQuotedHtml("<p>plain</p>"));
        Assert.IsTrue(HtmlRemoteContentPolicy.QuoteCollapsedHidesFind(
            "<p>Hello</p><blockquote>secret payload</blockquote>",
            "payload"));
        Assert.IsFalse(HtmlRemoteContentPolicy.QuoteCollapsedHidesFind(
            "<p>Hello payload</p><blockquote>old</blockquote>",
            "payload"));
        Assert.IsFalse(HtmlRemoteContentPolicy.QuoteCollapsedHidesFind("<p>Hello</p>", "payload"));
    }

    [TestMethod]
    public void MailShellView_wraps_HTML_through_offline_allow_list()
    {
        var root = FindRepoRoot();
        var view = File.ReadAllText(Path.Combine(root, "src", "Mailtide.UI", "MailShellView.axaml.cs"));
        Assert.Contains("HtmlRemoteContentPolicy.WrapForOfflineRender", view, StringComparison.Ordinal);
        Assert.Contains("HtmlRemoteContentPolicy.IsAllowed", view, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Mailtide.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        Assert.Fail("Could not locate repo root from the test output directory.");
        return null!;
    }
}
