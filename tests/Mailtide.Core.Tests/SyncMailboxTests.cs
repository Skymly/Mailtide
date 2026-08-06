using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class SyncMailboxTests
{
    [TestMethod]
    public async Task SyncNow_populates_Mailboxes_with_optional_roles_from_fake_IMAP()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Sent", "Sent", MailboxRole.Sent),
            new RemoteMailbox("Projects", "Projects", Role: null));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());

        await app.SyncNowAsync(account.Id);

        var mailboxes = await app.ListMailboxesAsync(account.Id);
        Assert.HasCount(3, mailboxes);

        var inbox = mailboxes.Single(m => m.Name == "INBOX");
        Assert.AreEqual(MailboxRole.Inbox, inbox.Role);
        Assert.AreEqual("INBOX", inbox.Path);

        var sent = mailboxes.Single(m => m.Name == "Sent");
        Assert.AreEqual(MailboxRole.Sent, sent.Role);

        var projects = mailboxes.Single(m => m.Name == "Projects");
        Assert.IsNull(projects.Role);
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
