using System.Text;
using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class AttachmentBlobTests
{
    private static readonly byte[] UniquePayload =
        Encoding.UTF8.GetBytes("%PDF-1.4 unique-attachment-payload-xyz");

    [TestMethod]
    public async Task SyncNow_stores_attachment_bytes_on_filesystem_with_metadata_in_store()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "msg-with-attach",
                Subject: "Has file",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "See attached.")
            {
                Attachments =
                [
                    new RemoteAttachment(
                        FileName: "report.pdf",
                        ContentType: "application/pdf",
                        Content: UniquePayload),
                ],
            });

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);

        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;
        var messageId = (await app.ListMessagesAsync(account.Id, mailboxId)).Single().Id;

        var attachments = await app.ListAttachmentsAsync(account.Id, messageId);
        Assert.HasCount(1, attachments);
        Assert.AreEqual("report.pdf", attachments[0].FileName);
        Assert.AreEqual("application/pdf", attachments[0].ContentType);

        var opened = await app.OpenAttachmentAsync(account.Id, attachments[0].Id);
        Assert.IsNotNull(opened);
        CollectionAssert.AreEqual(UniquePayload, opened.Content);

        AssertAttachmentBytesNotInSqlite(fixture.AppDataDirectory, UniquePayload);
        AssertAttachmentBytesExistOnFilesystem(fixture.AppDataDirectory, account.Id, UniquePayload);
    }

    [TestMethod]
    public async Task Already_downloaded_attachment_opens_while_offline()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "msg-offline-attach",
                Subject: "Plane reading",
                FromAddress: "carol@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 2, 8, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "Attachment for offline.")
            {
                Attachments =
                [
                    new RemoteAttachment(
                        FileName: "notes.txt",
                        ContentType: "text/plain",
                        Content: UniquePayload),
                ],
            });

        Guid accountId;
        Guid attachmentId;

        await using (var app = await fixture.OpenAppAsync())
        {
            var account = await app.AddManualAccountAsync(ValidDraft());
            accountId = account.Id;
            await app.SyncNowAsync(accountId);

            var mailboxId = (await app.ListMailboxesAsync(accountId)).Single().Id;
            var messageId = (await app.ListMessagesAsync(accountId, mailboxId)).Single().Id;
            attachmentId = (await app.ListAttachmentsAsync(accountId, messageId)).Single().Id;
        }

        // Simulate offline: protocol port has nothing; blob store still serves content.
        fixture.Imap.SeedMailboxes();
        fixture.Imap.ClearMessages();

        await using (var restarted = await fixture.OpenAppAsync())
        {
            var opened = await restarted.OpenAttachmentAsync(accountId, attachmentId);
            Assert.IsNotNull(opened);
            Assert.AreEqual("notes.txt", opened.FileName);
            Assert.AreEqual("text/plain", opened.ContentType);
            CollectionAssert.AreEqual(UniquePayload, opened.Content);
        }
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

    private static void AssertAttachmentBytesNotInSqlite(string appDataDirectory, byte[] payload)
    {
        var dbPath = Path.Combine(appDataDirectory, "mailtide.db");
        Assert.IsTrue(File.Exists(dbPath));
        var dbBytes = File.ReadAllBytes(dbPath);
        Assert.IsFalse(
            ContainsSequence(dbBytes, payload),
            "Attachment payload must not be inlined in the SQLite database.");
    }

    private static void AssertAttachmentBytesExistOnFilesystem(
        string appDataDirectory,
        Guid accountId,
        byte[] payload)
    {
        var partition = Path.Combine(appDataDirectory, "accounts", accountId.ToString("D"));
        Assert.IsTrue(Directory.Exists(partition));

        var found = Directory
            .EnumerateFiles(partition, "*", SearchOption.AllDirectories)
            .Any(path =>
            {
                // Skip the SQLite file if it were under the partition (it isn't).
                var bytes = File.ReadAllBytes(path);
                return ContainsSequence(bytes, payload);
            });

        Assert.IsTrue(found, "Attachment payload must exist under the Account filesystem blob area.");
    }

    private static bool ContainsSequence(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || needle.Length > haystack.Length)
        {
            return false;
        }

        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }
}
