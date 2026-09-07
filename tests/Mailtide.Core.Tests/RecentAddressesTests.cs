using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class RecentAddressesTests
{
    [TestMethod]
    public async Task ListRecentAddresses_includes_From_To_and_Cc_once()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-1",
                Subject: "Hello",
                FromAddress: "Bob <bob@example.com>",
                ReceivedAt: new DateTimeOffset(2026, 4, 2, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "hi")
            {
                ToAddresses = ["alice@example.com"],
                CcAddresses = ["carol@example.com"],
            },
            new RemoteMessage(
                RemoteId: "m-2",
                Subject: "Later",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "old"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);

        var recent = await app.ListRecentAddressesAsync();
        Assert.AreEqual(1, recent.Count(item => item.Contains("bob@example.com", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(recent.Any(item => item.Contains("alice@example.com", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(recent.Any(item => item.Contains("carol@example.com", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(recent[0].Contains("bob@example.com", StringComparison.OrdinalIgnoreCase));
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
