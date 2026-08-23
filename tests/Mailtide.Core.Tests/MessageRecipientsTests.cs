using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class MessageRecipientsTests
{
    [TestMethod]
    public async Task ListMessages_includes_To_and_Cc()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "r1",
                Subject: "Hello",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 12, 9, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "hi")
            {
                ToAddresses = ["alice@example.com"],
                CcAddresses = ["carol@example.com"],
            });

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(
            new ManualAccountDraft("Personal", "alice@example.com", "imap.example.com", 993, "smtp.example.com", 587, "s3cret-password"));
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;
        var message = (await app.ListMessagesAsync(account.Id, mailboxId)).Single();
        CollectionAssert.AreEqual(new[] { "alice@example.com" }, message.ToAddresses.ToArray());
        CollectionAssert.AreEqual(new[] { "carol@example.com" }, message.CcAddresses.ToArray());
    }
}
