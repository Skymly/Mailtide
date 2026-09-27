using Mailtide.Core;
using Mailtide.Core.Imap;
using Mailtide.Core.Store;
using Mailtide.Core.Tests.Protocol;
using Microsoft.Data.Sqlite;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class AttachmentStreamTests
{
    [TestMethod]
    public async Task Small_attachment_is_on_disk_and_not_in_the_SQLite_file()
    {
        var payload = DistinctPayload();
        await using var imap = LoopbackImapServer.Start(
        [
            new SeededMailbox(
                Path: "INBOX",
                Attributes: ["Inbox"],
                Messages:
                [
                    new SeededImapMessage(
                        Uid: 4,
                        Subject: "Has file",
                        From: "bob@example.com",
                        InternalDate: new DateTimeOffset(2026, 8, 12, 9, 0, 0, TimeSpan.Zero),
                        IsRead: true,
                        BodyText: "see attached")
                    {
                        Attachments = [new SeededImapAttachment("notes.bin", "application/octet-stream", payload)],
                    },
                ]),
        ]);

        using var fixture = new CoreAppFixture();
        {
            await using var app = await MailtideApp.OpenAsync(
                fixture.AppDataDirectory,
                fixture.SecureStorage,
                fixture.OAuth,
                new MailKitImapClientFactory(),
                fixture.Smtp);
            var account = await app.AddManualAccountAsync(Draft(imap.Port, "127.0.0.1"));
            await app.SyncNowAsync(account.Id);

            Assert.AreEqual(AccountSyncState.Idle, app.GetAccountStatus(account.Id).State);
            var mailbox = (await app.ListMailboxesAsync(account.Id)).Single();
            var message = (await app.ListMessagesAsync(account.Id, mailbox.Id)).Single();
            var attachment = (await app.ListAttachmentsAsync(account.Id, message.Id)).Single();
            Assert.IsFalse(attachment.ContentOmitted);
            Assert.AreEqual("notes.bin", attachment.ListLabel);
            var opened = await app.OpenAttachmentAsync(account.Id, attachment.Id);
            Assert.IsNotNull(opened);
            CollectionAssert.AreEqual(payload, opened!.Content);
        }

        var blob = Directory
            .GetFiles(fixture.AppDataDirectory, "*", SearchOption.AllDirectories)
            .Where(path => !IsSqliteFile(path))
            .Select(path => File.ReadAllBytes(path))
            .FirstOrDefault(bytes => bytes.AsSpan().SequenceEqual(payload));
        Assert.IsNotNull(blob, "Attachment payload must exist under the Account filesystem blob area.");
        foreach (var name in new[] { "mailtide.db", "mailtide.db-wal", "mailtide.db-shm" })
        {
            var path = Path.Combine(fixture.AppDataDirectory, name);
            if (!File.Exists(path))
            {
                continue;
            }

            var stored = await File.ReadAllBytesAsync(path);
            Assert.IsFalse(
                ContainsSequence(stored, payload),
                $"Attachment payload must not be stored in {name}.");
        }
    }

    [TestMethod]
    public async Task Imap_extract_streams_a_small_part_without_buffering_decoded_bytes()
    {
        var payload = DistinctPayload();
        await using var imap = LoopbackImapServer.Start(
        [
            new SeededMailbox(
                Path: "INBOX",
                Attributes: ["Inbox"],
                Messages:
                [
                    new SeededImapMessage(
                        Uid: 8,
                        Subject: "Stream",
                        From: "bob@example.com",
                        InternalDate: new DateTimeOffset(2026, 8, 12, 10, 0, 0, TimeSpan.Zero),
                        IsRead: true,
                        BodyText: "stream me")
                    {
                        Attachments = [new SeededImapAttachment("a.bin", "application/octet-stream", payload)],
                    },
                ]),
        ]);

        await using var client = new MailKitImapClientFactory().Create();
        await client.ConnectAndAuthenticateAsync("127.0.0.1", imap.Port, "alice@example.com", "s3cret-password");
        var attachment = (await client.FetchMessagesAsync("INBOX")).Single().Attachments.Single();
        Assert.AreEqual(0, attachment.Content.Length);
        Assert.IsNotNull(attachment.WriteContentAsync);
        Assert.IsFalse(attachment.ContentOmitted);

        var path = Path.Combine(Path.GetTempPath(), "mailtide-stream-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.IsTrue(await AttachmentBlob.TryWriteAsync(attachment, path, CancellationToken.None));
            CollectionAssert.AreEqual(payload, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [TestMethod]
    public async Task Over_limit_attachment_stays_visible_and_is_not_refetched_as_missing_body()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox) { UidValidity = 3 });
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "uid-big",
                Subject: "Too big",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 12, 11, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "body-stays")
            {
                Attachments =
                [
                    new RemoteAttachment("huge.bin", "application/octet-stream", [])
                    {
                        DeclaredDecodedBytes = AttachmentBlobLimits.MaxDecodedBytes + 1L,
                        ContentOmitted = true,
                    },
                ],
            });

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(Draft(993));
        await app.SyncNowAsync(account.Id);
        var mailbox = (await app.ListMailboxesAsync(account.Id)).Single();
        var message = (await app.ListMessagesAsync(account.Id, mailbox.Id)).Single();
        var attachment = (await app.ListAttachmentsAsync(account.Id, message.Id)).Single();
        Assert.IsTrue(attachment.ContentOmitted);
        StringAssert.Contains(attachment.ListLabel, "omitted");
        Assert.AreEqual("huge.bin (omitted, over size limit)", attachment.ListLabel);
        Assert.IsNull(await app.OpenAttachmentAsync(account.Id, attachment.Id));

        fixture.Imap.FetchedRemoteIds.Clear();
        await app.SyncNowAsync(account.Id);

        Assert.HasCount(0, fixture.Imap.FetchedRemoteIds);
        Assert.AreEqual("body-stays", await app.GetMessageBodyAsync(account.Id, message.Id));
        var again = (await app.ListAttachmentsAsync(account.Id, message.Id)).Single();
        Assert.AreEqual(attachment.Id, again.Id);
        Assert.IsTrue(again.ContentOmitted);
        Assert.AreEqual(attachment.ListLabel, again.ListLabel);
    }

    [TestMethod]
    public async Task Imap_extract_omits_a_part_whose_declared_size_exceeds_the_limit()
    {
        await using var imap = LoopbackImapServer.Start(
        [
            new SeededMailbox(
                Path: "INBOX",
                Attributes: ["Inbox"],
                Messages:
                [
                    new SeededImapMessage(
                        Uid: 9,
                        Subject: "Declared huge",
                        From: "bob@example.com",
                        InternalDate: new DateTimeOffset(2026, 8, 12, 12, 0, 0, TimeSpan.Zero),
                        IsRead: true,
                        BodyText: "header says huge")
                    {
                        Attachments =
                        [
                            new SeededImapAttachment("huge.bin", "application/octet-stream", [1, 2, 3])
                            {
                                DeclaredSize = AttachmentBlobLimits.MaxDecodedBytes + 1L,
                            },
                        ],
                    },
                ]),
        ]);

        await using var client = new MailKitImapClientFactory().Create();
        await client.ConnectAndAuthenticateAsync("127.0.0.1", imap.Port, "alice@example.com", "s3cret-password");
        var attachment = (await client.FetchMessagesAsync("INBOX")).Single().Attachments.Single();
        Assert.IsTrue(attachment.ContentOmitted);
        Assert.AreEqual(0, attachment.Content.Length);
        Assert.IsNull(attachment.WriteContentAsync);
        Assert.IsTrue(AttachmentBlobLimits.ExceedsLimit(attachment.DeclaredDecodedBytes ?? 0));
    }

    [TestMethod]
    public async Task UidValidity_change_still_replaces_the_local_Message()
    {
        using var fixture = new CoreAppFixture();
        var first = "first-blob"u8.ToArray();
        var second = "second-blob"u8.ToArray();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox) { UidValidity = 1 });
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "1",
                Subject: "Old UID space",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "first")
            {
                Attachments = [new RemoteAttachment("a.txt", "text/plain", first)],
            });

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(Draft(993));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();
        var original = (await app.ListMessagesAsync(account.Id, inbox.Id)).Single();

        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox) { UidValidity = 2 });
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "1",
                Subject: "New UID space",
                FromAddress: "carol@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "second")
            {
                Attachments = [new RemoteAttachment("b.txt", "text/plain", second)],
            });

        await app.SyncNowAsync(account.Id);
        var replaced = (await app.ListMessagesAsync(account.Id, inbox.Id)).Single();
        Assert.AreEqual("New UID space", replaced.Subject);
        Assert.AreEqual("carol@example.com", replaced.FromAddress);
        Assert.AreNotEqual(original.Id, replaced.Id);
        Assert.AreEqual(2, fixture.Imap.FetchedRemoteIds.Count);
        var attachment = (await app.ListAttachmentsAsync(account.Id, replaced.Id)).Single();
        Assert.AreEqual("b.txt", attachment.FileName);
        var opened = await app.OpenAttachmentAsync(account.Id, attachment.Id);
        Assert.IsNotNull(opened);
        CollectionAssert.AreEqual(second, opened!.Content);
    }

    [TestMethod]
    public async Task StoreMigrator_adds_ContentOmitted_and_keeps_existing_Attachment_rows()
    {
        using var fixture = new CoreAppFixture();
        Directory.CreateDirectory(fixture.AppDataDirectory);
        var path = Path.Combine(fixture.AppDataDirectory, "mailtide.db");
        var attachmentId = "11111111-1111-1111-1111-111111111111";
        await using (var seed = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            await seed.OpenAsync();
            await using var command = seed.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE SchemaVersion (
                    Id INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
                    Version INTEGER NOT NULL
                );
                INSERT INTO SchemaVersion (Id, Version) VALUES (1, 6);
                CREATE TABLE Attachments (
                    Id TEXT NOT NULL PRIMARY KEY,
                    AccountId TEXT NOT NULL,
                    MessageId TEXT NOT NULL,
                    FileName TEXT NOT NULL,
                    ContentType TEXT NOT NULL,
                    BlobRelativePath TEXT NOT NULL,
                    ContentId TEXT NULL
                );
                INSERT INTO Attachments (Id, AccountId, MessageId, FileName, ContentType, BlobRelativePath)
                VALUES ($id, $account, $message, 'old.txt', 'text/plain', 'accounts/x/blobs/y');
                """;
            command.Parameters.AddWithValue("$id", attachmentId);
            command.Parameters.AddWithValue("$account", Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue("$message", Guid.NewGuid().ToString("D"));
            await command.ExecuteNonQueryAsync();
        }

        await using (var app = await fixture.OpenAppAsync())
        {
            Assert.IsNotNull(app);
        }

        await using var verify = new SqliteConnection($"Data Source={path};Pooling=False");
        await verify.OpenAsync();
        await using var versionCommand = verify.CreateCommand();
        versionCommand.CommandText = "SELECT Version FROM SchemaVersion WHERE Id = 1";
        Assert.AreEqual(StoreMigrator.CurrentVersion, Convert.ToInt32(await versionCommand.ExecuteScalarAsync()));

        await using var columnCommand = verify.CreateCommand();
        columnCommand.CommandText = "SELECT ContentOmitted FROM Attachments WHERE Id = $id";
        columnCommand.Parameters.AddWithValue("$id", attachmentId);
        Assert.AreEqual(0, Convert.ToInt32(await columnCommand.ExecuteScalarAsync()));
    }

    private static byte[] DistinctPayload()
    {
        var payload = new byte[48];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(0xA5 ^ i);
        }

        return payload;
    }

    private static bool ContainsSequence(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length)
        {
            return false;
        }

        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSqliteFile(string path)
    {
        var name = Path.GetFileName(path);
        return name.Equals("mailtide.db", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".db-wal", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".db-shm", StringComparison.OrdinalIgnoreCase);
    }

    private static ManualAccountDraft Draft(int imapPort, string imapHost = "imap.example.com") =>
        new(
            DisplayName: "Personal",
            EmailAddress: "alice@example.com",
            ImapHost: imapHost,
            ImapPort: imapPort,
            SmtpHost: "smtp.example.com",
            SmtpPort: 587,
            Password: "s3cret-password");
}
