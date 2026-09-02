using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class UidValidityTests
{
    [TestMethod]
    public async Task Sync_refetches_Mailbox_when_UIDVALIDITY_changes()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox) { UidValidity = 1 });
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "1",
                Subject: "Old UID space",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "first"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();
        var original = (await app.ListMessagesAsync(account.Id, inbox.Id)).Single();
        Assert.AreEqual("Old UID space", original.Subject);

        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox) { UidValidity = 2 });
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "1",
                Subject: "New UID space",
                FromAddress: "carol@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "second"));

        await app.SyncNowAsync(account.Id);
        var replaced = (await app.ListMessagesAsync(account.Id, inbox.Id)).Single();
        Assert.AreEqual("New UID space", replaced.Subject);
        Assert.AreEqual("carol@example.com", replaced.FromAddress);
        Assert.AreNotEqual(original.Id, replaced.Id);
        Assert.AreEqual(2, fixture.Imap.FetchedRemoteIds.Count);
    }

    [TestMethod]
    public async Task Sync_reuses_command_IMAP_session()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var created = fixture.Imap.CreateCount;
        await app.SyncNowAsync(account.Id);
        Assert.AreEqual(created, fixture.Imap.CreateCount);
        Assert.AreEqual(1, created);
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
