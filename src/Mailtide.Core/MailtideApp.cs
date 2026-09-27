using System.Collections.Concurrent;
using Mailtide.Core.Auth;
using Mailtide.Core.Imap;
using Mailtide.Core.Security;
using Mailtide.Core.Smtp;
using Mailtide.Core.Outbox;
using Mailtide.Core.Store;
using Mailtide.Core.Sync;
using Microsoft.EntityFrameworkCore;

namespace Mailtide.Core;

/// <summary>
/// Application surface for host/UI intents.
/// </summary>
public sealed partial class MailtideApp : IAsyncDisposable
{
    private readonly string _appDataDirectory;
    private readonly ISecureStorage _secureStorage;
    private readonly AccountCredentialAuth _auth;
    private readonly IImapClientFactory _imapClientFactory;
    private readonly ISmtpClientFactory _smtpClientFactory;
    private readonly MailtideDbContext _db;
    private readonly ImapSessionPool _imapSessions;
    private readonly RemoteSnapshotSync _snapshotSync;
    private readonly OutboxStateMachine _outbox;
    private readonly Dictionary<Guid, AccountStatus> _accountStatuses = new();
    private readonly object _statusGate = new();
    // DbContext is not thread-safe; serialize all store access on this single-user desktop app.
    private readonly SemaphoreSlim _dbGate = new(1, 1);
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _accountWorkGates = new();
    private readonly object _foregroundGate = new();
    public event EventHandler<Guid>? AccountWorkCompleted;
    public event EventHandler<InboxArrival>? InboxMessageArrived;
    private readonly HashSet<Guid> _notifiedInboxMessageIds = [];
    private readonly object _inboxArrivalGate = new();
    private CancellationTokenSource? _foregroundCts;
    private Task? _foregroundTask;
    public static readonly TimeSpan DefaultForegroundSyncInterval = TimeSpan.FromMinutes(5);

    private MailtideApp(
        string appDataDirectory,
        ISecureStorage secureStorage,
        IOAuthClient oauthClient,
        IImapClientFactory imapClientFactory,
        ISmtpClientFactory smtpClientFactory,
        MailtideDbContext db)
    {
        _appDataDirectory = appDataDirectory;
        _secureStorage = secureStorage;
        _auth = new AccountCredentialAuth(oauthClient, secureStorage);
        _imapClientFactory = imapClientFactory;
        _smtpClientFactory = smtpClientFactory;
        _db = db;
        _imapSessions = new ImapSessionPool(imapClientFactory);
        _snapshotSync = new RemoteSnapshotSync(db, appDataDirectory);
        _outbox = new OutboxStateMachine(db);
    }

    public static async Task<MailtideApp> OpenAsync(
        string appDataDirectory,
        ISecureStorage secureStorage,
        IOAuthClient oauthClient,
        IImapClientFactory imapClientFactory,
        ISmtpClientFactory smtpClientFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataDirectory);
        ArgumentNullException.ThrowIfNull(secureStorage);
        ArgumentNullException.ThrowIfNull(oauthClient);
        ArgumentNullException.ThrowIfNull(imapClientFactory);
        ArgumentNullException.ThrowIfNull(smtpClientFactory);

        Directory.CreateDirectory(appDataDirectory);

        var options = new DbContextOptionsBuilder<MailtideDbContext>()
            // Single-user desktop store: disable pooling so Dispose releases the file promptly.
            .UseSqlite($"Data Source={Path.Combine(appDataDirectory, "mailtide.db")};Pooling=False")
            .Options;

        var db = new MailtideDbContext(options);
        await StoreMigrator.ApplyAsync(db, cancellationToken).ConfigureAwait(false);

        return new MailtideApp(
            appDataDirectory,
            secureStorage,
            oauthClient,
            imapClientFactory,
            smtpClientFactory,
            db);
    }

    public Task<AccountInfo> AddQqMailAccountAsync(
        QqMailAccountDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.AuthorizationCode);

        return AddManualAccountAsync(
            new ManualAccountDraft(
                DisplayName: draft.DisplayName,
                EmailAddress: draft.EmailAddress,
                ImapHost: QqMailPreset.ImapHost,
                ImapPort: QqMailPreset.ImapPort,
                SmtpHost: QqMailPreset.SmtpHost,
                SmtpPort: QqMailPreset.SmtpPort,
                Password: draft.AuthorizationCode),
            cancellationToken);
    }

    public Task<AccountInfo> AddGoogleAccountAsync(
        string displayName,
        CancellationToken cancellationToken = default) =>
        AddOAuthAccountAsync(
            displayName,
            OAuthProvider.Google,
            GoogleMailPreset.ImapHost,
            GoogleMailPreset.ImapPort,
            GoogleMailPreset.SmtpHost,
            GoogleMailPreset.SmtpPort,
            cancellationToken);

    public Task<AccountInfo> AddMicrosoftConsumerAccountAsync(
        string displayName,
        CancellationToken cancellationToken = default) =>
        AddOAuthAccountAsync(
            displayName,
            OAuthProvider.MicrosoftConsumer,
            MicrosoftConsumerMailPreset.ImapHost,
            MicrosoftConsumerMailPreset.ImapPort,
            MicrosoftConsumerMailPreset.SmtpHost,
            MicrosoftConsumerMailPreset.SmtpPort,
            cancellationToken);

    private async Task<AccountInfo> AddOAuthAccountAsync(
        string displayName,
        OAuthProvider provider,
        string imapHost,
        int imapPort,
        string smtpHost,
        int smtpPort,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var authorization = await _auth
            .ObtainAsync(provider, cancellationToken)
            .ConfigureAwait(false);

        ArgumentException.ThrowIfNullOrWhiteSpace(authorization.EmailAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(authorization.RefreshSecret);
        ArgumentNullException.ThrowIfNull(authorization.Metadata);

        if (authorization.Metadata.Provider != provider)
        {
            throw new InvalidOperationException(
                $"OAuth provider mismatch: expected '{provider}', got '{authorization.Metadata.Provider}'.");
        }

        var expectedAuthority = provider switch
        {
            OAuthProvider.Google => GoogleMailPreset.Authority,
            OAuthProvider.MicrosoftConsumer => MicrosoftConsumerMailPreset.Authority,
            _ => throw new InvalidOperationException($"Unsupported OAuth provider '{provider}'."),
        };

        if (!string.Equals(
                authorization.Metadata.Authority,
                expectedAuthority,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"OAuth authority mismatch for '{provider}': expected '{expectedAuthority}'.");
        }

        var accountId = Guid.NewGuid();
        var credentialHandle = $"account:{accountId:D}:credential";

        await _auth
            .StoreRefreshSecretAsync(credentialHandle, authorization.RefreshSecret, cancellationToken)
            .ConfigureAwait(false);

        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                var record = new AccountRecord
                {
                    Id = accountId,
                    DisplayName = displayName,
                    EmailAddress = authorization.EmailAddress,
                    ImapHost = imapHost,
                    ImapPort = imapPort,
                    SmtpHost = smtpHost,
                    SmtpPort = smtpPort,
                    CredentialKind = CredentialKind.OAuth,
                    CredentialHandle = credentialHandle,
                    OAuthProvider = authorization.Metadata.Provider,
                    OAuthAuthority = authorization.Metadata.Authority,
                    OAuthClientId = authorization.Metadata.ClientId,
                };

                _db.Accounts.Add(record);
                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                Directory.CreateDirectory(AccountPartitionPath(accountId));
                SetStatus(accountId, AccountStatus.Idle());

                return ToInfo(record);
            }
            catch
            {
                await _auth
                    .DeleteCredentialSecretAsync(credentialHandle, CancellationToken.None)
                    .ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _dbGate.Release();
        }
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

        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
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
        finally
        {
            _dbGate.Release();
        }
    }

    public async Task<IReadOnlyList<AccountInfo>> ListAccountsAsync(
        CancellationToken cancellationToken = default)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = await _db.Accounts
                .AsNoTracking()
                .OrderBy(a => a.DisplayName)
                .ThenBy(a => a.EmailAddress)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var unreadByAccount = await _db.Messages
                .AsNoTracking()
                .Where(m => !m.IsRead)
                .GroupBy(m => m.AccountId)
                .Select(g => new { AccountId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.AccountId, g => g.Count, cancellationToken)
                .ConfigureAwait(false);

            return records
                .Select(record => ToInfo(record) with
                {
                    UnreadCount = unreadByAccount.GetValueOrDefault(record.Id),
                })
                .ToList();
        }
        finally
        {
            _dbGate.Release();
        }
    }

    public async Task RemoveAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
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
            var attachments = await _db.Attachments
                .Where(a => a.AccountId == accountId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var drafts = await _db.Drafts
                .Where(d => d.AccountId == accountId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var draftAttachments = await _db.DraftAttachments
                .Where(a => a.AccountId == accountId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var outboxItems = await _db.OutboxItems
                .Where(o => o.AccountId == accountId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var outboxAttachments = await _db.OutboxAttachments
                .Where(a => a.AccountId == accountId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            _db.Attachments.RemoveRange(attachments);
            _db.Messages.RemoveRange(messages);
            _db.Mailboxes.RemoveRange(mailboxes);
            _db.DraftAttachments.RemoveRange(draftAttachments);
            _db.Drafts.RemoveRange(drafts);
            _db.OutboxAttachments.RemoveRange(outboxAttachments);
            _db.OutboxItems.RemoveRange(outboxItems);

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
        finally
        {
            _dbGate.Release();
        }
    }

    /// <summary>
    /// Updates display fields and optionally the password Credential on a Password Account.
    /// Blank/omitted <see cref="ManualAccountDraft.Password"/> keeps the existing secret.
    /// Missing Account is a no-op (returns null), matching <see cref="RemoveAccountAsync"/>.
    /// </summary>
    public async Task<AccountInfo?> UpdateManualAccountAsync(
        Guid accountId,
        ManualAccountDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var record = await _db.Accounts
                .SingleOrDefaultAsync(a => a.Id == accountId, cancellationToken)
                .ConfigureAwait(false);

            if (record is null)
            {
                return null;
            }

            if (record.CredentialKind != CredentialKind.Password)
            {
                throw new InvalidOperationException(
                    $"Account '{accountId}' is not a Password Account.");
            }

            if (!string.IsNullOrWhiteSpace(draft.Password))
            {
                await _secureStorage
                    .StoreSecretAsync(record.CredentialHandle, draft.Password, cancellationToken)
                    .ConfigureAwait(false);
            }

            record.DisplayName = draft.DisplayName;
            record.EmailAddress = draft.EmailAddress;
            record.ImapHost = draft.ImapHost;
            record.ImapPort = draft.ImapPort;
            record.SmtpHost = draft.SmtpHost;
            record.SmtpPort = draft.SmtpPort;

            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            SetStatus(accountId, AccountStatus.Idle());
            return ToInfo(record);
        }
        finally
        {
            _dbGate.Release();
        }
    }

    /// <summary>
    /// Updates a QQ Mail Password Account. Endpoints stay <see cref="QqMailPreset"/>.
    /// Blank/omitted <see cref="QqMailAccountDraft.AuthorizationCode"/> keeps the existing secret.
    /// Missing Account is a no-op (returns null), matching <see cref="RemoveAccountAsync"/>.
    /// </summary>
    public Task<AccountInfo?> UpdateQqMailAccountAsync(
        Guid accountId,
        QqMailAccountDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return UpdateManualAccountAsync(
            accountId,
            new ManualAccountDraft(
                DisplayName: draft.DisplayName,
                EmailAddress: draft.EmailAddress,
                ImapHost: QqMailPreset.ImapHost,
                ImapPort: QqMailPreset.ImapPort,
                SmtpHost: QqMailPreset.SmtpHost,
                SmtpPort: QqMailPreset.SmtpPort,
                Password: draft.AuthorizationCode),
            cancellationToken);
    }

    /// <summary>
    /// Re-runs OAuth obtain for an OAuth Account, replaces the refresh secret under the same
    /// CredentialHandle, and updates EmailAddress when the provider returns a different one.
    /// Missing Account is a no-op (returns null), matching <see cref="RemoveAccountAsync"/>.
    /// </summary>
    public async Task<AccountInfo?> ReauthorizeAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        AccountRecord? record;
        OAuthProvider provider;

        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            record = await _db.Accounts
                .SingleOrDefaultAsync(a => a.Id == accountId, cancellationToken)
                .ConfigureAwait(false);

            if (record is null)
            {
                return null;
            }

            if (record.CredentialKind != CredentialKind.OAuth || record.OAuthProvider is null)
            {
                throw new InvalidOperationException(
                    $"Account '{accountId}' is not an OAuth Account.");
            }

            provider = record.OAuthProvider.Value;
        }
        finally
        {
            _dbGate.Release();
        }

        var authorization = await _auth
            .ObtainAsync(provider, cancellationToken)
            .ConfigureAwait(false);

        ArgumentException.ThrowIfNullOrWhiteSpace(authorization.EmailAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(authorization.RefreshSecret);
        ArgumentNullException.ThrowIfNull(authorization.Metadata);

        if (authorization.Metadata.Provider != provider)
        {
            throw new InvalidOperationException(
                $"OAuth provider mismatch: expected '{provider}', got '{authorization.Metadata.Provider}'.");
        }

        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            record = await _db.Accounts
                .SingleOrDefaultAsync(a => a.Id == accountId, cancellationToken)
                .ConfigureAwait(false);

            if (record is null)
            {
                return null;
            }

            if (record.CredentialKind != CredentialKind.OAuth
                || record.OAuthProvider != provider)
            {
                throw new InvalidOperationException(
                    $"Account '{accountId}' is not an OAuth Account.");
            }

            await _auth
                .StoreRefreshSecretAsync(
                    record.CredentialHandle,
                    authorization.RefreshSecret,
                    cancellationToken)
                .ConfigureAwait(false);

            record.EmailAddress = authorization.EmailAddress;

            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            SetStatus(accountId, AccountStatus.Idle());
            return ToInfo(record);
        }
        finally
        {
            _dbGate.Release();
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

    public void StartForegroundSync(TimeSpan? interval = null)
    {
        lock (_foregroundGate)
        {
            if (_foregroundCts is not null)
            {
                return;
            }

            _foregroundCts = new CancellationTokenSource();
            var cancellationToken = _foregroundCts.Token;
            var period = interval ?? DefaultForegroundSyncInterval;
            _foregroundTask = Task.Run(
                () => Task.WhenAll(
                    RunForegroundSyncAsync(period, cancellationToken),
                    RunForegroundIdleAsync(cancellationToken)),
                CancellationToken.None);
        }
    }

    public async Task StopForegroundSyncAsync()
    {
        Task? running;
        CancellationTokenSource? cts;
        lock (_foregroundGate)
        {
            cts = _foregroundCts;
            running = _foregroundTask;
            _foregroundCts = null;
            _foregroundTask = null;
        }

        if (cts is null)
        {
            return;
        }

        await cts.CancelAsync().ConfigureAwait(false);
        if (running is not null)
        {
            try
            {
                await running.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        cts.Dispose();
    }

    private async Task RunForegroundSyncAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        while (!cancellationToken.IsCancellationRequested)
        {
            var accounts = await ListAccountsAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(
                    accounts.Select(account => SyncAndSendAccountAsync(account.Id, cancellationToken)))
                .ConfigureAwait(false);

            if (!await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                break;
            }
        }
    }

    private async Task SyncAndSendAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await SyncNowAsync(accountId, cancellationToken).ConfigureAwait(false);
        await SendNowAsync(accountId, cancellationToken).ConfigureAwait(false);
        AccountWorkCompleted?.Invoke(this, accountId);
    }

    private async Task RunForegroundIdleAsync(CancellationToken cancellationToken)
    {
        var loops = new Dictionary<Guid, Task>();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var accounts = await ListAccountsAsync(cancellationToken).ConfigureAwait(false);
                foreach (var account in accounts)
                {
                    if (loops.TryGetValue(account.Id, out var existing) && !existing.IsCompleted)
                    {
                        continue;
                    }

                    loops[account.Id] = IdleAccountAsync(account.Id, cancellationToken);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }

        try
        {
            await Task.WhenAll(loops.Values).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task IdleAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var inbox = (await ListMailboxesAsync(accountId, cancellationToken).ConfigureAwait(false))
                    .FirstOrDefault(mailbox => mailbox.Role == MailboxRole.Inbox);
                if (inbox is null)
                {
                    await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await WaitForInboxChangeAsync(accountId, inbox.Path, cancellationToken).ConfigureAwait(false);
                await SyncAndSendAccountAsync(accountId, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ClearTrackerAsync(CancellationToken cancellationToken)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _db.ChangeTracker.Clear();
        }
        finally
        {
            _dbGate.Release();
        }
    }

    public async Task<IReadOnlyList<MailboxInfo>> ListMailboxesAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = await _db.Mailboxes
                .AsNoTracking()
                .Where(m => m.AccountId == accountId)
                .OrderBy(m => m.Name)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var unreadByMailbox = await _db.Messages
                .AsNoTracking()
                .Where(m => m.AccountId == accountId && !m.IsRead)
                .GroupBy(m => m.MailboxId)
                .Select(g => new { MailboxId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.MailboxId, g => g.Count, cancellationToken)
                .ConfigureAwait(false);

            return records
                .Select(m => new MailboxInfo(m.Id, m.AccountId, m.Name, m.Path, m.Role)
                {
                    UnreadCount = unreadByMailbox.GetValueOrDefault(m.Id),
                })
                .ToList();
        }
        finally
        {
            _dbGate.Release();
        }
    }

    public async Task<IReadOnlyList<MessageInfo>> ListMessagesAsync(
        Guid accountId,
        Guid mailboxId,
        CancellationToken cancellationToken = default)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = await _db.Messages
                .AsNoTracking()
                .Where(m => m.AccountId == accountId && m.MailboxId == mailboxId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var attachments = await AttachmentMessageIdsAsync(records, cancellationToken)
                .ConfigureAwait(false);
            return records
                .OrderByDescending(m => m.ReceivedAt)
                .ThenBy(m => m.Subject)
                .Select(record => ToMessageInfo(record, attachments))
                .ToList();
        }
        finally
        {
            _dbGate.Release();
        }
    }

    public async Task<IReadOnlyList<MessageThreadInfo>> ListMailboxThreadsAsync(
        Guid accountId,
        Guid mailboxId,
        CancellationToken cancellationToken = default)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = await _db.Messages
                .AsNoTracking()
                .Where(m => m.AccountId == accountId && m.MailboxId == mailboxId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var attachments = await AttachmentMessageIdsAsync(records, cancellationToken)
                .ConfigureAwait(false);
            return ReplyThreadIndex.Group(records, record => ToMessageInfo(record, attachments));
        }
        finally
        {
            _dbGate.Release();
        }
    }

    /// <summary>
    /// Aggregates Messages from every Account's Inbox-role Mailbox as a query view —
    /// not a stored Mailbox/container.
    /// </summary>
    public async Task<IReadOnlyList<MessageInfo>> ListUnifiedInboxAsync(
        CancellationToken cancellationToken = default)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var inboxMailboxIds = await _db.Mailboxes
                .AsNoTracking()
                .Where(m => m.Role == MailboxRole.Inbox)
                .Select(m => m.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (inboxMailboxIds.Count == 0)
            {
                return [];
            }

            var records = await _db.Messages
                .AsNoTracking()
                .Where(m => inboxMailboxIds.Contains(m.MailboxId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var attachments = await AttachmentMessageIdsAsync(records, cancellationToken)
                .ConfigureAwait(false);
            return records
                .OrderByDescending(m => m.ReceivedAt)
                .ThenBy(m => m.Subject)
                .Select(record => ToMessageInfo(record, attachments))
                .ToList();
        }
        finally
        {
            _dbGate.Release();
        }
    }

    public async Task<IReadOnlyList<MessageThreadInfo>> ListUnifiedInboxThreadsAsync(
        CancellationToken cancellationToken = default)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var inboxMailboxIds = await _db.Mailboxes
                .AsNoTracking()
                .Where(m => m.Role == MailboxRole.Inbox)
                .Select(m => m.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (inboxMailboxIds.Count == 0)
            {
                return [];
            }

            var records = await _db.Messages
                .AsNoTracking()
                .Where(m => inboxMailboxIds.Contains(m.MailboxId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var attachments = await AttachmentMessageIdsAsync(records, cancellationToken)
                .ConfigureAwait(false);
            return ReplyThreadIndex.Group(records, record => ToMessageInfo(record, attachments));
        }
        finally
        {
            _dbGate.Release();
        }
    }

    public async Task<string?> GetMessageBodyAsync(
        Guid accountId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var record = await _db.Messages
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    m => m.AccountId == accountId && m.Id == messageId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (record is null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(record.BodyText))
            {
                return record.BodyText;
            }

            var stripped = HtmlText.Strip(record.BodyHtml);
            return string.IsNullOrWhiteSpace(stripped) ? record.BodyText : stripped;
        }
        finally
        {
            _dbGate.Release();
        }
    }

    public async Task<string?> GetMessageHtmlAsync(
        Guid accountId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var record = await _db.Messages
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    m => m.AccountId == accountId && m.Id == messageId,
                    cancellationToken)
                .ConfigureAwait(false);

            return record?.BodyHtml;
        }
        finally
        {
            _dbGate.Release();
        }
    }

    public async Task<string?> GetMessageHtmlForDisplayAsync(
        Guid accountId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        var html = await GetMessageHtmlAsync(accountId, messageId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(html))
        {
            return html;
        }

        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = await _db.Attachments
                .AsNoTracking()
                .Where(a => a.AccountId == accountId && a.MessageId == messageId && a.ContentId != null)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (records.Count == 0)
            {
                return html;
            }

            var parts = new List<(string ContentId, string ContentType, byte[] Content)>();
            foreach (var record in records)
            {
                var blobPath = Path.Combine(_appDataDirectory, record.BlobRelativePath);
                if (!File.Exists(blobPath) || string.IsNullOrWhiteSpace(record.ContentId))
                {
                    continue;
                }

                var bytes = await File.ReadAllBytesAsync(blobPath, cancellationToken).ConfigureAwait(false);
                parts.Add((record.ContentId, record.ContentType, bytes));
            }

            return HtmlCidInliner.Inline(html, parts);
        }
        finally
        {
            _dbGate.Release();
        }
    }

    public async Task MarkMailboxReadAsync(
        Guid accountId,
        Guid mailboxId,
        CancellationToken cancellationToken = default)
    {
        List<Guid> unreadIds;
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            unreadIds = await _db.Messages
                .AsNoTracking()
                .Where(m => m.AccountId == accountId && m.MailboxId == mailboxId && !m.IsRead)
                .Select(m => m.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _dbGate.Release();
        }

        foreach (var messageId in unreadIds)
        {
            await MarkReadAsync(accountId, messageId, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task MarkUnifiedInboxReadAsync(CancellationToken cancellationToken = default)
    {
        List<(Guid AccountId, Guid MessageId)> unread;
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var inboxIds = await _db.Mailboxes
                .AsNoTracking()
                .Where(m => m.Role == MailboxRole.Inbox)
                .Select(m => m.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            unread = await _db.Messages
                .AsNoTracking()
                .Where(m => inboxIds.Contains(m.MailboxId) && !m.IsRead)
                .Select(m => new ValueTuple<Guid, Guid>(m.AccountId, m.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _dbGate.Release();
        }

        foreach (var (accountId, messageId) in unread)
        {
            await MarkReadAsync(accountId, messageId, cancellationToken).ConfigureAwait(false);
        }
    }


    public async Task<IReadOnlyList<AttachmentInfo>> ListAttachmentsAsync(
        Guid accountId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = await _db.Attachments
                .AsNoTracking()
                .Where(a => a.AccountId == accountId && a.MessageId == messageId)
                .OrderBy(a => a.FileName)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return records
                .Select(a => new AttachmentInfo(
                    a.Id,
                    a.MessageId,
                    a.AccountId,
                    a.FileName,
                    a.ContentType)
                {
                    ContentOmitted = a.ContentOmitted,
                })
                .ToList();
        }
        finally
        {
            _dbGate.Release();
        }
    }

    public async Task<AttachmentContent?> OpenAttachmentAsync(
        Guid accountId,
        Guid attachmentId,
        CancellationToken cancellationToken = default)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var record = await _db.Attachments
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    a => a.AccountId == accountId && a.Id == attachmentId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (record is null)
            {
                return null;
            }

            var blobPath = Path.Combine(_appDataDirectory, record.BlobRelativePath);
            if (!File.Exists(blobPath))
            {
                return null;
            }

            var content = await File
                .ReadAllBytesAsync(blobPath, cancellationToken)
                .ConfigureAwait(false);

            return new AttachmentContent(
                record.Id,
                record.FileName,
                record.ContentType,
                content);
        }
        finally
        {
            _dbGate.Release();
        }
    }



    public async Task<IReadOnlyList<DraftInfo>> ListDraftsAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = await _db.Drafts
                .AsNoTracking()
                .Where(d => d.AccountId == accountId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return records
                .OrderByDescending(d => d.UpdatedAt)
                .ThenBy(d => d.Subject)
                .Select(ToDraftInfo)
                .ToList();
        }
        finally
        {
            _dbGate.Release();
        }
    }

    public async Task DiscardDraftAsync(
        Guid accountId,
        Guid draftId,
        CancellationToken cancellationToken = default)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var draft = await _db.Drafts
                .SingleOrDefaultAsync(d => d.AccountId == accountId && d.Id == draftId, cancellationToken)
                .ConfigureAwait(false);
            if (draft is null)
            {
                return;
            }

            await DeleteDraftAttachmentsLockedAsync(accountId, draftId, cancellationToken).ConfigureAwait(false);
            _db.Drafts.Remove(draft);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _dbGate.Release();
        }
    }

    public async Task<DraftAttachmentInfo> AddDraftAttachmentAsync(
        Guid accountId,
        Guid draftId,
        string fileName,
        string contentType,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentNullException.ThrowIfNull(content);
        contentType = AttachmentContentType.Resolve(fileName, contentType);

        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var draftExists = await _db.Drafts
                .AsNoTracking()
                .AnyAsync(d => d.AccountId == accountId && d.Id == draftId, cancellationToken)
                .ConfigureAwait(false);
            if (!draftExists)
            {
                throw new InvalidOperationException($"Draft '{draftId}' was not found.");
            }

            var attachmentId = Guid.NewGuid();
            var blobRelativePath = BlobRelativePath(accountId, attachmentId);
            var blobPath = Path.Combine(_appDataDirectory, blobRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(blobPath)!);
            await File.WriteAllBytesAsync(blobPath, content, cancellationToken).ConfigureAwait(false);

            var record = new DraftAttachmentRecord
            {
                Id = attachmentId,
                AccountId = accountId,
                DraftId = draftId,
                FileName = fileName,
                ContentType = contentType,
                BlobRelativePath = blobRelativePath,
            };
            _db.DraftAttachments.Add(record);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new DraftAttachmentInfo(record.Id, record.DraftId, record.AccountId, record.FileName, record.ContentType);
        }
        finally
        {
            _dbGate.Release();
        }
    }

    public async Task<IReadOnlyList<DraftAttachmentInfo>> ListDraftAttachmentsAsync(
        Guid accountId,
        Guid draftId,
        CancellationToken cancellationToken = default)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = await _db.DraftAttachments
                .AsNoTracking()
                .Where(a => a.AccountId == accountId && a.DraftId == draftId)
                .OrderBy(a => a.FileName)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            return records
                .Select(a => new DraftAttachmentInfo(a.Id, a.DraftId, a.AccountId, a.FileName, a.ContentType))
                .ToList();
        }
        finally
        {
            _dbGate.Release();
        }
    }

    public async Task RemoveDraftAttachmentAsync(
        Guid accountId,
        Guid attachmentId,
        CancellationToken cancellationToken = default)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var record = await _db.DraftAttachments
                .SingleOrDefaultAsync(
                    a => a.AccountId == accountId && a.Id == attachmentId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (record is null)
            {
                return;
            }

            var blobPath = Path.Combine(_appDataDirectory, record.BlobRelativePath);
            if (File.Exists(blobPath))
            {
                File.Delete(blobPath);
            }

            _db.DraftAttachments.Remove(record);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _dbGate.Release();
        }
    }

    private async Task DeleteDraftAttachmentsLockedAsync(
        Guid accountId,
        Guid draftId,
        CancellationToken cancellationToken)
    {
        var attachments = await _db.DraftAttachments
            .Where(a => a.AccountId == accountId && a.DraftId == draftId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var attachment in attachments)
        {
            var blobPath = Path.Combine(_appDataDirectory, attachment.BlobRelativePath);
            if (File.Exists(blobPath))
            {
                File.Delete(blobPath);
            }
        }

        _db.DraftAttachments.RemoveRange(attachments);
    }


    public async Task<IReadOnlyList<OutboxItemInfo>> ListOutboxAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = await _db.OutboxItems
                .AsNoTracking()
                .Where(o => o.AccountId == accountId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return records
                .OrderByDescending(o => o.UpdatedAt)
                .ThenBy(o => o.Subject)
                .Select(ToOutboxItemInfo)
                .ToList();
        }
        finally
        {
            _dbGate.Release();
        }
    }

    public async Task SendNowAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var workGate = AccountWorkGate(accountId);
        await workGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
        AccountInfo account;
        string credentialHandle;
        CredentialKind credentialKind;
        OAuthTokenMetadata? oauthMetadata = null;
        string? secret;
        List<Guid> itemIds;

        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var accountRecord = await _db.Accounts
                .AsNoTracking()
                .SingleOrDefaultAsync(a => a.Id == accountId, cancellationToken)
                .ConfigureAwait(false);

            if (accountRecord is null)
            {
                throw new InvalidOperationException($"Account '{accountId}' was not found.");
            }

            account = ToInfo(accountRecord);
            credentialHandle = accountRecord.CredentialHandle;
            credentialKind = accountRecord.CredentialKind;
            if (accountRecord.CredentialKind == CredentialKind.OAuth)
            {
                oauthMetadata = RequireOAuthMetadata(accountRecord);
            }

            var items = await _db.OutboxItems
                .Where(o => o.AccountId == accountId && o.State == OutboxItemState.Queued)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            items = items
                .OrderBy(o => o.UpdatedAt)
                .ToList();

            if (items.Count == 0)
            {
                return;
            }

            secret = await _auth
                .RetrieveCredentialSecretAsync(account.CredentialHandle, cancellationToken)
                .ConfigureAwait(false);

            if (secret is null)
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var item in items)
                {
                    item.State = OutboxItemState.Failed;
                    item.ErrorMessage = AuthenticationFailedMessage;
                    item.UpdatedAt = now;
                }

                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            itemIds = items.Select(i => i.Id).ToList();
        }
        finally
        {
            _dbGate.Release();
        }

        string? protocolSecret;
        try
        {
            protocolSecret = await ResolveProtocolSecretAsync(
                    credentialKind,
                    oauthMetadata,
                    secret!,
                    credentialHandle,
                    invalidateOnAuthFailure: true,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await FailQueuedOutboxItemsAsync(accountId, itemIds, MapSendFailure(ex), cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (protocolSecret is null)
        {
            await FailQueuedOutboxItemsAsync(
                    accountId,
                    itemIds,
                    AuthenticationFailedMessage,
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        ISmtpClient? client = null;
        try
        {
            client = _smtpClientFactory.Create();
            await client
                .ConnectAndAuthenticateAsync(
                    account.SmtpHost,
                    account.SmtpPort,
                    account.EmailAddress,
                    protocolSecret,
                    cancellationToken)
                .ConfigureAwait(false);

            foreach (var itemId in itemIds)
            {
                OutboundMessage? outbound = null;

                await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var item = await _db.OutboxItems
                        .SingleOrDefaultAsync(
                            o => o.AccountId == accountId && o.Id == itemId,
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (item is null || item.State != OutboxItemState.Queued)
                    {
                        continue;
                    }

                    item.State = OutboxItemState.Sending;
                    item.ErrorMessage = null;
                    item.UpdatedAt = DateTimeOffset.UtcNow;
                    await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    var outboxAttachments = await _db.OutboxAttachments
                        .AsNoTracking()
                        .Where(a => a.AccountId == accountId && a.OutboxItemId == item.Id)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                    var outboundAttachments = new List<OutboundAttachment>();
                    foreach (var attachment in outboxAttachments)
                    {
                        var blobPath = Path.Combine(_appDataDirectory, attachment.BlobRelativePath);
                        if (!File.Exists(blobPath))
                        {
                            continue;
                        }

                        outboundAttachments.Add(new OutboundAttachment(
                            attachment.FileName,
                            attachment.ContentType,
                            await File.ReadAllBytesAsync(blobPath, cancellationToken).ConfigureAwait(false)));
                    }

                    outbound = new OutboundMessage(
                        account.EmailAddress,
                        PackedStringList.Decode(item.ToAddresses),
                        item.Subject,
                        item.BodyText)
                    {
                        CcAddresses = PackedStringList.Decode(item.CcAddresses),
                        BccAddresses = PackedStringList.Decode(item.BccAddresses),
                        InReplyTo = item.InReplyTo,
                        References = PackedStringList.Decode(item.ReferencesJson),
                        Attachments = outboundAttachments,
                        BodyHtml = item.BodyHtml,
                    };
                }
                finally
                {
                    _dbGate.Release();
                }

                if (outbound is null)
                {
                    continue;
                }

                try
                {
                    await client.SubmitAsync(outbound, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    await RequeueSendingOutboxItemAsync(accountId, itemId, CancellationToken.None)
                        .ConfigureAwait(false);
                    throw;
                }
                catch (Exception ex)
                {
                    await MarkOutboxItemFailedAsync(
                            accountId,
                            itemId,
                            MapSendFailure(ex),
                            cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                // SMTP accepted — clear the row with CancellationToken.None so cancel/DB
                // errors here cannot requeue or Fail the item (duplicate send).
                try
                {
                    await _dbGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                    try
                    {
                        var item = await _db.OutboxItems
                            .SingleOrDefaultAsync(
                                o => o.AccountId == accountId && o.Id == itemId,
                                CancellationToken.None)
                            .ConfigureAwait(false);

                        if (item is not null)
                        {
                            var blobs = await DetachOutboxAttachmentsLockedAsync(
                                    accountId,
                                    item.Id,
                                    CancellationToken.None)
                                .ConfigureAwait(false);
                            _db.OutboxItems.Remove(item);
                            await _db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
                            TryDeleteFiles(blobs);
                        }
                    }
                    finally
                    {
                        _dbGate.Release();
                    }
                }
                catch
                {
                    // Best-effort cleanup; Leaving Sending is safer than Queued/Failed.
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Connect (or pre-submit) failure: only Queued items. Do not Fail Sending —
            // that state may mean SMTP already accepted and local cleanup lagged.
            var message = MapSendFailure(ex);
            foreach (var itemId in itemIds)
            {
                await MarkQueuedOutboxItemFailedAsync(accountId, itemId, message, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            if (client is not null)
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
        }
        }
        finally
        {
            workGate.Release();
        }
    }

    public async Task RetryOutboxItemAsync(
        Guid accountId,
        Guid outboxItemId,
        CancellationToken cancellationToken = default)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var item = await _db.OutboxItems
                .SingleOrDefaultAsync(
                    o => o.AccountId == accountId && o.Id == outboxItemId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (item is null)
            {
                throw new InvalidOperationException($"Outbox item '{outboxItemId}' was not found.");
            }

            if (item.State != OutboxItemState.Failed)
            {
                throw new InvalidOperationException(
                    $"Outbox item '{outboxItemId}' cannot be retried from state '{item.State}'.");
            }

            item.State = OutboxItemState.Queued;
            item.ErrorMessage = null;
            item.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _dbGate.Release();
        }
    }

    public async Task<int> RetryFailedOutboxAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        List<Guid> ids;
        try
        {
            ids = await _db.OutboxItems
                .AsNoTracking()
                .Where(item => item.AccountId == accountId && item.State == OutboxItemState.Failed)
                .Select(item => item.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _dbGate.Release();
        }

        foreach (var id in ids)
        {
            await RetryOutboxItemAsync(accountId, id, cancellationToken).ConfigureAwait(false);
        }

        return ids.Count;
    }

    public async Task DiscardOutboxItemAsync(
        Guid accountId,
        Guid outboxItemId,
        CancellationToken cancellationToken = default)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var item = await _db.OutboxItems
                .SingleOrDefaultAsync(
                    o => o.AccountId == accountId && o.Id == outboxItemId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (item is null)
            {
                return;
            }

            var blobs = await DetachOutboxAttachmentsLockedAsync(accountId, item.Id, cancellationToken)
                .ConfigureAwait(false);
            _db.OutboxItems.Remove(item);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            TryDeleteFiles(blobs);
        }
        finally
        {
            _dbGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopForegroundSyncAsync().ConfigureAwait(false);
        await _imapSessions.DisposeAsync().ConfigureAwait(false);
        await _dbGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _db.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _dbGate.Release();
            _dbGate.Dispose();
        }
    }

    private async Task<List<string>> DetachOutboxAttachmentsLockedAsync(
        Guid accountId,
        Guid outboxItemId,
        CancellationToken cancellationToken)
    {
        var attachments = await _db.OutboxAttachments
            .Where(a => a.AccountId == accountId && a.OutboxItemId == outboxItemId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var blobs = attachments
            .Select(attachment => Path.Combine(_appDataDirectory, attachment.BlobRelativePath))
            .ToList();
        _db.OutboxAttachments.RemoveRange(attachments);
        return blobs;
    }

    private static void TryDeleteFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private async Task FailQueuedOutboxItemsAsync(
        Guid accountId,
        IReadOnlyList<Guid> itemIds,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _outbox.FailQueuedAsync(accountId, itemIds, errorMessage, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _dbGate.Release();
        }
    }

    private async Task RequeueSendingOutboxItemAsync(
        Guid accountId,
        Guid outboxItemId,
        CancellationToken cancellationToken)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _outbox.RequeueSendingAsync(accountId, outboxItemId, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _dbGate.Release();
        }
    }

    private async Task MarkQueuedOutboxItemFailedAsync(
        Guid accountId,
        Guid outboxItemId,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _outbox.MarkQueuedFailedAsync(accountId, outboxItemId, errorMessage, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _dbGate.Release();
        }
    }

    private async Task MarkOutboxItemFailedAsync(
        Guid accountId,
        Guid outboxItemId,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _outbox.MarkFailedAsync(accountId, outboxItemId, errorMessage, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _dbGate.Release();
        }
    }

    private SemaphoreSlim AccountWorkGate(Guid accountId) =>
        _accountWorkGates.GetOrAdd(accountId, static _ => new SemaphoreSlim(1, 1));

    private void SetStatus(Guid accountId, AccountStatus status)
    {
        lock (_statusGate)
        {
            _accountStatuses[accountId] = status;
        }
    }

    private const string AuthenticationFailedMessage = AccountStatus.AuthenticationFailedMessage;
    private const string SyncFailedMessage = "Could not sync this Account. Try again later.";
    private const string SendFailedMessage = "Could not send this Message. Try again later.";

    private static AccountStatus MapSyncError(Exception ex) =>
        ex is ImapAuthenticationException
            ? AccountStatus.AuthenticationFailed()
            : AccountStatus.Error(SyncFailedMessage);

    private static string MapSendFailure(Exception ex) =>
        ex is SmtpAuthenticationException
            ? AuthenticationFailedMessage
            : SendFailedMessage;

    private static string EncodeReplyReferences(string existingJson, string? internetMessageId)
    {
        var ids = PackedStringList.Decode(existingJson).ToList();
        if (!string.IsNullOrWhiteSpace(internetMessageId)
            && !ids.Contains(internetMessageId, StringComparer.OrdinalIgnoreCase))
        {
            ids.Add(internetMessageId);
        }

        return PackedStringList.Encode(ids);
    }

    private static string ForwardSubject(string subject) =>
        subject.StartsWith("Fwd:", StringComparison.OrdinalIgnoreCase)
            ? subject
            : "Fwd: " + subject;

    private static string FormatForwardedBody(
        string fromAddress,
        DateTimeOffset receivedAt,
        string subject,
        string bodyText)
    {
        var when = receivedAt.ToLocalTime().ToString(
            "yyyy-MM-dd HH:mm",
            System.Globalization.CultureInfo.InvariantCulture);
        return $"\n---------- Forwarded Message ----------\nFrom: {fromAddress}\nDate: {when}\nSubject: {subject}\n\n{bodyText}";
    }
    private static IReadOnlyList<string> DistinctAddresses(
        IEnumerable<string> addresses,
        IEnumerable<string> except)
    {
        var skip = except
            .Select(MailboxKey)
            .Where(key => key.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var address in addresses)
        {
            var key = MailboxKey(address);
            if (key.Length == 0 || skip.Contains(key) || !seen.Add(key))
            {
                continue;
            }

            result.Add(address);
        }

        return result;
    }

    private static IReadOnlyList<string> ReplyDestinations(AccountRecord account, MessageRecord message)
    {
        if (IsFromSelf(account, message))
        {
            var to = PackedStringList.Decode(message.ToAddresses);
            var withoutSelf = DistinctAddresses(to, except: [account.EmailAddress]);
            if (withoutSelf.Count > 0)
            {
                return withoutSelf;
            }

            if (to.Count > 0)
            {
                return to;
            }

            return DistinctAddresses(
                PackedStringList.Decode(message.CcAddresses),
                except: [account.EmailAddress]);
        }

        var replyTo = PackedStringList.Decode(message.ReplyToAddresses);
        return replyTo.Count > 0 ? replyTo : [message.FromAddress];
    }

    private static bool IsFromSelf(AccountRecord account, MessageRecord message)
    {
        var self = MailboxKey(account.EmailAddress);
        var from = MailboxKey(message.FromAddress);
        return self.Length > 0 && from.Equals(self, StringComparison.OrdinalIgnoreCase);
    }

    private static string MailboxKey(string? value) =>
        MailAddresses.TryGetMailbox(value, out var address, out _) ? address : (value ?? string.Empty).Trim();
    private static string ReplySubject(string subject) =>
        subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)
            ? subject
            : "Re: " + subject;

    private static string PlainBody(MessageRecord message)
    {
        if (!string.IsNullOrWhiteSpace(message.BodyText))
        {
            return message.BodyText;
        }

        return HtmlText.Strip(message.BodyHtml);
    }

    private static string QuoteForReply(string fromAddress, DateTimeOffset receivedAt, string bodyText)
    {
        var when = receivedAt.ToLocalTime().ToString(
            "yyyy-MM-dd HH:mm",
            System.Globalization.CultureInfo.InvariantCulture);
        var quoted = string.Join(
            "\n",
            bodyText.ReplaceLineEndings("\n").Split('\n').Select(line => "> " + line));
        return $"\nOn {when}, {fromAddress} wrote:\n\n{quoted}";
    }

    private static DraftInfo ToDraftInfo(DraftRecord record) =>
        new(
            record.Id,
            record.AccountId,
            PackedStringList.Decode(record.ToAddresses),
            record.Subject,
            record.BodyText,
            record.UpdatedAt)
        {
            CcAddresses = PackedStringList.Decode(record.CcAddresses),
            BccAddresses = PackedStringList.Decode(record.BccAddresses),
            InReplyTo = record.InReplyTo,
            References = PackedStringList.Decode(record.ReferencesJson),
            BodyHtml = record.BodyHtml,
        };

    private static OutboxItemInfo ToOutboxItemInfo(OutboxItemRecord record) =>
        new(
            record.Id,
            record.AccountId,
            record.State,
            record.Subject,
            record.ErrorMessage,
            record.UpdatedAt)
        {
            ToAddresses = PackedStringList.Decode(record.ToAddresses),
        };

    private void RaiseInboxArrivals(IReadOnlyList<InboxArrival> arrivals)
    {
        foreach (var arrival in arrivals)
        {
            lock (_inboxArrivalGate)
            {
                if (!_notifiedInboxMessageIds.Add(arrival.MessageId))
                {
                    continue;
                }
            }

            InboxMessageArrived?.Invoke(this, arrival);
        }
    }

    private string AccountPartitionPath(Guid accountId) =>
        Path.Combine(_appDataDirectory, "accounts", accountId.ToString("D"));

    private string BlobAreaPath(Guid accountId) =>
        Path.Combine(AccountPartitionPath(accountId), "blobs");

    private static string BlobRelativePath(Guid accountId, Guid attachmentId) =>
        Path.Combine("accounts", accountId.ToString("D"), "blobs", attachmentId.ToString("D"));

    public async Task<AccountInfo?> SetAccountSignatureAsync(
        Guid accountId,
        string? signature,
        CancellationToken cancellationToken = default)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var record = await _db.Accounts
                .SingleOrDefaultAsync(a => a.Id == accountId, cancellationToken)
                .ConfigureAwait(false);
            if (record is null)
            {
                return null;
            }

            record.Signature = string.IsNullOrWhiteSpace(signature) ? null : signature.TrimEnd();
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return ToInfo(record);
        }
        finally
        {
            _dbGate.Release();
        }
    }

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
            record.CredentialHandle,
            record.OAuthProvider,
            record.OAuthAuthority)
        {
            Signature = record.Signature,
        };

    private static OAuthTokenMetadata RequireOAuthMetadata(AccountRecord account)
    {
        if (account.OAuthProvider is null
            || string.IsNullOrWhiteSpace(account.OAuthAuthority)
            || string.IsNullOrWhiteSpace(account.OAuthClientId))
        {
            throw new InvalidOperationException(
                $"Account '{account.Id}' is missing OAuth metadata.");
        }

        return new OAuthTokenMetadata(
            account.OAuthProvider.Value,
            account.OAuthAuthority,
            account.OAuthClientId);
    }

    /// <summary>
    /// Password Accounts return the stored secret; OAuth Accounts return a short-lived
    /// access token from Auth. Null means the OAuth Credential was invalidated.
    /// </summary>
    private async Task<string?> ResolveProtocolSecretAsync(
        CredentialKind credentialKind,
        OAuthTokenMetadata? oauthMetadata,
        string credentialSecret,
        string credentialHandle,
        bool invalidateOnAuthFailure,
        CancellationToken cancellationToken)
    {
        if (credentialKind != CredentialKind.OAuth)
        {
            return credentialSecret;
        }

        ArgumentNullException.ThrowIfNull(oauthMetadata);

        var accessToken = await _auth
            .GetAccessTokenAsync(
                oauthMetadata,
                credentialHandle,
                invalidateOnAuthFailure,
                cancellationToken)
            .ConfigureAwait(false);

        return accessToken;
    }

    private async Task<HashSet<Guid>> AttachmentMessageIdsAsync(
        IReadOnlyCollection<MessageRecord> records,
        CancellationToken cancellationToken)
    {
        if (records.Count == 0)
        {
            return [];
        }

        var messageIds = records.Select(record => record.Id).ToList();
        var matches = await _db.Attachments
            .AsNoTracking()
            .Where(attachment => messageIds.Contains(attachment.MessageId))
            .Select(attachment => attachment.MessageId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return matches.ToHashSet();
    }

    private static MessageInfo ToMessageInfo(MessageRecord record) =>
        ToMessageInfo(record, attachmentMessageIds: null);

    private static MessageInfo ToMessageInfo(
        MessageRecord record,
        HashSet<Guid>? attachmentMessageIds) =>
        new(
            record.Id,
            record.AccountId,
            record.MailboxId,
            record.RemoteId,
            record.Subject,
            record.FromAddress,
            record.ReceivedAt,
            record.IsRead,
            record.IsFlagged)
        {
            Preview = MessagePreview.FromBodyText(
                string.IsNullOrWhiteSpace(record.BodyText)
                    ? HtmlText.Strip(record.BodyHtml)
                    : record.BodyText),
            ToAddresses = PackedStringList.Decode(record.ToAddresses),
            CcAddresses = PackedStringList.Decode(record.CcAddresses),
            BccAddresses = PackedStringList.Decode(record.BccAddresses),
            ReplyToAddresses = PackedStringList.Decode(record.ReplyToAddresses),
            HasAttachments = attachmentMessageIds?.Contains(record.Id) == true,
            SizeBytes = record.SizeBytes,
            InternetMessageId = record.InternetMessageId,
        };

}
