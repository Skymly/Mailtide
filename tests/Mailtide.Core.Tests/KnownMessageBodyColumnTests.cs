using Mailtide.Core;
using Mailtide.Core.Imap;
using Microsoft.Data.Sqlite;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class KnownMessageBodyColumnTests
{
    [TestMethod]
    public async Task Incremental_sync_of_known_Message_does_not_depend_on_body_columns()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox) { UidValidity = 4 });
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "uid-known",
                Subject: "Old subject",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "column-proof-body")
            {
                BodyHtml = "<p>column-proof-html</p>",
            });

        Guid accountId;
        Guid messageId;
        await using (var app = await fixture.OpenAppAsync())
        {
            var account = await app.AddManualAccountAsync(Draft());
            accountId = account.Id;
            await app.SyncNowAsync(accountId);
            var mailboxId = (await app.ListMailboxesAsync(accountId)).Single().Id;
            messageId = (await app.ListMessagesAsync(accountId, mailboxId)).Single().Id;
            Assert.AreEqual("column-proof-body", await app.GetMessageBodyAsync(accountId, messageId));
            Assert.AreEqual("<p>column-proof-html</p>", await app.GetMessageHtmlAsync(accountId, messageId));
        }

        RenameBodyColumns(fixture.AppDataDirectory, hide: true);
        try
        {
            fixture.Imap.SeedMessages(
                "INBOX",
                new RemoteMessage(
                    RemoteId: "uid-known",
                    Subject: "New subject",
                    FromAddress: "carol@example.com",
                    ReceivedAt: new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero),
                    IsRead: true,
                    BodyText: "replacement-body-must-not-be-required")
                {
                    BodyHtml = "<p>replacement-html</p>",
                });
            fixture.Imap.FetchedRemoteIds.Clear();

            await using var app = await fixture.OpenAppAsync();
            await app.SyncNowAsync(accountId);
            Assert.HasCount(0, fixture.Imap.FetchedRemoteIds);
        }
        finally
        {
            RenameBodyColumns(fixture.AppDataDirectory, hide: false);
        }

        await using (var app = await fixture.OpenAppAsync())
        {
            var mailboxId = (await app.ListMailboxesAsync(accountId)).Single().Id;
            var message = (await app.ListMessagesAsync(accountId, mailboxId)).Single();
            Assert.AreEqual(messageId, message.Id);
            Assert.AreEqual("New subject", message.Subject);
            Assert.AreEqual("carol@example.com", message.FromAddress);
            Assert.IsTrue(message.IsRead);
            Assert.AreEqual("column-proof-body", await app.GetMessageBodyAsync(accountId, messageId));
            Assert.AreEqual("<p>column-proof-html</p>", await app.GetMessageHtmlAsync(accountId, messageId));
        }
    }

    private static void RenameBodyColumns(string appDataDirectory, bool hide)
    {
        var path = Path.Combine(appDataDirectory, "mailtide.db");
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = hide
            ? """
              ALTER TABLE "Messages" RENAME COLUMN "BodyText" TO "BodyTextHidden";
              ALTER TABLE "Messages" RENAME COLUMN "BodyHtml" TO "BodyHtmlHidden";
              """
            : """
              ALTER TABLE "Messages" RENAME COLUMN "BodyTextHidden" TO "BodyText";
              ALTER TABLE "Messages" RENAME COLUMN "BodyHtmlHidden" TO "BodyHtml";
              """;
        command.ExecuteNonQuery();
    }

    private static ManualAccountDraft Draft() =>
        new(
            DisplayName: "Personal",
            EmailAddress: "alice@example.com",
            ImapHost: "imap.example.com",
            ImapPort: 993,
            SmtpHost: "smtp.example.com",
            SmtpPort: 587,
            Password: "s3cret-password");
}
