using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class SearchMessagesTests
{
    [TestMethod]
    public async Task SearchMessages_matches_Subject_From_and_body()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            Message("1", "Invoice March", "billing@example.com", "please pay"),
            Message("2", "Hello", "bob@example.com", "see you tomorrow"),
            Message("3", "Photos", "carol@example.com", "album", html: "<p>vacation pics</p>"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;

        var bySubject = await app.SearchMessagesAsync(account.Id, mailboxId, "invoice");
        Assert.AreEqual("Invoice March", bySubject.Single().Subject);

        var byFrom = await app.SearchMessagesAsync(account.Id, mailboxId, "BOB@EXAMPLE.COM");
        Assert.AreEqual("Hello", byFrom.Single().Subject);

        var byText = await app.SearchMessagesAsync(account.Id, mailboxId, "tomorrow");
        Assert.AreEqual("Hello", byText.Single().Subject);

        var byHtml = await app.SearchMessagesAsync(account.Id, mailboxId, "vacation");
        Assert.AreEqual("Photos", byHtml.Single().Subject);
    }

    [TestMethod]
    public async Task SearchMessages_blank_query_returns_full_Mailbox_list()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            Message("1", "A", "a@example.com", "one"),
            Message("2", "B", "b@example.com", "two"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;

        var listed = await app.ListMessagesAsync(account.Id, mailboxId);
        var searched = await app.SearchMessagesAsync(account.Id, mailboxId, "  ");
        CollectionAssert.AreEqual(listed.Select(m => m.Id).ToArray(), searched.Select(m => m.Id).ToArray());
    }

    [TestMethod]
    public async Task SearchUnifiedInbox_matches_across_Accounts()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages("INBOX", Message("1", "Alpha", "a@example.com", "first"));

        await using var app = await fixture.OpenAppAsync();
        var alice = await app.AddManualAccountAsync(ValidDraft("Alice", "alice@example.com"));
        await app.SyncNowAsync(alice.Id);

        fixture.Imap.SeedMessages("INBOX", Message("2", "Beta", "b@example.com", "second unique"));
        var bob = await app.AddManualAccountAsync(ValidDraft("Bob", "bob@example.com"));
        await app.SyncNowAsync(bob.Id);

        var hits = await app.SearchUnifiedInboxAsync("unique");
        Assert.AreEqual("Beta", hits.Single().Subject);
        Assert.AreEqual(bob.Id, hits.Single().AccountId);
    }

    private static RemoteMessage Message(
        string remoteId,
        string subject,
        string from,
        string body,
        string? html = null) =>
        new(
            RemoteId: remoteId,
            Subject: subject,
            FromAddress: from,
            ReceivedAt: new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero),
            IsRead: true,
            BodyText: body)
        {
            BodyHtml = html,
        };

    private static ManualAccountDraft ValidDraft(
        string displayName = "Personal",
        string email = "alice@example.com") =>
        new(
            DisplayName: displayName,
            EmailAddress: email,
            ImapHost: "imap.example.com",
            ImapPort: 993,
            SmtpHost: "smtp.example.com",
            SmtpPort: 587,
            Password: "s3cret-password");
}
