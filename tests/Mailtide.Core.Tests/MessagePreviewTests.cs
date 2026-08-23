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
        Assert.AreEqual(message.Preview, searched.Preview);
    }
}
