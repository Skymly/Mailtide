using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class MessagePreviewTests
{
    [TestMethod]
    public void FromBodyText_uses_first_line_and_collapses_spaces()
    {
        Assert.AreEqual("Hello world", MessagePreview.FromBodyText("  Hello   world  \nsecond"));
        Assert.AreEqual(string.Empty, MessagePreview.FromBodyText("   "));
    }

    [TestMethod]
    public void FromBodyText_caps_length()
    {
        var preview = MessagePreview.FromBodyText(new string('a', 90));
        Assert.AreEqual(MessagePreview.MaxLength + 1, preview.Length);
        Assert.EndsWith("…", preview);
    }

    [TestMethod]
    public async Task ListMessages_includes_Preview_from_BodyText()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "p1",
                Subject: "Sub",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 12, 9, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "First line of the body\nSecond"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(
            new ManualAccountDraft("Personal", "alice@example.com", "imap.example.com", 993, "smtp.example.com", 587, "s3cret-password"));
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;
        var message = (await app.ListMessagesAsync(account.Id, mailboxId)).Single();
        Assert.AreEqual("First line of the body", message.Preview);

        var unified = (await app.ListUnifiedInboxAsync()).Single();
        Assert.AreEqual(message.Preview, unified.Preview);

        var searched = (await app.SearchMessagesAsync(account.Id, mailboxId, "First")).Single();
        StringAssert.Contains(searched.Preview, "First");
    }

    [TestMethod]
    public void FromBodyText_windows_around_needle()
    {
        var buried = new string('x', 120) + " secret-token " + new string('y', 120);
        Assert.AreEqual(new string('x', MessagePreview.MaxLength) + "…", MessagePreview.FromBodyText(buried));
        var preview = MessagePreview.FromBodyText(buried, "secret-token");
        StringAssert.Contains(preview, "secret-token");
        Assert.IsTrue(preview.StartsWith('…'));
        Assert.IsTrue(preview.EndsWith('…'));
    }

    [TestMethod]
    public async Task SearchMessages_preview_windows_around_needle()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "p1",
                Subject: "Sub",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 12, 9, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: new string('x', 120) + " secret-token " + new string('y', 40)));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(
            new ManualAccountDraft("Personal", "alice@example.com", "imap.example.com", 993, "smtp.example.com", 587, "s3cret-password"));
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;
        var listed = (await app.ListMessagesAsync(account.Id, mailboxId)).Single();
        Assert.IsFalse(listed.Preview.Contains("secret-token", StringComparison.Ordinal));
        var searched = (await app.SearchMessagesAsync(account.Id, mailboxId, "secret-token")).Single();
        StringAssert.Contains(searched.Preview, "secret-token");
    }

    [TestMethod]
    public void FromBodyText_skips_quoted_reply_history()
    {
        Assert.AreEqual(
            "Thanks",
            MessagePreview.FromBodyText(
                "Thanks" + "\n\n" + "On 2026-01-01 10:00 UTC, Bob wrote:" + "\n\n" + "> old"));
    }
}
