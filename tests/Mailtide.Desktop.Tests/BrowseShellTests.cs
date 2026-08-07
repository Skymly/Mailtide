using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Desktop.Tests;

[TestClass]
public sealed class BrowseShellTests
{
    [TestMethod]
    public async Task BrowseShell_lists_Accounts_already_in_the_store()
    {
        using var fixture = new DesktopAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));

        var shell = new BrowseShell(app);
        await shell.LoadAccountsAsync();

        Assert.HasCount(1, shell.Accounts);
        Assert.AreEqual(account.Id, shell.Accounts[0].Id);
        Assert.AreEqual("Personal", shell.Accounts[0].DisplayName);
    }

    [TestMethod]
    public async Task BrowseShell_selecting_Account_lists_its_Mailboxes()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Sent", "Sent", MailboxRole.Sent));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);

        var shell = new BrowseShell(app);
        await shell.LoadAccountsAsync();
        await shell.SelectAccountAsync(account.Id);

        Assert.AreEqual(account.Id, shell.SelectedAccountId);
        Assert.HasCount(2, shell.Mailboxes);
        Assert.IsTrue(shell.Mailboxes.Any(m => m.Role == MailboxRole.Inbox));
        Assert.IsTrue(shell.Mailboxes.Any(m => m.Role == MailboxRole.Sent));
    }

    [TestMethod]
    public async Task BrowseShell_selecting_Mailbox_lists_its_Messages()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-1",
                Subject: "Hello",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "hi"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);

        Assert.AreEqual(inbox.Id, shell.SelectedMailboxId);
        Assert.IsFalse(shell.ShowingUnifiedInbox);
        Assert.HasCount(1, shell.Messages);
        Assert.AreEqual("Hello", shell.Messages[0].Subject);
    }

    [TestMethod]
    public async Task BrowseShell_shows_Unified_Inbox_as_query_view_not_a_Mailbox()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Sent", "Sent", MailboxRole.Sent));
        await using var app = await fixture.OpenAppAsync();

        var accountA = await app.AddManualAccountAsync(ValidDraft("Alice", "alice@example.com"));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "a-1",
                Subject: "From Alice Inbox",
                FromAddress: "carol@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "a"));
        await app.SyncNowAsync(accountA.Id);

        var accountB = await app.AddManualAccountAsync(ValidDraft("Bob", "bob@example.com"));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "b-1",
                Subject: "From Bob Inbox",
                FromAddress: "dave@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "b"));
        await app.SyncNowAsync(accountB.Id);

        var mailboxCountBefore = (await app.ListMailboxesAsync(accountA.Id)).Count
            + (await app.ListMailboxesAsync(accountB.Id)).Count;

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(accountA.Id);
        Assert.IsNotEmpty(shell.Mailboxes);
        Assert.AreEqual(accountA.Id, shell.SelectedAccountId);

        await shell.ShowUnifiedInboxAsync();

        Assert.IsTrue(shell.ShowingUnifiedInbox);
        Assert.IsNull(shell.SelectedAccountId);
        Assert.IsNull(shell.SelectedMailboxId);
        Assert.IsEmpty(shell.Mailboxes);
        Assert.HasCount(2, shell.Messages);
        Assert.AreEqual("From Bob Inbox", shell.Messages[0].Subject);
        Assert.AreEqual("From Alice Inbox", shell.Messages[1].Subject);

        var mailboxCountAfter = (await app.ListMailboxesAsync(accountA.Id)).Count
            + (await app.ListMailboxesAsync(accountB.Id)).Count;
        Assert.AreEqual(mailboxCountBefore, mailboxCountAfter);
    }

    private static ManualAccountDraft ValidDraft(string displayName, string email) =>
        new(
            DisplayName: displayName,
            EmailAddress: email,
            ImapHost: "imap.example.com",
            ImapPort: 993,
            SmtpHost: "smtp.example.com",
            SmtpPort: 587,
            Password: "s3cret-password");
}
