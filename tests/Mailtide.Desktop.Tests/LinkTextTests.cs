using Mailtide.UI;

namespace Mailtide.Desktop.Tests;

[TestClass]
public sealed class LinkTextTests
{
    [TestMethod]
    public void TryGetUri_accepts_http_mailto_and_bare_hosts()
    {
        Assert.IsTrue(LinkText.TryGetUri("https://example.com/a", out var https));
        Assert.AreEqual("https://example.com/a", https.AbsoluteUri);

        Assert.IsTrue(LinkText.TryGetUri("bob@example.com", out var mail));
        Assert.AreEqual("mailto:bob@example.com", mail.AbsoluteUri);

        Assert.IsTrue(LinkText.TryGetUri("www.example.com", out var www));
        Assert.AreEqual("https://www.example.com/", www.AbsoluteUri);

        Assert.IsFalse(LinkText.TryGetUri("not a link", out _));
        Assert.IsFalse(LinkText.TryGetUri("javascript:alert(1)", out _));
        Assert.IsFalse(LinkText.TryGetUri(@"file:///C:/secret", out _));
    }

    [TestMethod]
    public void TryGetUriAt_uses_the_token_under_the_caret()
    {
        const string text = "see https://example.com/a please";
        Assert.IsTrue(LinkText.TryGetUriAt(text, 10, out var uri));
        Assert.AreEqual("https://example.com/a", uri.AbsoluteUri);
        Assert.AreEqual("https://example.com/a", LinkText.TokenAt(text, text.IndexOf("example", StringComparison.Ordinal)));
        Assert.IsTrue(LinkText.TryGetUriAt(text, text.IndexOf(" please", StringComparison.Ordinal), out uri));
        Assert.IsFalse(LinkText.TryGetUriAt(text, 0, out _));
        Assert.IsFalse(LinkText.TryGetUriAt("  ", 1, out _));
    }
}
