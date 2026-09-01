using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class DeleteMailboxTests
{
    [TestMethod]
    public async Task DeleteMailbox_removes_Mailbox_and_its_Messages()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var created = await app.CreateMailboxAsync(account.Id, "Projects");

        await app.DeleteMailboxAsync(account.Id, created.Id);

        Assert.AreEqual("Projects", fixture.Imap.LastDeletedMailboxPath);
        var listed = await app.ListMailboxesAsync(account.Id);
        Assert.IsFalse(listed.Any(m => m.Id == created.Id));
        Assert.IsFalse(listed.Any(m => m.Name == "Projects"));
    }

    [TestMethod]
    public async Task DeleteMailbox_rejects_missing_and_role_Mailboxes()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Inbox);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => app.DeleteMailboxAsync(account.Id, Guid.NewGuid()));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => app.DeleteMailboxAsync(account.Id, inbox.Id));
        Assert.IsTrue((await app.ListMailboxesAsync(account.Id)).Any(m => m.Id == inbox.Id));
        Assert.IsNull(fixture.Imap.LastDeletedMailboxPath);
    }

    [TestMethod]
    public async Task DeleteMailbox_IMAP_failure_leaves_local_unchanged()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var created = await app.CreateMailboxAsync(account.Id, "Projects");
        fixture.Imap.FailWith = new ImapProtocolException("NO DELETE");

        await Assert.ThrowsAsync<ImapProtocolException>(
            () => app.DeleteMailboxAsync(account.Id, created.Id));
        Assert.IsTrue((await app.ListMailboxesAsync(account.Id)).Any(m => m.Id == created.Id));
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
