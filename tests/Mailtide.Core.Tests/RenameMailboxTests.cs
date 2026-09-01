using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class RenameMailboxTests
{
    [TestMethod]
    public async Task RenameMailbox_updates_Name_and_Path()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var created = await app.CreateMailboxAsync(account.Id, "Projects");

        var renamed = await app.RenameMailboxAsync(account.Id, created.Id, "Work");

        Assert.AreEqual(created.Id, renamed.Id);
        Assert.AreEqual("Work", renamed.Name);
        Assert.AreEqual("Work", renamed.Path);
        Assert.IsNull(renamed.Role);
        Assert.AreEqual("Projects", fixture.Imap.LastRenamedMailboxPath);
        Assert.AreEqual("Work", fixture.Imap.LastRenamedMailboxNewName);
        var listed = await app.ListMailboxesAsync(account.Id);
        Assert.IsTrue(listed.Any(m => m.Id == created.Id && m.Name == "Work" && m.Path == "Work"));
        Assert.IsFalse(listed.Any(m => m.Name == "Projects"));
    }

    [TestMethod]
    public async Task RenameMailbox_rejects_blank_missing_and_duplicate_names()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var projects = await app.CreateMailboxAsync(account.Id, "Projects");
        await app.CreateMailboxAsync(account.Id, "Archive");

        await Assert.ThrowsAsync<ArgumentException>(
            () => app.RenameMailboxAsync(account.Id, projects.Id, "  "));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => app.RenameMailboxAsync(account.Id, Guid.NewGuid(), "Work"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => app.RenameMailboxAsync(account.Id, projects.Id, "Archive"));
        Assert.AreEqual("Projects", (await app.ListMailboxesAsync(account.Id)).Single(m => m.Id == projects.Id).Name);
        Assert.IsNull(fixture.Imap.LastRenamedMailboxPath);
    }

    [TestMethod]
    public async Task RenameMailbox_same_name_is_noop()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var created = await app.CreateMailboxAsync(account.Id, "Projects");

        var renamed = await app.RenameMailboxAsync(account.Id, created.Id, "Projects");

        Assert.AreEqual("Projects", renamed.Name);
        Assert.AreEqual("Projects", renamed.Path);
        Assert.IsNull(fixture.Imap.LastRenamedMailboxPath);
    }

    [TestMethod]
    public async Task RenameMailbox_IMAP_failure_leaves_local_unchanged()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var created = await app.CreateMailboxAsync(account.Id, "Projects");
        fixture.Imap.FailWith = new ImapProtocolException("NO RENAME");

        await Assert.ThrowsAsync<ImapProtocolException>(
            () => app.RenameMailboxAsync(account.Id, created.Id, "Work"));
        Assert.AreEqual("Projects", (await app.ListMailboxesAsync(account.Id)).Single(m => m.Id == created.Id).Name);
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