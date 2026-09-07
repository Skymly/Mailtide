using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class FetchBodyTests
{
    [TestMethod]
    public async Task FetchMessageBody_downloads_an_empty_Message_body()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "uid-empty",
                Subject: "Later",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: ""));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;
        var message = (await app.ListMessagesAsync(account.Id, mailboxId)).Single();
        Assert.AreEqual(string.Empty, await app.GetMessageBodyAsync(account.Id, message.Id));

        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "uid-empty",
                Subject: "Later",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "downloaded now"));

        await app.FetchMessageBodyAsync(account.Id, message.Id);

        Assert.AreEqual("downloaded now", await app.GetMessageBodyAsync(account.Id, message.Id));
        Assert.AreEqual("downloaded now", (await app.ListMessagesAsync(account.Id, mailboxId)).Single().Preview);
    }

    [TestMethod]
    public async Task FetchMessageBody_throws_when_Message_is_missing()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        var missing = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => app.FetchMessageBodyAsync(account.Id, missing));
        Assert.AreEqual($"Message '{missing}' was not found.", ex.Message);
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
