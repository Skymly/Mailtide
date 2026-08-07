using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Mailtide.Core.Tests.Protocol;

/// <summary>
/// Minimal SMTP loopback for protocol-port contract tests.
/// </summary>
internal sealed class LoopbackSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;
    private readonly string _username;
    private readonly string _password;
    private readonly bool _rejectAuth;
    private readonly List<string> _acceptedMessages = [];
    private readonly object _gate = new();

    private LoopbackSmtpServer(
        TcpListener listener,
        string username,
        string password,
        bool rejectAuth)
    {
        _listener = listener;
        _username = username;
        _password = password;
        _rejectAuth = rejectAuth;
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = AcceptLoopAsync(_cts.Token);
    }

    public int Port { get; }

    public IReadOnlyList<string> AcceptedMessages
    {
        get
        {
            lock (_gate)
            {
                return _acceptedMessages.ToList();
            }
        }
    }

    public static LoopbackSmtpServer Start(
        string username = "alice@example.com",
        string password = "s3cret-password",
        bool rejectAuth = false)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return new LoopbackSmtpServer(listener, username, password, rejectAuth);
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        try
        {
            await _acceptLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _cts.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }

            _ = Task.Run(() => HandleClientAsync(client), CancellationToken.None);
        }
    }

    private async Task HandleClientAsync(TcpClient tcp)
    {
        using var _ = tcp;
        await using var stream = tcp.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        await using var writer = new StreamWriter(stream, Encoding.ASCII, bufferSize: 1024, leaveOpen: true)
        {
            NewLine = "\r\n",
            AutoFlush = true,
        };

        await writer.WriteLineAsync("220 localhost ESMTP").ConfigureAwait(false);
        var data = new StringBuilder();

        while (true)
        {
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line is null)
            {
                return;
            }

            var upper = line.ToUpperInvariant();
            if (upper.StartsWith("EHLO", StringComparison.Ordinal) || upper.StartsWith("HELO", StringComparison.Ordinal))
            {
                await writer.WriteLineAsync("250-localhost").ConfigureAwait(false);
                await writer.WriteLineAsync("250-AUTH LOGIN PLAIN").ConfigureAwait(false);
                await writer.WriteLineAsync("250-8BITMIME").ConfigureAwait(false);
                await writer.WriteLineAsync("250 OK").ConfigureAwait(false);
            }
            else if (upper.StartsWith("AUTH PLAIN", StringComparison.Ordinal))
            {
                string? payload = null;
                var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length >= 3)
                {
                    payload = tokens[2];
                }
                else
                {
                    await writer.WriteLineAsync("334").ConfigureAwait(false);
                    payload = await reader.ReadLineAsync().ConfigureAwait(false);
                }

                if (_rejectAuth || !CredentialsMatchPlain(payload))
                {
                    await writer.WriteLineAsync("535 Authentication failed").ConfigureAwait(false);
                }
                else
                {
                    await writer.WriteLineAsync("235 Authentication successful").ConfigureAwait(false);
                }
            }
            else if (upper.StartsWith("AUTH LOGIN", StringComparison.Ordinal))
            {
                await writer.WriteLineAsync("334 VXNlcm5hbWU6").ConfigureAwait(false);
                var userB64 = await reader.ReadLineAsync().ConfigureAwait(false);
                await writer.WriteLineAsync("334 UGFzc3dvcmQ6").ConfigureAwait(false);
                var passB64 = await reader.ReadLineAsync().ConfigureAwait(false);
                var user = DecodeBase64(userB64);
                var pass = DecodeBase64(passB64);
                if (_rejectAuth || user != _username || pass != _password)
                {
                    await writer.WriteLineAsync("535 Authentication failed").ConfigureAwait(false);
                }
                else
                {
                    await writer.WriteLineAsync("235 Authentication successful").ConfigureAwait(false);
                }
            }
            else if (upper.StartsWith("MAIL FROM", StringComparison.Ordinal)
                     || upper.StartsWith("RCPT TO", StringComparison.Ordinal)
                     || upper == "RSET")
            {
                await writer.WriteLineAsync("250 OK").ConfigureAwait(false);
            }
            else if (upper == "DATA")
            {
                await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>").ConfigureAwait(false);
                data.Clear();
                while (true)
                {
                    var dataLine = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (dataLine is null)
                    {
                        return;
                    }

                    if (dataLine == ".")
                    {
                        break;
                    }

                    data.AppendLine(dataLine);
                }

                lock (_gate)
                {
                    _acceptedMessages.Add(data.ToString());
                }

                await writer.WriteLineAsync("250 OK: queued").ConfigureAwait(false);
            }
            else if (upper == "QUIT")
            {
                await writer.WriteLineAsync("221 Bye").ConfigureAwait(false);
                return;
            }
            else
            {
                await writer.WriteLineAsync("250 OK").ConfigureAwait(false);
            }
        }
    }

    private bool CredentialsMatchPlain(string? payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return false;
        }

        var decoded = DecodeBase64(payload);
        var pieces = decoded.Split('\0');
        if (pieces.Length < 3)
        {
            return false;
        }

        return pieces[^2] == _username && pieces[^1] == _password;
    }

    private static string DecodeBase64(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return Encoding.UTF8.GetString(Convert.FromBase64String(value));
    }
}
