using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class MarkMailboxReadTests
{
    [TestMethod]
    public async Task MarkMailboxRead_marks_unread_Messages()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage("1", "A", "a@b.com", new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero), false, "x"),
            new RemoteMessage("2", "B", "a@b.com", new DateTimeOffset(2026, 8, 1, 8, 0, 0, TimeSpan.Zero), true, "y"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();
        Assert.AreEqual(1, inbox.UnreadCount);

        await app.MarkMailboxReadAsync(account.Id, inbox.Id);

        Assert.IsTrue((await app.ListMessagesAsync(account.Id, inbox.Id)).All(m => m.IsRead));
        Assert.AreEqual(0, (await app.ListMailboxesAsync(account.Id)).Single().UnreadCount);
        Assert.AreEqual("INBOX", fixture.Imap.LastSetSeenMailboxPath);
        Assert.AreEqual("1", fixture.Imap.LastSetSeenRemoteId);
    }

    private static ManualAccountDraft ValidDraft() =>
        new("Personal", "alice@example.com", "imap.example.com", 993, "smtp.example.com", 587, "s3cret-password");
}
