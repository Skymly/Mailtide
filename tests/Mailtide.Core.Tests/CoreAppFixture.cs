using Mailtide.Core;
using Mailtide.Core.Imap;
using Mailtide.Core.Security;
using Mailtide.Core.Smtp;

namespace Mailtide.Core.Tests;

internal sealed class CoreAppFixture : IDisposable
{
    private readonly string _appDataDirectory =
        Path.Combine(Path.GetTempPath(), "mailtide-tests", Guid.NewGuid().ToString("N"));

    public FakeSecureStorage SecureStorage { get; } = new();

    public FakeImapClientFactory Imap { get; } = new();

    public FakeSmtpClientFactory Smtp { get; } = new();

    public string AppDataDirectory => _appDataDirectory;

    public Task<MailtideApp> OpenAppAsync() =>
        MailtideApp.OpenAsync(_appDataDirectory, SecureStorage, Imap, Smtp);

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

internal sealed class FakeImapClientFactory : IImapClientFactory
{
    private readonly List<RemoteMailbox> _mailboxes = [];
    private readonly Dictionary<string, List<RemoteMessage>> _messagesByPath =
        new(StringComparer.Ordinal);

    public Exception? FailWith { get; set; }

    public TaskCompletionSource? BlockConnectUntil { get; set; }

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

    public IImapClient Create() => new FakeImapClient(this);

    private sealed class FakeImapClient : IImapClient
    {
        private readonly FakeImapClientFactory _factory;
        private bool _authenticated;

        public FakeImapClient(FakeImapClientFactory factory)
        {
            _factory = factory;
        }

        public async Task ConnectAndAuthenticateAsync(
            string host,
            int port,
            string username,
            string password,
            CancellationToken cancellationToken = default)
        {
            if (_factory.BlockConnectUntil is not null)
            {
                await _factory.BlockConnectUntil.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            if (_factory.FailWith is not null)
            {
                throw _factory.FailWith;
            }

            _ = host;
            _ = port;
            _ = username;
            _ = password;
            _authenticated = true;
        }

        public Task<IReadOnlyList<RemoteMailbox>> ListMailboxesAsync(
            CancellationToken cancellationToken = default)
        {
            EnsureAuthenticated();
            return Task.FromResult<IReadOnlyList<RemoteMailbox>>(_factory._mailboxes.ToList());
        }

        public Task<IReadOnlyList<RemoteMessage>> FetchMessagesAsync(
            string mailboxPath,
            CancellationToken cancellationToken = default)
        {
            EnsureAuthenticated();
            if (!_factory._messagesByPath.TryGetValue(mailboxPath, out var messages))
            {
                messages = [];
            }

            return Task.FromResult<IReadOnlyList<RemoteMessage>>(messages.ToList());
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
