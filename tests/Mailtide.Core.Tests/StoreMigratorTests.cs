using Mailtide.Core;
using Microsoft.Data.Sqlite;
using Mailtide.Core.Store;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class StoreMigratorTests
{
    [TestMethod]
    public async Task Open_assigns_schema_version_and_upgrades_legacy_columns()
    {
        using var fixture = new CoreAppFixture();
        Directory.CreateDirectory(fixture.AppDataDirectory);
        var path = Path.Combine(fixture.AppDataDirectory, "mailtide.db");
        await using (var seed = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            await seed.OpenAsync();
            await using var command = seed.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE "Accounts" (
                    "Id" TEXT NOT NULL PRIMARY KEY,
                    "DisplayName" TEXT NOT NULL,
                    "EmailAddress" TEXT NOT NULL,
                    "ImapHost" TEXT NOT NULL,
                    "ImapPort" INTEGER NOT NULL,
                    "SmtpHost" TEXT NOT NULL,
                    "SmtpPort" INTEGER NOT NULL,
                    "CredentialKind" TEXT NOT NULL,
                    "CredentialHandle" TEXT NOT NULL
                );
                CREATE TABLE "Messages" (
                    "Id" TEXT NOT NULL PRIMARY KEY,
                    "AccountId" TEXT NOT NULL,
                    "MailboxId" TEXT NOT NULL,
                    "RemoteId" TEXT NOT NULL,
                    "Subject" TEXT NOT NULL,
                    "FromAddress" TEXT NOT NULL,
                    "ReceivedAt" TEXT NOT NULL,
                    "IsRead" INTEGER NOT NULL,
                    "BodyText" TEXT NOT NULL
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using (var app = await fixture.OpenAppAsync())
        {
            var account = await app.AddManualAccountAsync(
                new ManualAccountDraft(
                    DisplayName: "Personal",
                    EmailAddress: "alice@example.com",
                    ImapHost: "imap.example.com",
                    ImapPort: 993,
                    SmtpHost: "smtp.example.com",
                    SmtpPort: 587,
                    Password: "s3cret-password"));
            Assert.IsNotNull(account);
        }

        await using var verify = new SqliteConnection($"Data Source={path};Pooling=False");
        await verify.OpenAsync();
        await using var versionCommand = verify.CreateCommand();
        versionCommand.CommandText = "SELECT Version FROM SchemaVersion WHERE Id = 1";
        var version = Convert.ToInt32(await versionCommand.ExecuteScalarAsync());
        Assert.AreEqual(StoreMigrator.CurrentVersion, version);
    }
}
