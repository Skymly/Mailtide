using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class RestoreFromJunkTests
{
    [TestMethod]
    public async Task RestoreFromJunk_refiles_Message_to_Inbox_and_keeps_Id()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Junk", "Junk", MailboxRole.Junk));
        fixture.Imap.SeedMessages("INBOX", Message("uid-1", "Not spam"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Inbox);
        var junk = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Junk);
        var original = (await app.ListMessagesAsync(account.Id, inbox.Id)).Single();

        await app.MoveToJunkAsync(account.Id, original.Id);
        await app.RestoreFromJunkAsync(account.Id, original.Id);

        Assert.AreEqual("Junk", fixture.Imap.LastMoveSourcePath);
        Assert.AreEqual("INBOX", fixture.Imap.LastMoveDestinationPath);
        Assert.AreEqual("uid-1", fixture.Imap.LastMoveRemoteId);
        Assert.IsEmpty(await app.ListMessagesAsync(account.Id, junk.Id));
        var restored = (await app.ListMessagesAsync(account.Id, inbox.Id)).Single();
        Assert.AreEqual(original.Id, restored.Id);
        Assert.AreEqual("Not spam", restored.Subject);
    }

    [TestMethod]
    public async Task RestoreFromJunk_throws_when_Message_is_not_in_Junk()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Junk", "Junk", MailboxRole.Junk));
        fixture.Imap.SeedMessages("INBOX", Message("uid-1", "Still here"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Inbox);
        var message = (await app.ListMessagesAsync(account.Id, inbox.Id)).Single();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => app.RestoreFromJunkAsync(account.Id, message.Id));
        Assert.AreEqual("Message is not in Junk.", ex.Message);
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
