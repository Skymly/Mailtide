using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class CreateMailboxTests
{
    [TestMethod]
    public async Task CreateMailbox_adds_Mailbox_without_role()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);

        var created = await app.CreateMailboxAsync(account.Id, "Projects");

        Assert.AreEqual("Projects", created.Name);
        Assert.AreEqual("Projects", created.Path);
        Assert.IsNull(created.Role);
        Assert.AreEqual("Projects", fixture.Imap.LastCreatedMailboxPath);
        var listed = await app.ListMailboxesAsync(account.Id);
        Assert.IsTrue(listed.Any(m => m.Id == created.Id && m.Name == "Projects"));
    }

    [TestMethod]
    public async Task CreateMailbox_rejects_blank_and_duplicate_names()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);

        await Assert.ThrowsAsync<ArgumentException>(() => app.CreateMailboxAsync(account.Id, "  "));
        await app.CreateMailboxAsync(account.Id, "Projects");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => app.CreateMailboxAsync(account.Id, "Projects"));
        Assert.HasCount(1, (await app.ListMailboxesAsync(account.Id)).Where(m => m.Name == "Projects"));
    }

    [TestMethod]
    public async Task CreateMailbox_IMAP_failure_does_not_insert_local_Mailbox()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        fixture.Imap.FailWith = new ImapProtocolException("NO CREATE");

        await Assert.ThrowsAsync<ImapProtocolException>(
            () => app.CreateMailboxAsync(account.Id, "Projects"));
        Assert.IsFalse((await app.ListMailboxesAsync(account.Id)).Any(m => m.Name == "Projects"));
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