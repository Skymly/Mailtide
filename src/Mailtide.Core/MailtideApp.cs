using Mailtide.Core.Security;
using Mailtide.Core.Store;
using Microsoft.EntityFrameworkCore;

namespace Mailtide.Core;

/// <summary>
/// Application surface for host/UI intents.
/// </summary>
public sealed class MailtideApp : IAsyncDisposable
{
    private readonly string _appDataDirectory;
    private readonly ISecureStorage _secureStorage;
    private readonly MailtideDbContext _db;

    private MailtideApp(
        string appDataDirectory,
        ISecureStorage secureStorage,
        MailtideDbContext db)
    {
        _appDataDirectory = appDataDirectory;
        _secureStorage = secureStorage;
        _db = db;
    }

    public static async Task<MailtideApp> OpenAsync(
        string appDataDirectory,
        ISecureStorage secureStorage,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataDirectory);
        ArgumentNullException.ThrowIfNull(secureStorage);

        Directory.CreateDirectory(appDataDirectory);

        var options = new DbContextOptionsBuilder<MailtideDbContext>()
            .UseSqlite($"Data Source={Path.Combine(appDataDirectory, "mailtide.db")}")
            .Options;

        var db = new MailtideDbContext(options);
        await db.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        return new MailtideApp(appDataDirectory, secureStorage, db);
    }

    public async Task<AccountInfo> AddManualAccountAsync(
        ManualAccountDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.Password);

        var accountId = Guid.NewGuid();
        var credentialHandle = $"account:{accountId:D}:credential";

        await _secureStorage
            .StoreSecretAsync(credentialHandle, draft.Password, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var record = new AccountRecord
            {
                Id = accountId,
                DisplayName = draft.DisplayName,
                EmailAddress = draft.EmailAddress,
                ImapHost = draft.ImapHost,
                ImapPort = draft.ImapPort,
                SmtpHost = draft.SmtpHost,
                SmtpPort = draft.SmtpPort,
                CredentialKind = CredentialKind.Password,
                CredentialHandle = credentialHandle,
            };

            _db.Accounts.Add(record);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            Directory.CreateDirectory(AccountPartitionPath(accountId));

            return ToInfo(record);
        }
        catch
        {
            await _secureStorage
                .DeleteSecretAsync(credentialHandle, CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
    }

    public async Task<IReadOnlyList<AccountInfo>> ListAccountsAsync(
        CancellationToken cancellationToken = default)
    {
        var records = await _db.Accounts
            .AsNoTracking()
            .OrderBy(a => a.DisplayName)
            .ThenBy(a => a.EmailAddress)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return records.Select(ToInfo).ToList();
    }

    public async Task RemoveAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var record = await _db.Accounts
            .SingleOrDefaultAsync(a => a.Id == accountId, cancellationToken)
            .ConfigureAwait(false);

        if (record is null)
        {
            return;
        }

        // Clear the Credential first so a later store failure cannot leave an orphaned secret.
        await _secureStorage
            .DeleteSecretAsync(record.CredentialHandle, cancellationToken)
            .ConfigureAwait(false);

        _db.Accounts.Remove(record);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var accountPartition = AccountPartitionPath(accountId);
        if (Directory.Exists(accountPartition))
        {
            Directory.Delete(accountPartition, recursive: true);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync().ConfigureAwait(false);
    }

    private string AccountPartitionPath(Guid accountId) =>
        Path.Combine(_appDataDirectory, "accounts", accountId.ToString("D"));

    private static AccountInfo ToInfo(AccountRecord record) =>
        new(
            record.Id,
            record.DisplayName,
            record.EmailAddress,
            record.ImapHost,
            record.ImapPort,
            record.SmtpHost,
            record.SmtpPort,
            record.CredentialKind,
            record.CredentialHandle);
}
