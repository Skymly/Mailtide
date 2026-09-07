using Microsoft.EntityFrameworkCore;

namespace Mailtide.Core.Store;

/// <summary>
/// Versioned SQLite schema. Failures propagate; missing columns are detected via
/// pragma_table_info rather than swallowed ALTER exceptions.
/// </summary>
internal static class StoreMigrator
{
    public const int CurrentVersion = 6;

    public static async Task ApplyAsync(MailtideDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        await db.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ExecuteAsync(
                    db,
                    """
                    CREATE TABLE IF NOT EXISTS SchemaVersion (
                        Id INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
                        Version INTEGER NOT NULL
                    )
                    """,
                    cancellationToken)
                .ConfigureAwait(false);
            await ExecuteAsync(
                    db,
                    "INSERT OR IGNORE INTO SchemaVersion (Id, Version) VALUES (1, 0)",
                    cancellationToken)
                .ConfigureAwait(false);

            var version = await ReadVersionAsync(db, cancellationToken).ConfigureAwait(false);
            if (version < 1)
            {
                await ApplyV1Async(db, cancellationToken).ConfigureAwait(false);
                await SetVersionAsync(db, 1, cancellationToken).ConfigureAwait(false);
                version = 1;
            }

            if (version < 2)
            {
                await ApplyV2Async(db, cancellationToken).ConfigureAwait(false);
                await SetVersionAsync(db, 2, cancellationToken).ConfigureAwait(false);
                version = 2;
            }

            if (version < 3)
            {
                await ApplyV3Async(db, cancellationToken).ConfigureAwait(false);
                await SetVersionAsync(db, 3, cancellationToken).ConfigureAwait(false);
                version = 3;
            }

            if (version < 4)
            {
                await ApplyV4Async(db, cancellationToken).ConfigureAwait(false);
                await SetVersionAsync(db, 4, cancellationToken).ConfigureAwait(false);
                version = 4;
            }

            if (version < 5)
            {
                await ApplyV5Async(db, cancellationToken).ConfigureAwait(false);
                await SetVersionAsync(db, 5, cancellationToken).ConfigureAwait(false);
                version = 5;
            }

            if (version < 6)
            {
                await ApplyV6Async(db, cancellationToken).ConfigureAwait(false);
                await SetVersionAsync(db, 6, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    private static async Task ApplyV1Async(MailtideDbContext db, CancellationToken cancellationToken)
    {
        await ExecuteAsync(
                db,
                """
                CREATE TABLE IF NOT EXISTS "Mailboxes" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_Mailboxes" PRIMARY KEY,
                    "AccountId" TEXT NOT NULL,
                    "Name" TEXT NOT NULL,
                    "Path" TEXT NOT NULL,
                    "Role" TEXT NULL
                )
                """,
                cancellationToken)
            .ConfigureAwait(false);
        await ExecuteAsync(
                db,
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Mailboxes_AccountId_Path"
                ON "Mailboxes" ("AccountId", "Path")
                """,
                cancellationToken)
            .ConfigureAwait(false);

        await ExecuteAsync(
                db,
                """
                CREATE TABLE IF NOT EXISTS "Messages" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_Messages" PRIMARY KEY,
                    "AccountId" TEXT NOT NULL,
                    "MailboxId" TEXT NOT NULL,
                    "RemoteId" TEXT NOT NULL,
                    "Subject" TEXT NOT NULL,
                    "FromAddress" TEXT NOT NULL,
                    "ReceivedAt" TEXT NOT NULL,
                    "IsRead" INTEGER NOT NULL,
                    "BodyText" TEXT NOT NULL
                )
                """,
                cancellationToken)
            .ConfigureAwait(false);
        await ExecuteAsync(
                db,
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Messages_AccountId_MailboxId_RemoteId"
                ON "Messages" ("AccountId", "MailboxId", "RemoteId")
                """,
                cancellationToken)
            .ConfigureAwait(false);

        await ExecuteAsync(
                db,
                """
                CREATE TABLE IF NOT EXISTS "Attachments" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_Attachments" PRIMARY KEY,
                    "AccountId" TEXT NOT NULL,
                    "MessageId" TEXT NOT NULL,
                    "FileName" TEXT NOT NULL,
                    "ContentType" TEXT NOT NULL,
                    "BlobRelativePath" TEXT NOT NULL
                )
                """,
                cancellationToken)
            .ConfigureAwait(false);
        await ExecuteAsync(
                db,
                """
                CREATE INDEX IF NOT EXISTS "IX_Attachments_AccountId_MessageId"
                ON "Attachments" ("AccountId", "MessageId")
                """,
                cancellationToken)
            .ConfigureAwait(false);

        await ExecuteAsync(
                db,
                """
                CREATE TABLE IF NOT EXISTS "Drafts" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_Drafts" PRIMARY KEY,
                    "AccountId" TEXT NOT NULL,
                    "ToAddresses" TEXT NOT NULL,
                    "Subject" TEXT NOT NULL,
                    "BodyText" TEXT NOT NULL,
                    "UpdatedAt" TEXT NOT NULL
                )
                """,
                cancellationToken)
            .ConfigureAwait(false);
        await ExecuteAsync(
                db,
                """
                CREATE INDEX IF NOT EXISTS "IX_Drafts_AccountId"
                ON "Drafts" ("AccountId")
                """,
                cancellationToken)
            .ConfigureAwait(false);

        await ExecuteAsync(
                db,
                """
                CREATE TABLE IF NOT EXISTS "DraftAttachments" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_DraftAttachments" PRIMARY KEY,
                    "AccountId" TEXT NOT NULL,
                    "DraftId" TEXT NOT NULL,
                    "FileName" TEXT NOT NULL,
                    "ContentType" TEXT NOT NULL,
                    "BlobRelativePath" TEXT NOT NULL
                )
                """,
                cancellationToken)
            .ConfigureAwait(false);
        await ExecuteAsync(
                db,
                """
                CREATE INDEX IF NOT EXISTS "IX_DraftAttachments_AccountId_DraftId"
                ON "DraftAttachments" ("AccountId", "DraftId")
                """,
                cancellationToken)
            .ConfigureAwait(false);

        await ExecuteAsync(
                db,
                """
                CREATE TABLE IF NOT EXISTS "OutboxItems" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_OutboxItems" PRIMARY KEY,
                    "AccountId" TEXT NOT NULL,
                    "ToAddresses" TEXT NOT NULL,
                    "Subject" TEXT NOT NULL,
                    "BodyText" TEXT NOT NULL,
                    "State" TEXT NOT NULL,
                    "ErrorMessage" TEXT NULL,
                    "UpdatedAt" TEXT NOT NULL
                )
                """,
                cancellationToken)
            .ConfigureAwait(false);
        await ExecuteAsync(
                db,
                """
                CREATE INDEX IF NOT EXISTS "IX_OutboxItems_AccountId"
                ON "OutboxItems" ("AccountId")
                """,
                cancellationToken)
            .ConfigureAwait(false);

        await ExecuteAsync(
                db,
                """
                CREATE TABLE IF NOT EXISTS "OutboxAttachments" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_OutboxAttachments" PRIMARY KEY,
                    "AccountId" TEXT NOT NULL,
                    "OutboxItemId" TEXT NOT NULL,
                    "FileName" TEXT NOT NULL,
                    "ContentType" TEXT NOT NULL,
                    "BlobRelativePath" TEXT NOT NULL
                )
                """,
                cancellationToken)
            .ConfigureAwait(false);
        await ExecuteAsync(
                db,
                """
                CREATE INDEX IF NOT EXISTS "IX_OutboxAttachments_AccountId_OutboxItemId"
                ON "OutboxAttachments" ("AccountId", "OutboxItemId")
                """,
                cancellationToken)
            .ConfigureAwait(false);

        await AddColumnIfMissingAsync(db, "Accounts", "OAuthProvider", "TEXT NULL", cancellationToken)
            .ConfigureAwait(false);
        await AddColumnIfMissingAsync(db, "Accounts", "OAuthAuthority", "TEXT NULL", cancellationToken)
            .ConfigureAwait(false);
        await AddColumnIfMissingAsync(db, "Accounts", "OAuthClientId", "TEXT NULL", cancellationToken)
            .ConfigureAwait(false);
        await AddColumnIfMissingAsync(db, "Accounts", "Signature", "TEXT", cancellationToken)
            .ConfigureAwait(false);
        await AddColumnIfMissingAsync(db, "Messages", "ToAddresses", "TEXT NOT NULL DEFAULT '[]'", cancellationToken)
            .ConfigureAwait(false);
        await AddColumnIfMissingAsync(db, "Messages", "CcAddresses", "TEXT NOT NULL DEFAULT '[]'", cancellationToken)
            .ConfigureAwait(false);
        await AddColumnIfMissingAsync(db, "Messages", "BodyHtml", "TEXT", cancellationToken)
            .ConfigureAwait(false);
        await AddColumnIfMissingAsync(db, "Messages", "InternetMessageId", "TEXT", cancellationToken)
            .ConfigureAwait(false);
        await AddColumnIfMissingAsync(db, "Messages", "ReferencesJson", "TEXT NOT NULL DEFAULT '[]'", cancellationToken)
            .ConfigureAwait(false);
        await AddColumnIfMissingAsync(db, "Messages", "IsFlagged", "INTEGER NOT NULL DEFAULT 0", cancellationToken)
            .ConfigureAwait(false);
        await AddColumnIfMissingAsync(db, "Attachments", "ContentId", "TEXT", cancellationToken)
            .ConfigureAwait(false);
        await AddColumnIfMissingAsync(db, "Drafts", "CcAddresses", "TEXT NOT NULL DEFAULT '[]'", cancellationToken)
            .ConfigureAwait(false);
        await AddColumnIfMissingAsync(db, "Drafts", "BccAddresses", "TEXT NOT NULL DEFAULT '[]'", cancellationToken)
            .ConfigureAwait(false);
        await AddColumnIfMissingAsync(db, "Drafts", "BodyHtml", "TEXT", cancellationToken)
            .ConfigureAwait(false);
        await AddColumnIfMissingAsync(db, "Drafts", "InReplyTo", "TEXT", cancellationToken)
            .ConfigureAwait(false);
        await AddColumnIfMissingAsync(db, "Drafts", "ReferencesJson", "TEXT NOT NULL DEFAULT '[]'", cancellationToken)
            .ConfigureAwait(false);
        await AddColumnIfMissingAsync(db, "OutboxItems", "CcAddresses", "TEXT NOT NULL DEFAULT '[]'", cancellationToken)
            .ConfigureAwait(false);
        await AddColumnIfMissingAsync(db, "OutboxItems", "BccAddresses", "TEXT NOT NULL DEFAULT '[]'", cancellationToken)
            .ConfigureAwait(false);
        await AddColumnIfMissingAsync(db, "OutboxItems", "BodyHtml", "TEXT", cancellationToken)
            .ConfigureAwait(false);
        await AddColumnIfMissingAsync(db, "OutboxItems", "InReplyTo", "TEXT", cancellationToken)
            .ConfigureAwait(false);
        await AddColumnIfMissingAsync(db, "OutboxItems", "ReferencesJson", "TEXT NOT NULL DEFAULT '[]'", cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task ApplyV2Async(MailtideDbContext db, CancellationToken cancellationToken)
    {
        await AddColumnIfMissingAsync(db, "Mailboxes", "UidValidity", "INTEGER NOT NULL DEFAULT 0", cancellationToken)
            .ConfigureAwait(false);
        await ExecuteAsync(
                db,
                """
                CREATE VIRTUAL TABLE IF NOT EXISTS MessageFts USING fts5(
                    MessageId UNINDEXED,
                    Subject,
                    FromAddress,
                    BodyText,
                    HtmlText,
                    tokenize = 'unicode61'
                )
                """,
                cancellationToken)
            .ConfigureAwait(false);
        await MessageSearchIndex.RebuildAllAsync(db, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ApplyV3Async(MailtideDbContext db, CancellationToken cancellationToken)
    {
        await ExecuteAsync(
                db,
                """
                CREATE TABLE IF NOT EXISTS "Preferences" (
                    "Key" TEXT NOT NULL CONSTRAINT "PK_Preferences" PRIMARY KEY,
                    "Value" TEXT NOT NULL
                )
                """,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task ApplyV4Async(MailtideDbContext db, CancellationToken cancellationToken)
    {
        await AddColumnIfMissingAsync(db, "Messages", "BccAddresses", "TEXT NOT NULL DEFAULT '[]'", cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task ApplyV5Async(MailtideDbContext db, CancellationToken cancellationToken)
    {
        await AddColumnIfMissingAsync(db, "Messages", "ReplyToAddresses", "TEXT NOT NULL DEFAULT '[]'", cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task ApplyV6Async(MailtideDbContext db, CancellationToken cancellationToken)
    {
        await AddColumnIfMissingAsync(db, "Messages", "SizeBytes", "INTEGER NOT NULL DEFAULT 0", cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task AddColumnIfMissingAsync(

        MailtideDbContext db,
        string table,
        string column,
        string sqlType,
        CancellationToken cancellationToken)
    {
        if (await ColumnExistsAsync(db, table, column, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await ExecuteAsync(
                db,
                $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {sqlType}",
                cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<int> ReadVersionAsync(MailtideDbContext db, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Version FROM SchemaVersion WHERE Id = 1";
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null or DBNull ? 0 : Convert.ToInt32(result);
    }

    private static async Task SetVersionAsync(MailtideDbContext db, int version, CancellationToken cancellationToken)
    {
        await ExecuteAsync(
                db,
                $"UPDATE SchemaVersion SET Version = {version} WHERE Id = 1",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<bool> ColumnExistsAsync(
        MailtideDbContext db,
        string table,
        string column,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT 1 FROM pragma_table_info('{table}') WHERE name = @name LIMIT 1";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@name";
        parameter.Value = column;
        command.Parameters.Add(parameter);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null and not DBNull;
    }

    private static Task<int> ExecuteAsync(MailtideDbContext db, string sql, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
}
