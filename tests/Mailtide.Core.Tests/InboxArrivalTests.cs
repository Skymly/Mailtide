using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class InboxArrivalTests
{
    [TestMethod]
    public async Task First_Inbox_sync_does_not_raise_InboxMessageArrived()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            Unread("first", "Welcome", "bob@example.com"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());

        var arrivals = new List<InboxArrival>();
        app.InboxMessageArrived += (_, arrival) => arrivals.Add(arrival);

        await app.SyncNowAsync(account.Id);

        Assert.IsEmpty(arrivals);
        Assert.HasCount(1, await app.ListUnifiedInboxAsync());
    }

    [TestMethod]
    public async Task Incremental_unread_Inbox_Message_raises_InboxMessageArrived()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            Unread("m-1", "Already here", "bob@example.com"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);

        var arrivals = new List<InboxArrival>();
        app.InboxMessageArrived += (_, arrival) => arrivals.Add(arrival);

        fixture.Imap.SeedMessages(
            "INBOX",
            Unread("m-1", "Already here", "bob@example.com"),
            Unread("m-2", "Just arrived", "carol@example.com"));
        await app.SyncNowAsync(account.Id);

        Assert.HasCount(1, arrivals);
        Assert.AreEqual(account.Id, arrivals[0].AccountId);
        Assert.AreEqual("Just arrived", arrivals[0].Subject);
        Assert.AreEqual("carol@example.com", arrivals[0].FromAddress);
        Assert.AreEqual("Personal", arrivals[0].AccountDisplayName);
        Assert.AreNotEqual(Guid.Empty, arrivals[0].MessageId);
        Assert.AreNotEqual(Guid.Empty, arrivals[0].MailboxId);
    }

    [TestMethod]
    public async Task Read_or_non_Inbox_Messages_do_not_raise_InboxMessageArrived()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Sent", "Sent", MailboxRole.Sent));
        fixture.Imap.SeedMessages("INBOX", Unread("seed", "Seed", "bob@example.com"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);

        var arrivals = new List<InboxArrival>();
        app.InboxMessageArrived += (_, arrival) => arrivals.Add(arrival);

        fixture.Imap.SeedMessages(
            "INBOX",
            Unread("seed", "Seed", "bob@example.com"),
            new RemoteMessage(
                RemoteId: "read-new",
                Subject: "Already read",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 4, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "opened"));
        fixture.Imap.SeedMessages(
            "Sent",
            Unread("sent-1", "Outgoing", "alice@example.com"));
        await app.SyncNowAsync(account.Id);

        Assert.IsEmpty(arrivals);
    }

    [TestMethod]
    public async Task Same_Message_Id_is_not_raised_twice()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages("INBOX", Unread("seed", "Seed", "bob@example.com"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);

        var arrivals = new List<InboxArrival>();
        app.InboxMessageArrived += (_, arrival) => arrivals.Add(arrival);

        fixture.Imap.SeedMessages(
            "INBOX",
            Unread("seed", "Seed", "bob@example.com"),
            Unread("dup", "Ping", "carol@example.com"));
        await app.SyncNowAsync(account.Id);
        await app.SyncNowAsync(account.Id);

        Assert.HasCount(1, arrivals);
        Assert.AreEqual("Ping", arrivals[0].Subject);
    }

    private static RemoteMessage Unread(string remoteId, string subject, string from) =>
        new(
            RemoteId: remoteId,
            Subject: subject,
            FromAddress: from,
            ReceivedAt: new DateTimeOffset(2026, 4, 4, 9, 0, 0, TimeSpan.Zero),
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