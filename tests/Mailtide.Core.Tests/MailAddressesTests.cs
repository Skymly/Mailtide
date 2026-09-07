using Mailtide.Core;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class MailAddressesTests
{
    [TestMethod]
    public void Parse_splits_commas_and_semicolons()
    {
        CollectionAssert.AreEqual(
            new[] { "alice@example.com", "bob@example.com" },
            MailAddresses.Parse("alice@example.com, bob@example.com").ToArray());
        CollectionAssert.AreEqual(
            new[] { "alice@example.com", "bob@example.com" },
            MailAddresses.Parse("alice@example.com; bob@example.com").ToArray());
        Assert.IsEmpty(MailAddresses.Parse("  "));
    }

    [TestMethod]
    public void Parse_keeps_quoted_display_names_that_contain_commas()
    {
        var parsed = MailAddresses.Parse("\"Smith, Alice\" <alice@example.com>, bob@example.com");
        Assert.HasCount(2, parsed);
        StringAssert.Contains(parsed[0], "alice@example.com");
        StringAssert.Contains(parsed[0], "Smith");
        Assert.IsFalse(parsed[0].Contains("bob@example.com", StringComparison.Ordinal));
        Assert.AreEqual("bob@example.com", parsed[1]);
    }

    [TestMethod]
    public void Parse_keeps_angle_addr_display_names()
    {
        var parsed = MailAddresses.Parse("Alice <alice@example.com>");
        Assert.HasCount(1, parsed);
        StringAssert.Contains(parsed[0], "alice@example.com");
        StringAssert.Contains(parsed[0], "Alice");
    }

    [TestMethod]
    public void TryParseMailto_reads_to_subject_body_and_cc()
    {
        Assert.IsFalse(MailAddresses.TryParseMailto(new Uri("https://example.com"), out _));
        Assert.IsTrue(MailAddresses.TryParseMailto(new Uri("mailto:bob@example.com"), out var simple));
        Assert.AreEqual("bob@example.com", simple.To);
        Assert.AreEqual(string.Empty, simple.Subject);

        Assert.IsTrue(MailAddresses.TryParseMailto(
            new Uri("mailto:bob@example.com?subject=Hello%20there&body=Hi%20Bob&cc=carol@example.com"),
            out var full));
        Assert.AreEqual("bob@example.com", full.To);
        Assert.AreEqual("Hello there", full.Subject);
        Assert.AreEqual("Hi Bob", full.Body);
        Assert.AreEqual("carol@example.com", full.Cc);
    }

    [TestMethod]
    public void Invalid_flags_tokens_that_are_not_mailboxes()
    {
        CollectionAssert.AreEqual(
            new[] { "not-an-email" },
            MailAddresses.Invalid(["bob@example.com", "not-an-email"]).ToArray());
        Assert.IsEmpty(MailAddresses.Invalid(["Alice <alice@example.com>"]));
    }

    [TestMethod]
    public void Format_keeps_display_name_when_present()
    {
        Assert.AreEqual("bob@example.com", MailAddresses.Format(new MimeKit.MailboxAddress("", "bob@example.com")));
        var named = MailAddresses.Format(new MimeKit.MailboxAddress("Bob Example", "bob@example.com"));
        StringAssert.Contains(named, "bob@example.com");
        StringAssert.Contains(named, "Bob");
    }
}
