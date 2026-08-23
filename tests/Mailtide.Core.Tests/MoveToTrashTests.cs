using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class MoveToTrashTests
{
    [TestMethod]
    public async Task MoveToTrash_refiles_Message_to_Trash_and_keeps_Id()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Trash", "Trash", MailboxRole.Trash));
        fixture.Imap.SeedMessages("INBOX", Message("uid-1", "Delete me"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Inbox);
        var trash = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Trash);
        var original = (await app.ListMessagesAsync(account.Id, inbox.Id)).Single();

        await app.MoveToTrashAsync(account.Id, original.Id);

        Assert.AreEqual("INBOX", fixture.Imap.LastMoveSourcePath);
        Assert.AreEqual("Trash", fixture.Imap.LastMoveDestinationPath);
        Assert.AreEqual("uid-1", fixture.Imap.LastMoveRemoteId);
        Assert.IsEmpty(await app.ListMessagesAsync(account.Id, inbox.Id));
        var moved = (await app.ListMessagesAsync(account.Id, trash.Id)).Single();
        Assert.AreEqual(original.Id, moved.Id);
        Assert.AreEqual("Delete me", moved.Subject);
    }

    [TestMethod]
    public async Task MoveToTrash_throws_when_Account_has_no_Trash_Mailbox()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages("INBOX", Message("uid-1", "Stuck"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();
        var message = (await app.ListMessagesAsync(account.Id, inbox.Id)).Single();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => app.MoveToTrashAsync(account.Id, message.Id));
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
