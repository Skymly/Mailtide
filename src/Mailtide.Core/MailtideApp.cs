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

            return records.Select(ToInfo).ToList();
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

    public async Task SyncNowAsync(Guid accountId, CancellationToken cancellationToken = default)
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

            var snapshot = await FetchRemoteSnapshotAsync(client, cancellationToken)
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

            return records
                .Select(m => new MailboxInfo(m.Id, m.AccountId, m.Name, m.Path, m.Role))
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

    public async Task<DraftInfo> SaveDraftAsync(
        Guid accountId,
        DraftContent content,
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
            var record = new DraftRecord
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                ToAddresses = EncodeAddresses(content.ToAddresses),
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
                Subject = draft.Subject,
                BodyText = draft.BodyText,
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
                        item.BodyText);
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
        var existingAttachments = await _db.Attachments
            .Where(a => a.AccountId == accountId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        _db.Attachments.RemoveRange(existingAttachments);

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

        ResetBlobArea(accountId);

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
                var messageId = Guid.NewGuid();
                _db.Messages.Add(new MessageRecord
                {
                    Id = messageId,
                    AccountId = accountId,
                    MailboxId = mailboxId,
                    RemoteId = remoteMessage.RemoteId,
                    Subject = remoteMessage.Subject,
                    FromAddress = remoteMessage.FromAddress,
                    ReceivedAt = remoteMessage.ReceivedAt,
                    IsRead = remoteMessage.IsRead,
                    BodyText = remoteMessage.BodyText,
                });

                foreach (var remoteAttachment in remoteMessage.Attachments)
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
        }

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

    private static string EncodeAddresses(IReadOnlyList<string> addresses) =>
        JsonSerializer.Serialize(addresses);

    private static IReadOnlyList<string> DecodeAddresses(string encoded) =>
        JsonSerializer.Deserialize<string[]>(encoded) ?? [];

    private static DraftInfo ToDraftInfo(DraftRecord record) =>
        new(
            record.Id,
            record.AccountId,
            DecodeAddresses(record.ToAddresses),
            record.Subject,
            record.BodyText,
            record.UpdatedAt);

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
        IReadOnlyList<RemoteMessage> Messages);
}
