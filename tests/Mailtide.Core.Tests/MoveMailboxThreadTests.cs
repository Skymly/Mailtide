using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class MoveMailboxThreadTests
{
    [TestMethod]
    public async Task MoveMailboxThread_refiles_every_Message_in_the_reply_chain()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Archive", "Archive", Role: null));
        fixture.Imap.SeedMessages(
            "INBOX",
            Root(),
            Reply());
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Inbox);
        var archive = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Name == "Archive");
        var thread = (await app.ListMailboxThreadsAsync(account.Id, inbox.Id)).Single();

        await app.MoveMailboxThreadAsync(account.Id, inbox.Id, thread.Latest.Id, archive.Id);

        Assert.IsEmpty(await app.ListMessagesAsync(account.Id, inbox.Id));
        var moved = await app.ListMessagesAsync(account.Id, archive.Id);
        Assert.HasCount(2, moved);
        CollectionAssert.AreEquivalent(
            thread.Messages.Select(m => m.Id).ToArray(),
            moved.Select(m => m.Id).ToArray());
    }

    [TestMethod]
    public async Task MoveMailboxThread_singleton_moves_the_one_Message()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Archive", "Archive", Role: null));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-1",
                Subject: "Alone",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "hi"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Inbox);
        var archive = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Name == "Archive");
        var thread = (await app.ListMailboxThreadsAsync(account.Id, inbox.Id)).Single();

        await app.MoveMailboxThreadAsync(account.Id, inbox.Id, thread.Latest.Id, archive.Id);

        Assert.IsEmpty(await app.ListMessagesAsync(account.Id, inbox.Id));
        Assert.HasCount(1, await app.ListMessagesAsync(account.Id, archive.Id));
    }

    [TestMethod]
    public async Task MoveMailboxThread_IMAP_failure_leaves_every_Message_in_place()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Archive", "Archive", Role: null));
        fixture.Imap.SeedMessages("INBOX", Root(), Reply());
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Inbox);
        var archive = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Name == "Archive");
        var thread = (await app.ListMailboxThreadsAsync(account.Id, inbox.Id)).Single();

        fixture.Imap.FailWith = new ImapProtocolException("NO MOVE");

        await Assert.ThrowsAsync<ImapProtocolException>(
            () => app.MoveMailboxThreadAsync(account.Id, inbox.Id, thread.Latest.Id, archive.Id));
        Assert.HasCount(2, await app.ListMessagesAsync(account.Id, inbox.Id));
        Assert.IsEmpty(await app.ListMessagesAsync(account.Id, archive.Id));
    }

    private static RemoteMessage Root() =>
        new(
            RemoteId: "m-1",
            Subject: "Root",
            FromAddress: "bob@example.com",
            ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
            IsRead: true,
            BodyText: "root")
        {
            InternetMessageId = "<root@example.com>",
        };

    private static RemoteMessage Reply() =>
        new(
            RemoteId: "m-2",
            Subject: "Re: Root",
            FromAddress: "alice@example.com",
            ReceivedAt: new DateTimeOffset(2026, 4, 1, 11, 0, 0, TimeSpan.Zero),
            IsRead: false,
            BodyText: "reply")
        {
            InternetMessageId = "<reply@example.com>",
            References = ["<root@example.com>"],
        };

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
