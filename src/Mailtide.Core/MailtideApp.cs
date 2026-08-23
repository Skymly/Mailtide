using System.Collections.Concurrent;
using System.Text.Json;
using Mailtide.Core.Auth;
using Mailtide.Core.Imap;
using Mailtide.Core.Security;
using Mailtide.Core.Smtp;
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
    private readonly AccountCredentialAuth _auth;
    private readonly IImapClientFactory _imapClientFactory;
    private readonly ISmtpClientFactory _smtpClientFactory;
    private readonly MailtideDbContext _db;
    private readonly Dictionary<Guid, AccountStatus> _accountStatuses = new();
    private readonly object _statusGate = new();
    // DbContext is not thread-safe; serialize all store access on this single-user desktop app.
    private readonly SemaphoreSlim _dbGate = new(1, 1);
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _accountWorkGates = new();
    private readonly object _foregroundGate = new();
    public event EventHandler<Guid>? AccountWorkCompleted;
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
        await EnsureStoreSchemaAsync(db, cancellationToken).ConfigureAwait(false);

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
            var outboxItems = await _db.OutboxItems
                .Where(o => o.AccountId == accountId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            _db.Attachments.RemoveRange(attachments);
            _db.Messages.RemoveRange(messages);
            _db.Mailboxes.RemoveRange(mailboxes);
            _db.Drafts.RemoveRange(drafts);
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

    private async Task WaitForInboxChangeAsync(
        Guid accountId,
        string mailboxPath,
        CancellationToken cancellationToken)
    {
        string imapHost;
        int imapPort;
        string emailAddress;
        string credentialHandle;
        CredentialKind credentialKind;
        OAuthTokenMetadata? oauthMetadata = null;
        string? secret;

        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var account = await _db.Accounts
                .AsNoTracking()
                .SingleOrDefaultAsync(a => a.Id == accountId, cancellationToken)
                .ConfigureAwait(false);
            if (account is null)
            {
                throw new InvalidOperationException($"Account '{accountId}' was not found.");
            }

            imapHost = account.ImapHost;
            imapPort = account.ImapPort;
            emailAddress = account.EmailAddress;
            credentialHandle = account.CredentialHandle;
            credentialKind = account.CredentialKind;
            if (account.CredentialKind == CredentialKind.OAuth)
            {
                oauthMetadata = RequireOAuthMetadata(account);
            }

            secret = await _auth
                .RetrieveCredentialSecretAsync(account.CredentialHandle, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _dbGate.Release();
        }

        if (secret is null)
        {
            throw new InvalidOperationException(AuthenticationFailedMessage);
        }

        var protocolSecret = await ResolveProtocolSecretAsync(
                credentialKind,
                oauthMetadata,
                secret,
                credentialHandle,
                invalidateOnAuthFailure: false,
                cancellationToken)
            .ConfigureAwait(false);
        if (protocolSecret is null)
        {
            throw new InvalidOperationException(AuthenticationFailedMessage);
        }

        await using var client = _imapClientFactory.Create();
        await client
            .ConnectAndAuthenticateAsync(imapHost, imapPort, emailAddress, protocolSecret, cancellationToken)
            .ConfigureAwait(false);
        await client.WaitForMailboxChangeAsync(mailboxPath, cancellationToken).ConfigureAwait(false);
    }

    public async Task SyncNowAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var workGate = AccountWorkGate(accountId);
        await workGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
        string imapHost;
        int imapPort;
        string emailAddress;
        string credentialHandle;
        CredentialKind credentialKind;
        OAuthTokenMetadata? oauthMetadata = null;
        string? secret;

        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var account = await _db.Accounts
                .AsNoTracking()
                .SingleOrDefaultAsync(a => a.Id == accountId, cancellationToken)
                .ConfigureAwait(false);

            if (account is null)
            {
                throw new InvalidOperationException($"Account '{accountId}' was not found.");
            }

            imapHost = account.ImapHost;
            imapPort = account.ImapPort;
            emailAddress = account.EmailAddress;
            credentialHandle = account.CredentialHandle;
            credentialKind = account.CredentialKind;
            if (account.CredentialKind == CredentialKind.OAuth)
            {
                oauthMetadata = RequireOAuthMetadata(account);
            }

            secret = await _auth
                .RetrieveCredentialSecretAsync(account.CredentialHandle, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _dbGate.Release();
        }

        if (secret is null)
        {
            SetStatus(accountId, AccountStatus.Error(AuthenticationFailedMessage));
            return;
        }

        string? protocolSecret;
        try
        {
            protocolSecret = await ResolveProtocolSecretAsync(
                    credentialKind,
                    oauthMetadata,
                    secret,
                    credentialHandle,
                    invalidateOnAuthFailure: true,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetStatus(accountId, AccountStatus.Error(MapSyncFailure(ex)));
            return;
        }

        if (protocolSecret is null)
        {
            SetStatus(accountId, AccountStatus.Error(AuthenticationFailedMessage));
            return;
        }

        // Network I/O runs outside _dbGate so other Accounts can sync in parallel.
        SetStatus(accountId, AccountStatus.Syncing());

        try
        {
            await using var client = _imapClientFactory.Create();
            await client
                .ConnectAndAuthenticateAsync(
                    imapHost,
                    imapPort,
                    emailAddress,
                    protocolSecret,
                    cancellationToken)
                .ConfigureAwait(false);

            IReadOnlyDictionary<string, HashSet<string>> knownRemoteIds;
            await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                knownRemoteIds = await LoadKnownRemoteIdsByPathAsync(accountId, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _dbGate.Release();
            }

            var snapshot = await FetchRemoteSnapshotAsync(client, knownRemoteIds, cancellationToken)
                .ConfigureAwait(false);

            await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Account may have been removed while IMAP ran outside _dbGate.
                var stillPresent = await _db.Accounts
                    .AsNoTracking()
                    .AnyAsync(a => a.Id == accountId, cancellationToken)
                    .ConfigureAwait(false);

                if (!stillPresent)
                {
                    lock (_statusGate)
                    {
                        _accountStatuses.Remove(accountId);
                    }

                    return;
                }

                await PersistSnapshotAsync(accountId, snapshot, cancellationToken)
                    .ConfigureAwait(false);

                SetStatus(accountId, AccountStatus.Idle());
            }
            finally
            {
                _dbGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            await ClearTrackerAsync(CancellationToken.None).ConfigureAwait(false);
            SetStatus(accountId, AccountStatus.Idle());
            throw;
        }
        catch (Exception ex)
        {
            await ClearTrackerAsync(CancellationToken.None).ConfigureAwait(false);
            SetStatus(accountId, AccountStatus.Error(MapSyncFailure(ex)));
        }
        }
        finally
        {
            workGate.Release();
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

            return records
                .OrderByDescending(m => m.ReceivedAt)
                .ThenBy(m => m.Subject)
                .Select(ToMessageInfo)
                .ToList();
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

            return records
                .OrderByDescending(m => m.ReceivedAt)
                .ThenBy(m => m.Subject)
                .Select(ToMessageInfo)
                .ToList();
        }
        finally
        {
            _dbGate.Release();
        }
    }

    public Task<IReadOnlyList<MessageInfo>> SearchMessagesAsync(
        Guid accountId,
        Guid mailboxId,
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return ListMessagesAsync(accountId, mailboxId, cancellationToken);
        }

        return SearchMailboxAsync(accountId, mailboxId, query, cancellationToken);
    }

    public Task<IReadOnlyList<MessageInfo>> SearchUnifiedInboxAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return ListUnifiedInboxAsync(cancellationToken);
        }

        return SearchUnifiedAsync(query, cancellationToken);
    }

    private async Task<IReadOnlyList<MessageInfo>> SearchMailboxAsync(
        Guid accountId,
        Guid mailboxId,
        string query,
        CancellationToken cancellationToken)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = await _db.Messages
                .AsNoTracking()
                .Where(m => m.AccountId == accountId && m.MailboxId == mailboxId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return FilterMessages(records, query);
        }
        finally
        {
            _dbGate.Release();
        }
    }

    private async Task<IReadOnlyList<MessageInfo>> SearchUnifiedAsync(
        string query,
        CancellationToken cancellationToken)
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

            return FilterMessages(records, query);
        }
        finally
        {
            _dbGate.Release();
        }
    }

    private static IReadOnlyList<MessageInfo> FilterMessages(
        IReadOnlyList<MessageRecord> records,
        string query)
    {
        return records
            .Where(record => MessageMatches(record, query))
            .OrderByDescending(m => m.ReceivedAt)
            .ThenBy(m => m.Subject)
            .Select(ToMessageInfo)
            .ToList();
    }

    private static bool MessageMatches(MessageRecord record, string query) =>
        ContainsIgnoreCase(record.Subject, query)
        || ContainsIgnoreCase(record.FromAddress, query)
        || ContainsIgnoreCase(record.BodyText, query)
        || ContainsIgnoreCase(record.BodyHtml, query);

    private static bool ContainsIgnoreCase(string? value, string query) =>
        !string.IsNullOrEmpty(value)
        && value.Contains(query, StringComparison.OrdinalIgnoreCase);

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

            return record?.BodyText;
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

    public async Task MarkReadAsync(
        Guid accountId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        string imapHost;
        int imapPort;
        string emailAddress;
        string mailboxPath;
        string remoteId;
        string credentialHandle;
        CredentialKind credentialKind;
        OAuthTokenMetadata? oauthMetadata = null;
        string? secret;

        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var message = await _db.Messages
                .SingleOrDefaultAsync(
                    m => m.AccountId == accountId && m.Id == messageId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (message is null)
            {
                throw new InvalidOperationException("Message '{messageId}' was not found.");
            }

            if (message.IsRead)
            {
                return;
            }

            var mailbox = await _db.Mailboxes
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    m => m.AccountId == accountId && m.Id == message.MailboxId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (mailbox is null)
            {
                throw new InvalidOperationException("Mailbox '{message.MailboxId}' was not found.");
            }

            var account = await _db.Accounts
                .AsNoTracking()
                .SingleOrDefaultAsync(a => a.Id == accountId, cancellationToken)
                .ConfigureAwait(false);

            if (account is null)
            {
                throw new InvalidOperationException("Account '{accountId}' was not found.");
            }

            message.IsRead = true;
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            imapHost = account.ImapHost;
            imapPort = account.ImapPort;
            emailAddress = account.EmailAddress;
            mailboxPath = mailbox.Path;
            remoteId = message.RemoteId;
            credentialHandle = account.CredentialHandle;
            credentialKind = account.CredentialKind;
            if (account.CredentialKind == CredentialKind.OAuth)
            {
                oauthMetadata = RequireOAuthMetadata(account);
            }

            secret = await _auth
                .RetrieveCredentialSecretAsync(account.CredentialHandle, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _dbGate.Release();
        }

        if (secret is null)
        {
            SetStatus(accountId, AccountStatus.Error(AuthenticationFailedMessage));
            return;
        }

        try
        {
            var protocolSecret = await ResolveProtocolSecretAsync(
                    credentialKind,
                    oauthMetadata,
                    secret,
                    credentialHandle,
                    invalidateOnAuthFailure: true,
                    cancellationToken)
                .ConfigureAwait(false);

            if (protocolSecret is null)
            {
                SetStatus(accountId, AccountStatus.Error(AuthenticationFailedMessage));
                return;
            }

            await using var client = _imapClientFactory.Create();
            await client
                .ConnectAndAuthenticateAsync(
                    imapHost,
                    imapPort,
                    emailAddress,
                    protocolSecret,
                    cancellationToken)
                .ConfigureAwait(false);
            await client
                .SetSeenAsync(mailboxPath, remoteId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            SetStatus(accountId, AccountStatus.Error(MapSyncFailure(ex)));
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
                    a.ContentType))
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


    public async Task<DraftInfo> StartForwardAsync(
        Guid accountId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var accountExists = await _db.Accounts
                .AsNoTracking()
                .AnyAsync(a => a.Id == accountId, cancellationToken)
                .ConfigureAwait(false);

            if (!accountExists)
            {
                throw new InvalidOperationException($"Account '{accountId}' was not found.");
            }

            var message = await _db.Messages
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    m => m.AccountId == accountId && m.Id == messageId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (message is null)
            {
                throw new InvalidOperationException($"Message '{messageId}' was not found.");
            }

            var now = DateTimeOffset.UtcNow;
            var record = new DraftRecord
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                ToAddresses = EncodeAddresses([]),
                Subject = ForwardSubject(message.Subject),
                BodyText = FormatForwardedBody(message.FromAddress, message.ReceivedAt, message.Subject, message.BodyText),
                InReplyTo = message.InternetMessageId,
                ReferencesJson = EncodeReplyReferences(message.ReferencesJson, message.InternetMessageId),
                UpdatedAt = now,
            };

            _db.Drafts.Add(record);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return ToDraftInfo(record);
        }
        finally
        {
            _dbGate.Release();
        }
    }
    public async Task<DraftInfo> StartReplyAllAsync(
        Guid accountId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var account = await _db.Accounts
                .AsNoTracking()
                .SingleOrDefaultAsync(a => a.Id == accountId, cancellationToken)
                .ConfigureAwait(false);
            if (account is null)
            {
                throw new InvalidOperationException($"Account '{accountId}' was not found.");
            }

            var message = await _db.Messages
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    m => m.AccountId == accountId && m.Id == messageId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (message is null)
            {
                throw new InvalidOperationException($"Message '{messageId}' was not found.");
            }

            var self = account.EmailAddress;
            var to = DistinctAddresses(
                [message.FromAddress, ..DecodeAddresses(message.ToAddresses)],
                except: [self]);
            var cc = DistinctAddresses(
                DecodeAddresses(message.CcAddresses),
                except: [self, ..to]);

            var record = new DraftRecord
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                ToAddresses = EncodeAddresses(to),
                CcAddresses = EncodeAddresses(cc),
                Subject = ReplySubject(message.Subject),
                BodyText = QuoteForReply(message.FromAddress, message.ReceivedAt, message.BodyText),
                InReplyTo = message.InternetMessageId,
                ReferencesJson = EncodeReplyReferences(message.ReferencesJson, message.InternetMessageId),
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            _db.Drafts.Add(record);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return ToDraftInfo(record);
        }
        finally
        {
            _dbGate.Release();
        }
    }
    public async Task<DraftInfo> StartReplyAsync(
        Guid accountId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var accountExists = await _db.Accounts
                .AsNoTracking()
                .AnyAsync(a => a.Id == accountId, cancellationToken)
                .ConfigureAwait(false);

            if (!accountExists)
            {
                throw new InvalidOperationException($"Account '{accountId}' was not found.");
            }

            var message = await _db.Messages
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    m => m.AccountId == accountId && m.Id == messageId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (message is null)
            {
                throw new InvalidOperationException($"Message '{messageId}' was not found.");
            }

            var now = DateTimeOffset.UtcNow;
            var record = new DraftRecord
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                ToAddresses = EncodeAddresses([message.FromAddress]),
                Subject = ReplySubject(message.Subject),
                BodyText = QuoteForReply(message.FromAddress, message.ReceivedAt, message.BodyText),
                InReplyTo = message.InternetMessageId,
                ReferencesJson = EncodeReplyReferences(message.ReferencesJson, message.InternetMessageId),
                UpdatedAt = now,
            };

            _db.Drafts.Add(record);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return ToDraftInfo(record);

        }
        finally
        {
            _dbGate.Release();
        }
    }

    public async Task<DraftInfo> SaveDraftAsync(
        Guid accountId,
        DraftContent content,
        Guid? draftId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(content.ToAddresses);

        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var accountExists = await _db.Accounts
                .AsNoTracking()
                .AnyAsync(a => a.Id == accountId, cancellationToken)
                .ConfigureAwait(false);

            if (!accountExists)
            {
                throw new InvalidOperationException($"Account '{accountId}' was not found.");
            }

            var now = DateTimeOffset.UtcNow;
            if (draftId is { } existingId)
            {
                var existing = await _db.Drafts
                    .SingleOrDefaultAsync(
                        d => d.AccountId == accountId && d.Id == existingId,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (existing is null)
                {
                    throw new InvalidOperationException($"Draft '{existingId}' was not found.");
                }

                existing.ToAddresses = EncodeAddresses(content.ToAddresses);
                existing.CcAddresses = EncodeAddresses(content.CcAddresses);
                existing.Subject = content.Subject;
                existing.BodyText = content.BodyText;
                existing.UpdatedAt = now;
                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return ToDraftInfo(existing);
            }

            var record = new DraftRecord
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                ToAddresses = EncodeAddresses(content.ToAddresses),
                CcAddresses = EncodeAddresses(content.CcAddresses),
                Subject = content.Subject,
                BodyText = content.BodyText,
                UpdatedAt = now,
            };

            _db.Drafts.Add(record);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return ToDraftInfo(record);

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

            _db.Drafts.Remove(draft);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _dbGate.Release();
        }
    }

    public async Task SendAsync(
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
                throw new InvalidOperationException($"Draft '{draftId}' was not found.");
            }

            var now = DateTimeOffset.UtcNow;
            _db.OutboxItems.Add(new OutboxItemRecord
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                ToAddresses = draft.ToAddresses,
                CcAddresses = draft.CcAddresses,
                Subject = draft.Subject,
                BodyText = draft.BodyText,
                InReplyTo = draft.InReplyTo,
                ReferencesJson = draft.ReferencesJson,
                State = OutboxItemState.Queued,
                ErrorMessage = null,
                UpdatedAt = now,
            });
            _db.Drafts.Remove(draft);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _dbGate.Release();
        }
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

                    outbound = new OutboundMessage(
                        account.EmailAddress,
                        DecodeAddresses(item.ToAddresses),
                        item.Subject,
                        item.BodyText)
                    {
                        CcAddresses = DecodeAddresses(item.CcAddresses),
                        InReplyTo = item.InReplyTo,
                        References = DecodeAddresses(item.ReferencesJson),
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
                            _db.OutboxItems.Remove(item);
                            await _db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
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

            _db.OutboxItems.Remove(item);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _dbGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopForegroundSyncAsync().ConfigureAwait(false);
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

    /// <summary>
    /// EnsureCreated only creates a missing database; it does not add tables to an existing file.
    /// Create any model tables that may be absent after upgrading from an Accounts-only schema.
    /// </summary>
    private static async Task TryAddThreadingColumnsAsync(
        MailtideDbContext db,
        CancellationToken cancellationToken)
    {
        foreach (var sql in new[]
                 {
                     "ALTER TABLE Messages ADD COLUMN InternetMessageId TEXT",
                     "ALTER TABLE Messages ADD COLUMN ReferencesJson TEXT NOT NULL DEFAULT '[]'",
                     "ALTER TABLE Drafts ADD COLUMN InReplyTo TEXT",
                     "ALTER TABLE Drafts ADD COLUMN ReferencesJson TEXT NOT NULL DEFAULT '[]'",
                     "ALTER TABLE OutboxItems ADD COLUMN InReplyTo TEXT",
                     "ALTER TABLE OutboxItems ADD COLUMN ReferencesJson TEXT NOT NULL DEFAULT '[]'",
                 })
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(sql, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Column already exists on upgraded stores.
            }
        }
    }

    private static async Task TryAddDraftOutboxCcColumnsAsync(
        MailtideDbContext db,
        CancellationToken cancellationToken)
    {
        foreach (var sql in new[]
                 {
                     "ALTER TABLE Drafts ADD COLUMN CcAddresses TEXT NOT NULL DEFAULT '[]'",
                     "ALTER TABLE OutboxItems ADD COLUMN CcAddresses TEXT NOT NULL DEFAULT '[]'",
                 })
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(sql, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Column already exists on upgraded stores.
            }
        }
    }

    private static async Task TryAddMessageRecipientColumnsAsync(
        MailtideDbContext db,
        CancellationToken cancellationToken)
    {
        foreach (var sql in new[]
                 {
                     "ALTER TABLE Messages ADD COLUMN ToAddresses TEXT NOT NULL DEFAULT '[]'",
                     "ALTER TABLE Messages ADD COLUMN CcAddresses TEXT NOT NULL DEFAULT '[]'",
                     "ALTER TABLE Messages ADD COLUMN BodyHtml TEXT",
                 })
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(sql, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Column already exists on upgraded stores.
            }
        }
    }
    private static async Task EnsureStoreSchemaAsync(
        MailtideDbContext db,
        CancellationToken cancellationToken)
    {
        await db.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync(
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

        await db.Database.ExecuteSqlRawAsync(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Mailboxes_AccountId_Path"
                ON "Mailboxes" ("AccountId", "Path")
                """,
                cancellationToken)
            .ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync(
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

        await db.Database.ExecuteSqlRawAsync(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Messages_AccountId_MailboxId_RemoteId"
                ON "Messages" ("AccountId", "MailboxId", "RemoteId")
                """,
                cancellationToken)
            .ConfigureAwait(false);

        await TryAddMessageRecipientColumnsAsync(db, cancellationToken).ConfigureAwait(false);
        await TryAddDraftOutboxCcColumnsAsync(db, cancellationToken).ConfigureAwait(false);
        await TryAddThreadingColumnsAsync(db, cancellationToken).ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync(
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

        await db.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_Attachments_AccountId_MessageId"
                ON "Attachments" ("AccountId", "MessageId")
                """,
                cancellationToken)
            .ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync(
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

        await db.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_Drafts_AccountId"
                ON "Drafts" ("AccountId")
                """,
                cancellationToken)
            .ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync(
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

        await db.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_OutboxItems_AccountId"
                ON "OutboxItems" ("AccountId")
                """,
                cancellationToken)
            .ConfigureAwait(false);

        await EnsureAccountsOAuthColumnsAsync(db, cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureAccountsOAuthColumnsAsync(
        MailtideDbContext db,
        CancellationToken cancellationToken)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM pragma_table_info('Accounts')";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                existing.Add(reader.GetString(0));
            }
        }
        finally
        {
            if (shouldClose)
            {
                await db.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }

        if (!existing.Contains("OAuthProvider"))
        {
            await db.Database.ExecuteSqlRawAsync(
                    """ALTER TABLE "Accounts" ADD COLUMN "OAuthProvider" TEXT NULL""",
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (!existing.Contains("OAuthAuthority"))
        {
            await db.Database.ExecuteSqlRawAsync(
                    """ALTER TABLE "Accounts" ADD COLUMN "OAuthAuthority" TEXT NULL""",
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (!existing.Contains("OAuthClientId"))
        {
            await db.Database.ExecuteSqlRawAsync(
                    """ALTER TABLE "Accounts" ADD COLUMN "OAuthClientId" TEXT NULL""",
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task FailQueuedOutboxItemsAsync(
        Guid accountId,
        IReadOnlyList<Guid> itemIds,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        foreach (var itemId in itemIds)
        {
            await MarkQueuedOutboxItemFailedAsync(accountId, itemId, errorMessage, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyDictionary<string, HashSet<string>>> LoadKnownRemoteIdsByPathAsync(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var mailboxes = await _db.Mailboxes
            .AsNoTracking()
            .Where(m => m.AccountId == accountId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var messages = await _db.Messages
            .AsNoTracking()
            .Where(m => m.AccountId == accountId)
            .Select(m => new { m.MailboxId, m.RemoteId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byId = mailboxes.ToDictionary(m => m.Id, m => m.Path);
        var known = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var mailbox in mailboxes)
        {
            known[mailbox.Path] = new HashSet<string>(StringComparer.Ordinal);
        }

        foreach (var message in messages)
        {
            if (byId.TryGetValue(message.MailboxId, out var path))
            {
                known[path].Add(message.RemoteId);
            }
        }

        return known;
    }

    private static async Task<IReadOnlyList<RemoteMailboxSnapshot>> FetchRemoteSnapshotAsync(
        IImapClient client,
        IReadOnlyDictionary<string, HashSet<string>> knownRemoteIds,
        CancellationToken cancellationToken)
    {
        var remoteMailboxes = await client
            .ListMailboxesAsync(cancellationToken)
            .ConfigureAwait(false);

        var snapshot = new List<RemoteMailboxSnapshot>(remoteMailboxes.Count);
        foreach (var mailbox in remoteMailboxes)
        {
            var summaries = await client
                .FetchMessageSummariesAsync(mailbox.Path, cancellationToken)
                .ConfigureAwait(false);
            knownRemoteIds.TryGetValue(mailbox.Path, out var known);
            var missing = summaries
                .Select(s => s.RemoteId)
                .Where(id => known is null || !known.Contains(id))
                .ToList();
            var fetched = missing.Count == 0
                ? Array.Empty<RemoteMessage>()
                : await client
                    .FetchMessagesAsync(mailbox.Path, missing, cancellationToken)
                    .ConfigureAwait(false);
            snapshot.Add(new RemoteMailboxSnapshot(
                mailbox,
                summaries,
                fetched.ToDictionary(m => m.RemoteId, StringComparer.Ordinal)));
        }

        return snapshot;
    }
    private async Task PersistSnapshotAsync(
        Guid accountId,
        IReadOnlyList<RemoteMailboxSnapshot> snapshot,
        CancellationToken cancellationToken)
    {
        var existingMailboxes = await _db.Mailboxes
            .Where(m => m.AccountId == accountId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var mailboxByPath = existingMailboxes.ToDictionary(m => m.Path, StringComparer.Ordinal);

        var existingMessages = await _db.Messages
            .Where(m => m.AccountId == accountId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var existingAttachments = await _db.Attachments
            .Where(a => a.AccountId == accountId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var seenMailboxIds = new HashSet<Guid>();
        var seenMessageIds = new HashSet<Guid>();

        foreach (var entry in snapshot)
        {
            if (!mailboxByPath.TryGetValue(entry.Mailbox.Path, out var mailbox))
            {
                mailbox = new MailboxRecord
                {
                    Id = Guid.NewGuid(),
                    AccountId = accountId,
                    Name = entry.Mailbox.Name,
                    Path = entry.Mailbox.Path,
                    Role = entry.Mailbox.Role,
                };
                _db.Mailboxes.Add(mailbox);
                mailboxByPath[entry.Mailbox.Path] = mailbox;
            }
            else
            {
                mailbox.Name = entry.Mailbox.Name;
                mailbox.Role = entry.Mailbox.Role;
            }

            seenMailboxIds.Add(mailbox.Id);

            var messagesByRemote = existingMessages
                .Where(m => m.MailboxId == mailbox.Id)
                .ToDictionary(m => m.RemoteId, StringComparer.Ordinal);

            foreach (var summary in entry.Summaries)
            {
                entry.FetchedByRemoteId.TryGetValue(summary.RemoteId, out var fetched);
                if (!messagesByRemote.TryGetValue(summary.RemoteId, out var message))
                {
                    if (fetched is null)
                    {
                        continue;
                    }

                    var messageId = Guid.NewGuid();
                    message = new MessageRecord
                    {
                        Id = messageId,
                        AccountId = accountId,
                        MailboxId = mailbox.Id,
                        RemoteId = fetched.RemoteId,
                        Subject = fetched.Subject,
                        FromAddress = fetched.FromAddress,
                        ReceivedAt = fetched.ReceivedAt,
                        IsRead = summary.IsRead,
                        BodyText = fetched.BodyText,
                        BodyHtml = fetched.BodyHtml,
                        InternetMessageId = fetched.InternetMessageId,
                        ReferencesJson = EncodeAddresses(fetched.References),
                        ToAddresses = EncodeAddresses(fetched.ToAddresses),
                        CcAddresses = EncodeAddresses(fetched.CcAddresses),
                    };
                    _db.Messages.Add(message);

                    foreach (var remoteAttachment in fetched.Attachments)
                    {
                        var attachmentId = Guid.NewGuid();
                        var blobRelativePath = BlobRelativePath(accountId, attachmentId);
                        var blobAbsolutePath = Path.Combine(_appDataDirectory, blobRelativePath);
                        Directory.CreateDirectory(Path.GetDirectoryName(blobAbsolutePath)!);
                        await File
                            .WriteAllBytesAsync(blobAbsolutePath, remoteAttachment.Content, cancellationToken)
                            .ConfigureAwait(false);

                        _db.Attachments.Add(new AttachmentRecord
                        {
                            Id = attachmentId,
                            AccountId = accountId,
                            MessageId = messageId,
                            FileName = remoteAttachment.FileName,
                            ContentType = remoteAttachment.ContentType,
                            BlobRelativePath = blobRelativePath,
                        });
                    }
                }
                else
                {
                    message.IsRead = summary.IsRead;
                    message.Subject = summary.Subject;
                    message.FromAddress = summary.FromAddress;
                    message.ReceivedAt = summary.ReceivedAt;
                    if (fetched is not null)
                    {
                        message.BodyText = fetched.BodyText;
                        message.BodyHtml = fetched.BodyHtml;
                        message.InternetMessageId = fetched.InternetMessageId;
                        message.ReferencesJson = EncodeAddresses(fetched.References);
                        message.ToAddresses = EncodeAddresses(fetched.ToAddresses);
                        message.CcAddresses = EncodeAddresses(fetched.CcAddresses);
                    }
                }

                seenMessageIds.Add(message.Id);
            }
        }

        var messagesToRemove = existingMessages
            .Where(m => !seenMessageIds.Contains(m.Id))
            .ToList();
        var removedMessageIds = messagesToRemove.Select(m => m.Id).ToHashSet();
        var attachmentsToRemove = existingAttachments
            .Where(a => removedMessageIds.Contains(a.MessageId))
            .ToList();

        foreach (var attachment in attachmentsToRemove)
        {
            var blobAbsolutePath = Path.Combine(_appDataDirectory, attachment.BlobRelativePath);
            if (File.Exists(blobAbsolutePath))
            {
                File.Delete(blobAbsolutePath);
            }
        }

        _db.Attachments.RemoveRange(attachmentsToRemove);
        _db.Messages.RemoveRange(messagesToRemove);
        _db.Mailboxes.RemoveRange(existingMailboxes.Where(m => !seenMailboxIds.Contains(m.Id)));

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
    private void ResetBlobArea(Guid accountId)
    {
        var blobsDirectory = BlobAreaPath(accountId);
        if (Directory.Exists(blobsDirectory))
        {
            Directory.Delete(blobsDirectory, recursive: true);
        }

        Directory.CreateDirectory(blobsDirectory);
    }

    private async Task RequeueSendingOutboxItemAsync(
        Guid accountId,
        Guid outboxItemId,
        CancellationToken cancellationToken)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var item = await _db.OutboxItems
                .SingleOrDefaultAsync(
                    o => o.AccountId == accountId && o.Id == outboxItemId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (item is null || item.State != OutboxItemState.Sending)
            {
                return;
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

    private async Task MarkQueuedOutboxItemFailedAsync(
        Guid accountId,
        Guid outboxItemId,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var item = await _db.OutboxItems
                .SingleOrDefaultAsync(
                    o => o.AccountId == accountId && o.Id == outboxItemId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (item is null || item.State != OutboxItemState.Queued)
            {
                return;
            }

            item.State = OutboxItemState.Failed;
            item.ErrorMessage = errorMessage;
            item.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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
            var item = await _db.OutboxItems
                .SingleOrDefaultAsync(
                    o => o.AccountId == accountId && o.Id == outboxItemId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (item is null)
            {
                return;
            }

            if (item.State is not (OutboxItemState.Queued or OutboxItemState.Sending))
            {
                return;
            }

            item.State = OutboxItemState.Failed;
            item.ErrorMessage = errorMessage;
            item.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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

    private const string AuthenticationFailedMessage = "Authentication failed. Sign in again.";
    private const string SyncFailedMessage = "Could not sync this Account. Try again later.";
    private const string SendFailedMessage = "Could not send this Message. Try again later.";

    private static string MapSyncFailure(Exception ex) =>
        ex is ImapAuthenticationException
            ? AuthenticationFailedMessage
            : SyncFailedMessage;

    private static string MapSendFailure(Exception ex) =>
        ex is SmtpAuthenticationException
            ? AuthenticationFailedMessage
            : SendFailedMessage;

    private static string EncodeReplyReferences(string existingJson, string? internetMessageId)
    {
        var ids = DecodeAddresses(existingJson).ToList();
        if (!string.IsNullOrWhiteSpace(internetMessageId)
            && !ids.Contains(internetMessageId, StringComparer.OrdinalIgnoreCase))
        {
            ids.Add(internetMessageId);
        }

        return EncodeAddresses(ids);
    }

    private static string EncodeAddresses(IReadOnlyList<string> addresses) =>
        JsonSerializer.Serialize(addresses);

    private static IReadOnlyList<string> DecodeAddresses(string encoded) =>
        JsonSerializer.Deserialize<string[]>(encoded) ?? [];


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
        var when = receivedAt.UtcDateTime.ToString(
            "yyyy-MM-dd HH:mm",
            System.Globalization.CultureInfo.InvariantCulture);
        return $"\n---------- Forwarded Message ----------\nFrom: {fromAddress}\nDate: {when} UTC\nSubject: {subject}\n\n{bodyText}";
    }
    private static IReadOnlyList<string> DistinctAddresses(
        IEnumerable<string> addresses,
        IEnumerable<string> except)
    {
        var skip = except
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var address in addresses)
        {
            if (string.IsNullOrWhiteSpace(address) || skip.Contains(address) || result.Contains(address, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(address);
        }

        return result;
    }
    private static string ReplySubject(string subject) =>
        subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)
            ? subject
            : "Re: " + subject;

    private static string QuoteForReply(string fromAddress, DateTimeOffset receivedAt, string bodyText)
    {
        var when = receivedAt.UtcDateTime.ToString(
            "yyyy-MM-dd HH:mm",
            System.Globalization.CultureInfo.InvariantCulture);
        var quoted = string.Join(
            "\n",
            bodyText.ReplaceLineEndings("\n").Split('\n').Select(line => "> " + line));
        return $"\nOn {when} UTC, {fromAddress} wrote:\n\n{quoted}";
    }

    private static DraftInfo ToDraftInfo(DraftRecord record) =>
        new(
            record.Id,
            record.AccountId,
            DecodeAddresses(record.ToAddresses),
            record.Subject,
            record.BodyText,
            record.UpdatedAt)
        {
            CcAddresses = DecodeAddresses(record.CcAddresses),
            InReplyTo = record.InReplyTo,
            References = DecodeAddresses(record.ReferencesJson),
        };

    private static OutboxItemInfo ToOutboxItemInfo(OutboxItemRecord record) =>
        new(
            record.Id,
            record.AccountId,
            record.State,
            record.Subject,
            record.ErrorMessage,
            record.UpdatedAt);

    private string AccountPartitionPath(Guid accountId) =>
        Path.Combine(_appDataDirectory, "accounts", accountId.ToString("D"));

    private string BlobAreaPath(Guid accountId) =>
        Path.Combine(AccountPartitionPath(accountId), "blobs");

    private static string BlobRelativePath(Guid accountId, Guid attachmentId) =>
        Path.Combine("accounts", accountId.ToString("D"), "blobs", attachmentId.ToString("D"));

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
            record.OAuthAuthority);

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
            .GetAccessTokenAsync(oauthMetadata, credentialSecret, cancellationToken)
            .ConfigureAwait(false);

        if (accessToken is null && invalidateOnAuthFailure)
        {
            await _auth
                .InvalidateAsync(credentialHandle, cancellationToken)
                .ConfigureAwait(false);
        }

        return accessToken;
    }

    private static MessageInfo ToMessageInfo(MessageRecord record) =>
        new(
            record.Id,
            record.AccountId,
            record.MailboxId,
            record.RemoteId,
            record.Subject,
            record.FromAddress,
            record.ReceivedAt,
            record.IsRead);

    private sealed record RemoteMailboxSnapshot(
        RemoteMailbox Mailbox,
        IReadOnlyList<RemoteMessageSummary> Summaries,
        IReadOnlyDictionary<string, RemoteMessage> FetchedByRemoteId);
}
