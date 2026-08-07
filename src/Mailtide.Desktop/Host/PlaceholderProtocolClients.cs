using Mailtide.Core.Imap;
using Mailtide.Core.Smtp;

namespace Mailtide.Desktop.Host;

/// <summary>
/// Placeholder protocol ports until real MailKitLite adapters (#12) land.
/// Enough for browsing already-synced local store content.
/// </summary>
internal sealed class PlaceholderImapClientFactory : IImapClientFactory
{
    public IImapClient Create() => new PlaceholderImapClient();

    private sealed class PlaceholderImapClient : IImapClient
    {
        public Task ConnectAndAuthenticateAsync(
            string host,
            int port,
            string username,
            string password,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<RemoteMailbox>> ListMailboxesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RemoteMailbox>>([]);

        public Task<IReadOnlyList<RemoteMessage>> FetchMessagesAsync(
            string mailboxPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RemoteMessage>>([]);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal sealed class PlaceholderSmtpClientFactory : ISmtpClientFactory
{
    public ISmtpClient Create() => new PlaceholderSmtpClient();

    private sealed class PlaceholderSmtpClient : ISmtpClient
    {
        public Task ConnectAndAuthenticateAsync(
            string host,
            int port,
            string username,
            string password,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SubmitAsync(OutboundMessage message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
