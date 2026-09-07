using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class EmptyJunkTests
{
    [TestMethod]
    public async Task EmptyJunk_expunges_Junk_and_leaves_Inbox()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Junk", "Junk", MailboxRole.Junk));
        fixture.Imap.SeedMessages("INBOX", Message("keep-1", "Keep"));
        fixture.Imap.SeedMessages("Junk", Message("junk-1", "Spam"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Inbox);
        var junk = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Junk);
        Assert.HasCount(1, await app.ListMessagesAsync(account.Id, junk.Id));

        await app.EmptyJunkAsync(account.Id);

        Assert.AreEqual("Junk", fixture.Imap.LastExpungeMailboxPath);
        Assert.IsEmpty(await app.ListMessagesAsync(account.Id, junk.Id));
        Assert.AreEqual("Keep", (await app.ListMessagesAsync(account.Id, inbox.Id)).Single().Subject);
    }

    [TestMethod]
    public async Task EmptyJunk_throws_when_Account_has_no_Junk_Mailbox()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => app.EmptyJunkAsync(account.Id));
        Assert.AreEqual("This Account has no Junk Mailbox.", ex.Message);
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
