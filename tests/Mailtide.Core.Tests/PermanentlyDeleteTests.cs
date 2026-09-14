using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class PermanentlyDeleteTests
{
    [TestMethod]
    public async Task PermanentlyDeleteMessage_expunges_Trash_Message_and_keeps_others()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Trash", "Trash", MailboxRole.Trash));
        fixture.Imap.SeedMessages("INBOX", Message("keep-1", "Keep"));
        fixture.Imap.SeedMessages(
            "Trash",
            Message("trash-1", "Gone"),
            Message("trash-2", "Stay"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Inbox);
        var trash = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Trash);
        var gone = (await app.ListMessagesAsync(account.Id, trash.Id)).Single(m => m.Subject == "Gone");

        await app.PermanentlyDeleteMessageAsync(account.Id, gone.Id);

        Assert.AreEqual("Trash", fixture.Imap.LastExpungeMailboxPath);
        Assert.AreEqual("trash-1", fixture.Imap.LastExpungeRemoteId);
        var remaining = await app.ListMessagesAsync(account.Id, trash.Id);
        Assert.HasCount(1, remaining);
        Assert.AreEqual("Stay", remaining.Single().Subject);
        Assert.AreEqual("Keep", (await app.ListMessagesAsync(account.Id, inbox.Id)).Single().Subject);

        await app.SyncNowAsync(account.Id);
        remaining = await app.ListMessagesAsync(account.Id, trash.Id);
        Assert.HasCount(1, remaining);
        Assert.AreEqual("Stay", remaining.Single().Subject);
    }

    [TestMethod]
    public async Task PermanentlyDeleteMessage_expunges_Inbox_Message()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Trash", "Trash", MailboxRole.Trash));
        fixture.Imap.SeedMessages("INBOX", Message("uid-1", "Inbox"), Message("uid-2", "Keep"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Inbox);
        var gone = (await app.ListMessagesAsync(account.Id, inbox.Id)).Single(m => m.Subject == "Inbox");

        await app.PermanentlyDeleteMessageAsync(account.Id, gone.Id);

        Assert.AreEqual("INBOX", fixture.Imap.LastExpungeMailboxPath);
        Assert.AreEqual("uid-1", fixture.Imap.LastExpungeRemoteId);
        var remaining = await app.ListMessagesAsync(account.Id, inbox.Id);
        Assert.HasCount(1, remaining);
        Assert.AreEqual("Keep", remaining.Single().Subject);
        Assert.IsEmpty(await app.ListMessagesAsync(
            account.Id,
            (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Trash).Id));
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
