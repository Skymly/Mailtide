using System.Collections.Concurrent;
using Mailtide.Core.Imap;

namespace Mailtide.Core;

/// <summary>
/// Reuses one command IMAP session and one IDLE session per endpoint.
/// Failed sessions are dropped so the next use reconnects.
/// </summary>
internal sealed class ImapSessionPool : IAsyncDisposable
{
    internal static readonly TimeSpan DisposeGateTimeout = TimeSpan.FromSeconds(2);

    public enum Kind
    {
        Command = 0,
        Idle = 1,
    }

    private readonly IImapClientFactory _factory;
    private readonly ConcurrentDictionary<(Kind Kind, string Key), Slot> _slots = new();

    public ImapSessionPool(IImapClientFactory factory)
    {
        _factory = factory;
    }

    public Task UseCommandAsync(
        string host,
        int port,
        string username,
        string secret,
        Func<IImapClient, Task> action,
        CancellationToken cancellationToken) =>
        UseAsync(Kind.Command, host, port, username, secret, action, cancellationToken);

    public Task UseIdleAsync(
        string host,
        int port,
        string username,
        string secret,
        Func<IImapClient, Task> action,
        CancellationToken cancellationToken) =>
        UseAsync(Kind.Idle, host, port, username, secret, action, cancellationToken);

    public async Task UseAsync(
        Kind kind,
        string host,
        int port,
        string username,
        string secret,
        Func<IImapClient, Task> action,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        ArgumentNullException.ThrowIfNull(action);

        var slot = _slots.GetOrAdd((kind, Key(host, port, username)), _ => new Slot());
        await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (slot.Client is null || slot.Secret != secret)
            {
                await DisposeClientAsync(slot).ConfigureAwait(false);
                var client = _factory.Create();
                try
                {
                    await client
                        .ConnectAndAuthenticateAsync(host, port, username, secret, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    await client.DisposeAsync().ConfigureAwait(false);
                    throw;
                }

                slot.Client = client;
                slot.Secret = secret;
            }

            try
            {
                await action(slot.Client).ConfigureAwait(false);
            }
            catch
            {
                await DisposeClientAsync(slot).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            slot.Gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var slot in _slots.Values)
        {
            var acquired = await slot.Gate.WaitAsync(DisposeGateTimeout).ConfigureAwait(false);
            try
            {
                await DisposeClientAsync(slot).ConfigureAwait(false);
            }
            finally
            {
                if (acquired)
                {
                    slot.Gate.Release();
                }

                slot.Gate.Dispose();
            }
        }

        _slots.Clear();
    }

    private static string Key(string host, int port, string username) =>
        host + "\u001f" + port + "\u001f" + username;

    private static async Task DisposeClientAsync(Slot slot)
    {
        if (slot.Client is null)
        {
            return;
        }

        try
        {
            await slot.Client.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best-effort dispose of a dropped session.
        }
        finally
        {
            slot.Client = null;
            slot.Secret = null;
        }
    }

    private sealed class Slot
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public IImapClient? Client { get; set; }

        public string? Secret { get; set; }
    }
}
