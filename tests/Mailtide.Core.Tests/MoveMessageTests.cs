using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class MoveMessageTests
{
    [TestMethod]
    public async Task MoveMessage_refiles_to_destination_Mailbox_and_keeps_Id()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Archive", "Archive", Role: null));
        fixture.Imap.SeedMessages("INBOX", Message("uid-1", "File me"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Inbox);
        var archive = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Name == "Archive");
        var original = (await app.ListMessagesAsync(account.Id, inbox.Id)).Single();

        await app.MoveMessageAsync(account.Id, original.Id, archive.Id);

        Assert.AreEqual("INBOX", fixture.Imap.LastMoveSourcePath);
        Assert.AreEqual("Archive", fixture.Imap.LastMoveDestinationPath);
        Assert.AreEqual("uid-1", fixture.Imap.LastMoveRemoteId);
        Assert.IsEmpty(await app.ListMessagesAsync(account.Id, inbox.Id));
        var moved = (await app.ListMessagesAsync(account.Id, archive.Id)).Single();
        Assert.AreEqual(original.Id, moved.Id);
        Assert.AreEqual("File me", moved.Subject);
        Assert.IsEmpty(await app.ListUnifiedInboxAsync());
    }

    [TestMethod]
    public async Task MoveMessage_same_Mailbox_is_a_no_op()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages("INBOX", Message("uid-1", "Stay"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();
        var original = (await app.ListMessagesAsync(account.Id, inbox.Id)).Single();

        await app.MoveMessageAsync(account.Id, original.Id, inbox.Id);

        Assert.IsNull(fixture.Imap.LastMoveRemoteId);
        Assert.HasCount(1, await app.ListMessagesAsync(account.Id, inbox.Id));
    }

    [TestMethod]
    public async Task MoveMessage_missing_or_other_Account_Mailbox_is_an_error()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Archive", "Archive", Role: null));
        fixture.Imap.SeedMessages("INBOX", Message("uid-1", "Nope"));

        await using var app = await fixture.OpenAppAsync();
        var accountA = await app.AddManualAccountAsync(ValidDraft("A", "a@example.com"));
        await app.SyncNowAsync(accountA.Id);
        var inboxA = (await app.ListMailboxesAsync(accountA.Id)).Single(m => m.Role == MailboxRole.Inbox);
        var message = (await app.ListMessagesAsync(accountA.Id, inboxA.Id)).Single();

        var accountB = await app.AddManualAccountAsync(ValidDraft("B", "b@example.com"));
        await app.SyncNowAsync(accountB.Id);
        var archiveB = (await app.ListMailboxesAsync(accountB.Id)).Single(m => m.Name == "Archive");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => app.MoveMessageAsync(accountA.Id, message.Id, Guid.NewGuid()));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => app.MoveMessageAsync(accountA.Id, message.Id, archiveB.Id));
        Assert.HasCount(1, await app.ListMessagesAsync(accountA.Id, inboxA.Id));
    }

    [TestMethod]
    public async Task MoveMessage_IMAP_failure_leaves_Message_in_place()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Archive", "Archive", Role: null));
        fixture.Imap.SeedMessages("INBOX", Message("uid-1", "Keep"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Inbox);
        var archive = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Name == "Archive");
        var original = (await app.ListMessagesAsync(account.Id, inbox.Id)).Single();

        fixture.Imap.FailWith = new ImapProtocolException("NO MOVE");

        await Assert.ThrowsAsync<ImapProtocolException>(
            () => app.MoveMessageAsync(account.Id, original.Id, archive.Id));
        Assert.HasCount(1, await app.ListMessagesAsync(account.Id, inbox.Id));
        Assert.IsEmpty(await app.ListMessagesAsync(account.Id, archive.Id));
    }

    private static RemoteMessage Message(string remoteId, string subject) =>
        new(
            RemoteId: remoteId,
            Subject: subject,
            FromAddress: "bob@example.com",
            ReceivedAt: new DateTimeOffset(2026, 8, 8, 9, 0, 0, TimeSpan.Zero),
            IsRead: false,
            BodyText: "body");

    private static ManualAccountDraft ValidDraft(string displayName = "Personal", string email = "alice@example.com") =>
        new(
            DisplayName: displayName,
            EmailAddress: email,
            ImapHost: "imap.example.com",
            ImapPort: 993,
            SmtpHost: "smtp.example.com",
            SmtpPort: 587,
            Password: "s3cret-password");
}