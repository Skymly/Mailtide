using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class FlagMessageTests
{
    [TestMethod]
    public async Task MarkFlagged_sets_local_flag_and_IMAP()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "uid-f",
                Subject: "Star me",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 11, 9, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "x"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;
        var message = (await app.ListMessagesAsync(account.Id, mailboxId)).Single();
        Assert.IsFalse(message.IsFlagged);

        await app.MarkFlaggedAsync(account.Id, message.Id, flagged: true);

        Assert.IsTrue((await app.ListMessagesAsync(account.Id, mailboxId)).Single().IsFlagged);
        Assert.AreEqual("INBOX", fixture.Imap.LastSetFlaggedMailboxPath);
        Assert.AreEqual("uid-f", fixture.Imap.LastSetFlaggedRemoteId);
        Assert.IsTrue(fixture.Imap.LastSetFlaggedValue);
    }

    [TestMethod]
    public async Task Second_SyncNow_keeps_IsFlagged_from_summary()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "uid-f2",
                Subject: "Keep",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 11, 9, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "x")
            {
                IsFlagged = true,
            });

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        fixture.Imap.FetchedRemoteIds.Clear();
        await app.SyncNowAsync(account.Id);

        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;
        Assert.IsTrue((await app.ListMessagesAsync(account.Id, mailboxId)).Single().IsFlagged);
        Assert.AreEqual(0, fixture.Imap.FetchedRemoteIds.Count);
    }

    private static ManualAccountDraft ValidDraft() =>
        new(
            DisplayName: "Personal",
            EmailAddress: "alice@example.com",
            ImapHost: "imap.example.com",
            ImapPort: 993,
            SmtpHost: "smtp.example.com",
            SmtpPort: 587,
            Password: "s3cret-password");
}
