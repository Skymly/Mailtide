using Mailtide.Core;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class HtmlTextTests
{
    [TestMethod]
    public void Highlight_wraps_text_matches_without_touching_tags()
    {
        var html = "<p>Hello <b>world</b> hello</p>";
        var marked = HtmlText.Highlight(html, "HELLO");
        Assert.AreEqual("<p><mark>Hello</mark> <b>world</b> <mark>hello</mark></p>", marked);
        Assert.AreEqual(html, HtmlText.Highlight(html, " "));
        Assert.AreEqual(html, HtmlText.Highlight(html, null));
        Assert.AreEqual(
            "<a href=\"hello\">x</a>",
            HtmlText.Highlight("<a href=\"hello\">x</a>", "hello"));
        Assert.AreEqual(
            "<p><mark>Hello</mark> <b>world</b> <mark class=\"mailtide-current\">hello</mark></p>",
            HtmlText.Highlight(html, "HELLO", 1));
    }
}
