using Mailtide.Core;
using Mailtide.Core.Auth;
using Mailtide.Core.Imap;
using Mailtide.Core.Security;
using Mailtide.Core.Smtp;

namespace Mailtide.Core.Tests;

internal sealed class CoreAppFixture : IDisposable
{
    private readonly string _appDataDirectory =
        Path.Combine(Path.GetTempPath(), "mailtide-tests", Guid.NewGuid().ToString("N"));

    public FakeSecureStorage SecureStorage { get; } = new();

    public FakeOAuthClient OAuth { get; } = new();

    public FakeImapClientFactory Imap { get; } = new();

    public FakeSmtpClientFactory Smtp { get; } = new();

    public string AppDataDirectory => _appDataDirectory;

    public Task<MailtideApp> OpenAppAsync() =>
        MailtideApp.OpenAsync(_appDataDirectory, SecureStorage, OAuth, Imap, Smtp);

    public void Dispose()
    {
        if (Directory.Exists(_appDataDirectory))
        {
            Directory.Delete(_appDataDirectory, recursive: true);
        }
    }
}

internal sealed class FakeSecureStorage : ISecureStorage
{
    private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);

    public Task StoreSecretAsync(string key, string secret, CancellationToken cancellationToken = default)
    {
        _secrets[key] = secret;
        return Task.CompletedTask;
    }

    public Task<string?> RetrieveSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        _secrets.TryGetValue(key, out var secret);
        return Task.FromResult<string?>(secret);
    }

    public Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        _secrets.Remove(key);
        return Task.CompletedTask;
    }
}

internal sealed class FakeOAuthClient : IOAuthClient
{
    public OAuthAuthorizationResult? AuthorizeResult { get; set; }

    public OAuthAccessTokenResult? RefreshResult { get; set; }

    public Exception? AuthorizeFailWith { get; set; }

    public Exception? RefreshFailWith { get; set; }

    public OAuthAuthorizeRequest? LastAuthorizeRequest { get; private set; }

    public OAuthRefreshRequest? LastRefreshRequest { get; private set; }

    public int RefreshCallCount { get; private set; }

    public bool RejectStaleRefreshSecrets { get; set; }

    private readonly HashSet<string> _retiredRefreshSecrets = new(StringComparer.Ordinal);

    public Task<OAuthAuthorizationResult> AuthorizeAsync(
        OAuthAuthorizeRequest request,
        CancellationToken cancellationToken = default)
    {
        LastAuthorizeRequest = request;

        if (AuthorizeFailWith is not null)
        {
            throw AuthorizeFailWith;
        }

        if (AuthorizeResult is null)
        {
            throw new InvalidOperationException("FakeOAuthClient.AuthorizeResult was not set.");
        }

        return Task.FromResult(AuthorizeResult);
    }

    public Task<OAuthAccessTokenResult> RefreshAsync(
        OAuthRefreshRequest request,
        CancellationToken cancellationToken = default)
    {
        LastRefreshRequest = request;
        RefreshCallCount++;

        if (RefreshFailWith is not null)
        {
            throw RefreshFailWith;
        }

        if (RefreshResult is null)
        {
            throw new InvalidOperationException("FakeOAuthClient.RefreshResult was not set.");
        }

        lock (_retiredRefreshSecrets)
        {
            if (RejectStaleRefreshSecrets && _retiredRefreshSecrets.Contains(request.RefreshSecret))
            {
                throw new OAuthAuthenticationException("invalid_grant");
            }

            if (RejectStaleRefreshSecrets
                && !string.IsNullOrWhiteSpace(RefreshResult.RefreshSecret)
                && !string.Equals(RefreshResult.RefreshSecret, request.RefreshSecret, StringComparison.Ordinal))
            {
                _retiredRefreshSecrets.Add(request.RefreshSecret);
            }
        }

        return Task.FromResult(RefreshResult);
    }
}

internal sealed class FakeImapClientFactory : IImapClientFactory
{
    private readonly List<RemoteMailbox> _mailboxes = [];
    private readonly Dictionary<string, List<RemoteMessage>> _messagesByPath =
        new(StringComparer.Ordinal);

    public Exception? FailWith { get; set; }

    public TaskCompletionSource? BlockConnectUntil { get; set; }

    public string? LastPassword { get; private set; }

    private int _activeConnects;

    public int MaxActiveConnects { get; private set; }
    public string? LastSetSeenMailboxPath { get; private set; }

    public string? LastSetSeenRemoteId { get; private set; }

    public string? LastSetUnseenMailboxPath { get; private set; }

    public string? LastSetUnseenRemoteId { get; private set; }

    public string? LastSetFlaggedMailboxPath { get; private set; }

    public string? LastSetFlaggedRemoteId { get; private set; }

    public bool? LastSetFlaggedValue { get; private set; }

    public string? LastMoveSourcePath { get; private set; }

    public string? LastMoveDestinationPath { get; private set; }

    public string? LastMoveRemoteId { get; private set; }

    public string? LastCreatedMailboxPath { get; set; }

    public string? LastRenamedMailboxPath { get; set; }

    public string? LastRenamedMailboxNewName { get; set; }

    public string? LastDeletedMailboxPath { get; set; }

    public string? LastExpungeMailboxPath { get; private set; }



    public List<string> FetchedRemoteIds { get; } = [];

    private TaskCompletionSource _mailboxChange =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int IdleWaiters { get; private set; }

    public void SignalMailboxChange()
    {
        var previous = Interlocked.Exchange(
            ref _mailboxChange,
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        previous.TrySetResult();
    }

    internal async Task WaitForMailboxChangeAsync(CancellationToken cancellationToken)
    {
        IdleWaiters++;
        try
        {
            await _mailboxChange.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            IdleWaiters--;
        }
    }


    public void SeedMailboxes(params RemoteMailbox[] mailboxes)
    {
        _mailboxes.Clear();
        _mailboxes.AddRange(mailboxes);
    }

    public void SeedMessages(string mailboxPath, params RemoteMessage[] messages)
    {
        _messagesByPath[mailboxPath] = messages.ToList();
    }

    public void ClearMessages() => _messagesByPath.Clear();

    public int CreateCount { get; private set; }

    public IImapClient Create()
    {
        CreateCount++;
        return new FakeImapClient(this);
    }

    private sealed class FakeImapClient : IImapClient
    {
        private readonly FakeImapClientFactory _factory;
        private bool _authenticated;

        public FakeImapClient(FakeImapClientFactory factory)
        {
            _factory = factory;
        }

        private void ThrowIfFailed()
        {
            if (_factory.FailWith is not null)
            {
                throw _factory.FailWith;
            }
        }

        public async Task ConnectAndAuthenticateAsync(
            string host,
            int port,
            string username,
            string password,
            CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _factory._activeConnects);
            if (active > _factory.MaxActiveConnects)
            {
                _factory.MaxActiveConnects = active;
            }

            try
            {
            if (_factory.BlockConnectUntil is not null)
            {
                await _factory.BlockConnectUntil.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            ThrowIfFailed();

            _ = host;
            _ = port;
            _ = username;
            _factory.LastPassword = password;
            _authenticated = true;
            }
            finally
            {
                Interlocked.Decrement(ref _factory._activeConnects);
            }
        }

        public Task<IReadOnlyList<RemoteMailbox>> ListMailboxesAsync(
            CancellationToken cancellationToken = default)
        {
            EnsureAuthenticated();
            ThrowIfFailed();
            return Task.FromResult<IReadOnlyList<RemoteMailbox>>(_factory._mailboxes.ToList());
        }

        public Task<IReadOnlyList<RemoteMessage>> FetchMessagesAsync(
            string mailboxPath,
            CancellationToken cancellationToken = default)
        {
            EnsureAuthenticated();
            ThrowIfFailed();
            if (!_factory._messagesByPath.TryGetValue(mailboxPath, out var messages))
            {
                messages = [];
            }

            return Task.FromResult<IReadOnlyList<RemoteMessage>>(messages.ToList());
        }

        public Task<IReadOnlyList<RemoteMessageSummary>> FetchMessageSummariesAsync(
            string mailboxPath,
            CancellationToken cancellationToken = default)
        {
            EnsureAuthenticated();
            ThrowIfFailed();
            if (!_factory._messagesByPath.TryGetValue(mailboxPath, out var messages))
            {
                messages = [];
            }

            return Task.FromResult<IReadOnlyList<RemoteMessageSummary>>(
                messages.Select(m => new RemoteMessageSummary(m.RemoteId, m.IsRead, m.Subject, m.FromAddress, m.ReceivedAt, m.IsFlagged)).ToList());
        }

        public Task<IReadOnlyList<RemoteMessage>> FetchMessagesAsync(
            string mailboxPath,
            IReadOnlyList<string> remoteIds,
            CancellationToken cancellationToken = default)
        {
            EnsureAuthenticated();
            ThrowIfFailed();
            if (!_factory._messagesByPath.TryGetValue(mailboxPath, out var messages))
            {
                messages = [];
            }

            var wanted = remoteIds.ToHashSet(StringComparer.Ordinal);
            var fetched = messages.Where(m => wanted.Contains(m.RemoteId)).ToList();
            _factory.FetchedRemoteIds.AddRange(fetched.Select(m => m.RemoteId));
            return Task.FromResult<IReadOnlyList<RemoteMessage>>(fetched);
        }

        public Task WaitForMailboxChangeAsync(
            string mailboxPath,
            CancellationToken cancellationToken = default)
        {
            EnsureAuthenticated();
            ThrowIfFailed();
            _ = mailboxPath;
            return _factory.WaitForMailboxChangeAsync(cancellationToken);
        }

        public Task SetSeenAsync(
            string mailboxPath,
            string remoteId,
            CancellationToken cancellationToken = default)
        {
            EnsureAuthenticated();
            ThrowIfFailed();
            _factory.LastSetSeenMailboxPath = mailboxPath;
            _factory.LastSetSeenRemoteId = remoteId;
            return Task.CompletedTask;
        }

        public Task SetUnseenAsync(
            string mailboxPath,
            string remoteId,
            CancellationToken cancellationToken = default)
        {
            EnsureAuthenticated();
            ThrowIfFailed();
            _factory.LastSetUnseenMailboxPath = mailboxPath;
            _factory.LastSetUnseenRemoteId = remoteId;
            return Task.CompletedTask;
        }

        public Task SetFlaggedAsync(
            string mailboxPath,
            string remoteId,
            bool flagged,
            CancellationToken cancellationToken = default)
        {
            EnsureAuthenticated();
            ThrowIfFailed();
            _factory.LastSetFlaggedMailboxPath = mailboxPath;
            _factory.LastSetFlaggedRemoteId = remoteId;
            _factory.LastSetFlaggedValue = flagged;
            if (_factory._messagesByPath.TryGetValue(mailboxPath, out var messages))
            {
                _factory._messagesByPath[mailboxPath] = messages
                    .Select(m => m.RemoteId == remoteId ? m with { IsFlagged = flagged } : m)
                    .ToList();
            }

            return Task.CompletedTask;
        }

        public Task<string?> MoveAsync(
            string sourceMailboxPath,
            string destinationMailboxPath,
            string remoteId,
            CancellationToken cancellationToken = default)
        {
            EnsureAuthenticated();
            ThrowIfFailed();
            _factory.LastMoveSourcePath = sourceMailboxPath;
            _factory.LastMoveDestinationPath = destinationMailboxPath;
            _factory.LastMoveRemoteId = remoteId;
            if (!_factory._messagesByPath.TryGetValue(sourceMailboxPath, out var source))
            {
                return Task.FromResult<string?>(null);
            }

            var moved = source.Where(m => m.RemoteId == remoteId).ToList();
            _factory._messagesByPath[sourceMailboxPath] = source.Where(m => m.RemoteId != remoteId).ToList();
            if (!_factory._messagesByPath.TryGetValue(destinationMailboxPath, out var dest))
            {
                dest = [];
            }

            uint next = 1;
            foreach (var existing in dest)
            {
                if (uint.TryParse(existing.RemoteId, out var uid) && uid >= next)
                {
                    next = uid + 1;
                }
            }

            var remapped = new List<RemoteMessage>(moved.Count);
            string? assigned = null;
            foreach (var message in moved)
            {
                assigned = next.ToString();
                next++;
                remapped.Add(message with { RemoteId = assigned });
            }

            _factory._messagesByPath[destinationMailboxPath] = dest.Concat(remapped).ToList();
            return Task.FromResult(assigned);
        }

        public Task<string> CreateMailboxAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            EnsureAuthenticated();
            ThrowIfFailed();

            var path = name.Trim();
            _factory._mailboxes.Add(new RemoteMailbox(path, path, Role: null));
            _factory.LastCreatedMailboxPath = path;
            return Task.FromResult(path);
        }

        public Task<string> RenameMailboxAsync(
            string mailboxPath,
            string newName,
            CancellationToken cancellationToken = default)
        {
            EnsureAuthenticated();
            ThrowIfFailed();

            var path = newName.Trim();
            for (var i = 0; i < _factory._mailboxes.Count; i++)
            {
                if (_factory._mailboxes[i].Path == mailboxPath)
                {
                    var current = _factory._mailboxes[i];
                    _factory._mailboxes[i] = new RemoteMailbox(path, path, current.Role);
                }
            }

            if (_factory._messagesByPath.TryGetValue(mailboxPath, out var messages))
            {
                _factory._messagesByPath.Remove(mailboxPath);
                _factory._messagesByPath[path] = messages;
            }

            _factory.LastRenamedMailboxPath = mailboxPath;
            _factory.LastRenamedMailboxNewName = path;
            return Task.FromResult(path);
        }

        public Task DeleteMailboxAsync(
            string mailboxPath,
            CancellationToken cancellationToken = default)
        {
            EnsureAuthenticated();
            ThrowIfFailed();

            _factory._mailboxes.RemoveAll(m => m.Path == mailboxPath);
            _factory._messagesByPath.Remove(mailboxPath);
            _factory.LastDeletedMailboxPath = mailboxPath;
            return Task.CompletedTask;
        }

        public Task ExpungeAllAsync(
            string mailboxPath,
            CancellationToken cancellationToken = default)
        {
            EnsureAuthenticated();
            ThrowIfFailed();
            _factory.LastExpungeMailboxPath = mailboxPath;
            _factory._messagesByPath[mailboxPath] = [];
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private void EnsureAuthenticated()
        {
            if (!_authenticated)
            {
                throw new InvalidOperationException("IMAP client is not authenticated.");
            }
        }
    }
}

internal sealed class FakeSmtpClientFactory : ISmtpClientFactory
{
    private readonly List<OutboundMessage> _submitted = [];

    public Exception? FailWith { get; set; }

    public TaskCompletionSource? BlockSubmitUntil { get; set; }

    public IReadOnlyList<OutboundMessage> Submitted => _submitted;

    public ISmtpClient Create() => new FakeSmtpClient(this);

    private sealed class FakeSmtpClient : ISmtpClient
    {
        private readonly FakeSmtpClientFactory _factory;
        private bool _authenticated;

        public FakeSmtpClient(FakeSmtpClientFactory factory)
        {
            _factory = factory;
        }

        public Task ConnectAndAuthenticateAsync(
            string host,
            int port,
            string username,
            string password,
            CancellationToken cancellationToken = default)
        {
            if (_factory.FailWith is SmtpAuthenticationException)
            {
                throw _factory.FailWith;
            }

            _ = host;
            _ = port;
            _ = username;
            _ = password;
            _authenticated = true;
            return Task.CompletedTask;
        }

        public async Task SubmitAsync(OutboundMessage message, CancellationToken cancellationToken = default)
        {
            EnsureAuthenticated();

            if (_factory.BlockSubmitUntil is not null)
            {
                await _factory.BlockSubmitUntil.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            if (_factory.FailWith is not null)
            {
                throw _factory.FailWith;
            }

            _factory._submitted.Add(message);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private void EnsureAuthenticated()
        {
            if (!_authenticated)
            {
                throw new InvalidOperationException("SMTP client is not authenticated.");
            }
        }
    }
}
