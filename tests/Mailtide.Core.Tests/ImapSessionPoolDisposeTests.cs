using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class ImapSessionPoolDisposeTests
{
    [TestMethod]
    public async Task DisposeAsync_does_not_wait_forever_for_an_in_flight_Idle()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var pool = new ImapSessionPool(new HangImapFactory());
        var idle = pool.UseIdleAsync(
            "127.0.0.1",
            143,
            "alice@example.com",
            "secret",
            async client =>
            {
                started.TrySetResult();
                await client.WaitForMailboxChangeAsync("INBOX").ConfigureAwait(false);
            },
            CancellationToken.None);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var dispose = pool.DisposeAsync().AsTask();
        await dispose.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(dispose.IsCompleted);
        _ = idle;
    }

    private sealed class HangImapFactory : IImapClientFactory
    {
        public IImapClient Create() => new HangImapClient();
    }

    private sealed class HangImapClient : IImapClient
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

        public Task<IReadOnlyList<RemoteMessageSummary>> FetchMessageSummariesAsync(
            string mailboxPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RemoteMessageSummary>>([]);

        public Task<IReadOnlyList<RemoteMessage>> FetchMessagesAsync(
            string mailboxPath,
            IReadOnlyList<string> remoteIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RemoteMessage>>([]);

        public Task SetSeenAsync(
            string mailboxPath,
            string remoteId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SetUnseenAsync(
            string mailboxPath,
            string remoteId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SetFlaggedAsync(
            string mailboxPath,
            string remoteId,
            bool flagged,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task WaitForMailboxChangeAsync(
            string mailboxPath,
            CancellationToken cancellationToken = default) =>
            Task.Delay(Timeout.Infinite, CancellationToken.None);

        public Task<string?> MoveAsync(
            string sourceMailboxPath,
            string destinationMailboxPath,
            string remoteId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<string> CopyAsync(
            string sourceMailboxPath,
            string destinationMailboxPath,
            string remoteId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(remoteId);

        public Task<string> CreateMailboxAsync(
            string name,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(name);

        public Task<string> RenameMailboxAsync(
            string mailboxPath,
            string newName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(newName);

        public Task DeleteMailboxAsync(
            string mailboxPath,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ExpungeAllAsync(
            string mailboxPath,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ExpungeAsync(
            string mailboxPath,
            string remoteId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
