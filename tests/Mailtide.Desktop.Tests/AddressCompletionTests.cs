using Mailtide.UI;

namespace Mailtide.Desktop.Tests;

[TestClass]
public sealed class AddressCompletionTests
{
    [TestMethod]
    public void CurrentToken_is_the_segment_after_the_last_separator()
    {
        Assert.AreEqual("bo", AddressCompletion.CurrentToken("alice@example.com, bo", 22).Token);
        Assert.AreEqual("bo", AddressCompletion.CurrentToken("alice@example.com; bo", 21).Token);
        Assert.AreEqual("\"Smith, Alice\"", AddressCompletion.CurrentToken("\"Smith, Alice\"", 14).Token);
        Assert.AreEqual("", AddressCompletion.CurrentToken("alice@example.com, ", 19).Token);
    }

    [TestMethod]
    public void Suggest_matches_recent_addresses_for_the_current_token()
    {
        var recent = new[] { "Bob <bob@example.com>", "carol@example.com", "alice@example.com" };
        CollectionAssert.AreEqual(
            new[] { "Bob <bob@example.com>" },
            AddressCompletion.Suggest("bo", recent).ToArray());
        Assert.IsEmpty(AddressCompletion.Suggest("bob@example.com", ["bob@example.com"]));
        Assert.IsEmpty(AddressCompletion.Suggest(" ", recent));
        CollectionAssert.AreEqual(
            new[] { "albert@example.com" },
            AddressCompletion.Suggest(
                "a",
                ["alice@example.com", "albert@example.com"],
                exclude: ["Alice <alice@example.com>"]).ToArray());
        var exclude = AddressCompletion.RecipientsToExclude(
            "carol@example.com, al",
            "carol@example.com, al".IndexOf("al", StringComparison.Ordinal),
            2,
            "Alice <alice@example.com>");
        CollectionAssert.AreEqual(
            new[] { "albert@example.com" },
            AddressCompletion.Suggest("al", ["alice@example.com", "albert@example.com"], exclude).ToArray());
    }

    [TestMethod]
    public void ReplaceCurrentToken_keeps_addresses_already_entered()
    {
        var replaced = AddressCompletion.ReplaceCurrentToken(
            "alice@example.com, bo",
            22,
            "Bob <bob@example.com>",
            out var caret);
        Assert.AreEqual("alice@example.com, Bob <bob@example.com>", replaced);
        Assert.AreEqual(replaced.Length, caret);

        var withSep = AddressCompletion.ReplaceCurrentToken(
            "bo",
            2,
            "Bob <bob@example.com>",
            out var sepCaret,
            appendSeparator: true);
        Assert.AreEqual("Bob <bob@example.com>, ", withSep);
        Assert.AreEqual(withSep.Length, sepCaret);

        var middle = AddressCompletion.ReplaceCurrentToken(
            "alice@example.com, bo, carol@example.com",
            21,
            "Bob <bob@example.com>",
            out _,
            appendSeparator: true);
        Assert.AreEqual("alice@example.com, Bob <bob@example.com>, carol@example.com", middle);
    }
}
