using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class MoveToArchiveTests
{
    [TestMethod]
    public async Task MoveToArchive_refiles_Message_to_Archive_and_keeps_Id()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Archive", "Archive", MailboxRole.Archive));
        fixture.Imap.SeedMessages("INBOX", Message("uid-1", "Keep"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Inbox);
        var junk = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Archive);
        var original = (await app.ListMessagesAsync(account.Id, inbox.Id)).Single();

        await app.MoveToArchiveAsync(account.Id, original.Id);

        Assert.AreEqual("INBOX", fixture.Imap.LastMoveSourcePath);
        Assert.AreEqual("Archive", fixture.Imap.LastMoveDestinationPath);
        Assert.AreEqual("uid-1", fixture.Imap.LastMoveRemoteId);
        Assert.IsEmpty(await app.ListMessagesAsync(account.Id, inbox.Id));
        var moved = (await app.ListMessagesAsync(account.Id, junk.Id)).Single();
        Assert.AreEqual(original.Id, moved.Id);
        Assert.AreEqual("Keep", moved.Subject);
    }

    [TestMethod]
    public async Task MoveToArchive_throws_when_Account_has_no_Archive_Mailbox()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages("INBOX", Message("uid-1", "Stuck"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();
        var message = (await app.ListMessagesAsync(account.Id, inbox.Id)).Single();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => app.MoveToArchiveAsync(account.Id, message.Id));
        Assert.AreEqual("This Account has no Archive Mailbox.", ex.Message);
        Assert.HasCount(1, await app.ListMessagesAsync(account.Id, inbox.Id));
    }

    private static RemoteMessage Message(string remoteId, string subject) =>
        new(
            RemoteId: remoteId,
            Subject: subject,
            FromAddress: "bob@example.com",
            ReceivedAt: new DateTimeOffset(2026, 8, 8, 9, 0, 0, TimeSpan.Zero),
            IsRead: false,
            BodyText: "body");

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
