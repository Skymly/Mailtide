using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class CopyMessageTests
{
    [TestMethod]
    public async Task CopyMessage_duplicates_into_destination_and_keeps_source()
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

        var copyId = await app.CopyMessageAsync(account.Id, original.Id, archive.Id);

        Assert.AreEqual("INBOX", fixture.Imap.LastCopySourcePath);
        Assert.AreEqual("Archive", fixture.Imap.LastCopyDestinationPath);
        Assert.AreEqual("uid-1", fixture.Imap.LastCopyRemoteId);
        Assert.AreEqual(original.Id, (await app.ListMessagesAsync(account.Id, inbox.Id)).Single().Id);
        var copied = (await app.ListMessagesAsync(account.Id, archive.Id)).Single();
        Assert.AreEqual(copyId, copied.Id);
        Assert.AreNotEqual(original.Id, copied.Id);
        Assert.AreEqual("File me", copied.Subject);
        Assert.HasCount(1, await app.ListUnifiedInboxAsync());
    }

    [TestMethod]
    public async Task CopyMessage_same_Mailbox_is_a_no_op()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages("INBOX", Message("uid-1", "Stay"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();
        var original = (await app.ListMessagesAsync(account.Id, inbox.Id)).Single();

        var copyId = await app.CopyMessageAsync(account.Id, original.Id, inbox.Id);

        Assert.IsNull(fixture.Imap.LastCopyRemoteId);
        Assert.AreEqual(original.Id, copyId);
        Assert.HasCount(1, await app.ListMessagesAsync(account.Id, inbox.Id));
    }

    [TestMethod]
    public async Task CopyMessage_IMAP_failure_leaves_Message_in_place()
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

        fixture.Imap.FailWith = new ImapProtocolException("NO COPY");

        await Assert.ThrowsAsync<ImapProtocolException>(
            () => app.CopyMessageAsync(account.Id, original.Id, archive.Id));
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
