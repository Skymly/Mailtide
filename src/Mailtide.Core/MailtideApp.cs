using Mailtide.Core.Imap;
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
    private readonly IImapClientFactory _imapClientFactory;
    private readonly MailtideDbContext _db;
    private readonly Dictionary<Guid, AccountStatus> _accountStatuses = new();
    private readonly object _statusGate = new();

    private MailtideApp(
        string appDataDirectory,
        ISecureStorage secureStorage,
        IImapClientFactory imapClientFactory,
        MailtideDbContext db)
    {
        _appDataDirectory = appDataDirectory;
        _secureStorage = secureStorage;
        _imapClientFactory = imapClientFactory;
        _db = db;
    }

    public static async Task<MailtideApp> OpenAsync(
        string appDataDirectory,
        ISecureStorage secureStorage,
        IImapClientFactory imapClientFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataDirectory);
        ArgumentNullException.ThrowIfNull(secureStorage);
        ArgumentNullException.ThrowIfNull(imapClientFactory);

        Directory.CreateDirectory(appDataDirectory);

        var options = new DbContextOptionsBuilder<MailtideDbContext>()
            // Single-user desktop store: disable pooling so Dispose releases the file promptly.
            .UseSqlite($"Data Source={Path.Combine(appDataDirectory, "mailtide.db")};Pooling=False")
            .Options;

        var db = new MailtideDbContext(options);
        await db.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        return new MailtideApp(appDataDirectory, secureStorage, imapClientFactory, db);
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
            SetStatus(accountId, AccountStatus.Idle());

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

        var mailboxes = await _db.Mailboxes
            .Where(m => m.AccountId == accountId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var messages = await _db.Messages
            .Where(m => m.AccountId == accountId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        _db.Messages.RemoveRange(messages);
        _db.Mailboxes.RemoveRange(mailboxes);

        _db.Accounts.Remove(record);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        lock (_statusGate)
        {
            _accountStatuses.Remove(accountId);
        }

        var accountPartition = AccountPartitionPath(accountId);
        if (Directory.Exists(accountPartition))
        {
            Directory.Delete(accountPartition, recursive: true);
        }
    }

    public AccountStatus GetAccountStatus(Guid accountId)
    {
        lock (_statusGate)
        {
            return _accountStatuses.TryGetValue(accountId, out var status)
                ? status
                : AccountStatus.Idle();
        }
    }

    public async Task SyncNowAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var account = await _db.Accounts
            .SingleOrDefaultAsync(a => a.Id == accountId, cancellationToken)
            .ConfigureAwait(false);

        if (account is null)
        {
            throw new InvalidOperationException($"Account '{accountId}' was not found.");
        }

        var password = await _secureStorage
            .RetrieveSecretAsync(account.CredentialHandle, cancellationToken)
            .ConfigureAwait(false);

        if (password is null)
        {
            SetStatus(accountId, AccountStatus.Error(AuthenticationFailedMessage));
            return;
        }

        SetStatus(accountId, AccountStatus.Syncing());

        try
        {
            await using var client = _imapClientFactory.Create();
            await client
                .ConnectAndAuthenticateAsync(
                    account.ImapHost,
                    account.ImapPort,
                    account.EmailAddress,
                    password,
                    cancellationToken)
                .ConfigureAwait(false);

            var snapshot = await FetchRemoteSnapshotAsync(client, cancellationToken)
                .ConfigureAwait(false);

            await PersistSnapshotAsync(accountId, snapshot, cancellationToken)
                .ConfigureAwait(false);

            SetStatus(accountId, AccountStatus.Idle());
        }
        catch (OperationCanceledException)
        {
            _db.ChangeTracker.Clear();
            SetStatus(accountId, AccountStatus.Idle());
            throw;
        }
        catch (Exception ex)
        {
            _db.ChangeTracker.Clear();
            SetStatus(accountId, AccountStatus.Error(MapSyncFailure(ex)));
        }
    }

    public async Task<IReadOnlyList<MailboxInfo>> ListMailboxesAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var records = await _db.Mailboxes
            .AsNoTracking()
            .Where(m => m.AccountId == accountId)
            .OrderBy(m => m.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return records
            .Select(m => new MailboxInfo(m.Id, m.AccountId, m.Name, m.Path, m.Role))
            .ToList();
    }

    public async Task<IReadOnlyList<MessageInfo>> ListMessagesAsync(
        Guid accountId,
        Guid mailboxId,
        CancellationToken cancellationToken = default)
    {
        var records = await _db.Messages
            .AsNoTracking()
            .Where(m => m.AccountId == accountId && m.MailboxId == mailboxId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return records
            .OrderByDescending(m => m.ReceivedAt)
            .ThenBy(m => m.Subject)
            .Select(m => new MessageInfo(
                m.Id,
                m.AccountId,
                m.MailboxId,
                m.RemoteId,
                m.Subject,
                m.FromAddress,
                m.ReceivedAt,
                m.IsRead))
            .ToList();
    }

    public async Task<string?> GetMessageBodyAsync(
        Guid accountId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        var record = await _db.Messages
            .AsNoTracking()
            .SingleOrDefaultAsync(
                m => m.AccountId == accountId && m.Id == messageId,
                cancellationToken)
            .ConfigureAwait(false);

        return record?.BodyText;
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync().ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<RemoteMailboxSnapshot>> FetchRemoteSnapshotAsync(
        IImapClient client,
        CancellationToken cancellationToken)
    {
        var remoteMailboxes = await client
            .ListMailboxesAsync(cancellationToken)
            .ConfigureAwait(false);

        var snapshot = new List<RemoteMailboxSnapshot>(remoteMailboxes.Count);
        foreach (var mailbox in remoteMailboxes)
        {
            var messages = await client
                .FetchMessagesAsync(mailbox.Path, cancellationToken)
                .ConfigureAwait(false);
            snapshot.Add(new RemoteMailboxSnapshot(mailbox, messages));
        }

        return snapshot;
    }

    private async Task PersistSnapshotAsync(
        Guid accountId,
        IReadOnlyList<RemoteMailboxSnapshot> snapshot,
        CancellationToken cancellationToken)
    {
        var existingMessages = await _db.Messages
            .Where(m => m.AccountId == accountId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        _db.Messages.RemoveRange(existingMessages);

        var existingMailboxes = await _db.Mailboxes
            .Where(m => m.AccountId == accountId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        _db.Mailboxes.RemoveRange(existingMailboxes);

        foreach (var entry in snapshot)
        {
            var mailboxId = Guid.NewGuid();
            _db.Mailboxes.Add(new MailboxRecord
            {
                Id = mailboxId,
                AccountId = accountId,
                Name = entry.Mailbox.Name,
                Path = entry.Mailbox.Path,
                Role = entry.Mailbox.Role,
            });

            foreach (var remoteMessage in entry.Messages)
            {
                _db.Messages.Add(new MessageRecord
                {
                    Id = Guid.NewGuid(),
                    AccountId = accountId,
                    MailboxId = mailboxId,
                    RemoteId = remoteMessage.RemoteId,
                    Subject = remoteMessage.Subject,
                    FromAddress = remoteMessage.FromAddress,
                    ReceivedAt = remoteMessage.ReceivedAt,
                    IsRead = remoteMessage.IsRead,
                    BodyText = remoteMessage.BodyText,
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private void SetStatus(Guid accountId, AccountStatus status)
    {
        lock (_statusGate)
        {
            _accountStatuses[accountId] = status;
        }
    }

    private const string AuthenticationFailedMessage = "Authentication failed. Sign in again.";
    private const string SyncFailedMessage = "Could not sync this Account. Try again later.";

    private static string MapSyncFailure(Exception ex) =>
        ex is ImapAuthenticationException
            ? AuthenticationFailedMessage
            : SyncFailedMessage;

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

    private sealed record RemoteMailboxSnapshot(
        RemoteMailbox Mailbox,
        IReadOnlyList<RemoteMessage> Messages);
}
