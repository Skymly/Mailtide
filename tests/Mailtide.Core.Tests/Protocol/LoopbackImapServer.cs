using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Mailtide.Core.Tests.Protocol;

/// <summary>
/// Minimal IMAP4rev1 loopback for protocol-port contract tests.
/// Speaks enough of the wire protocol for MailKitLite connect/list/fetch.
/// </summary>
internal sealed class LoopbackImapServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;
    private readonly List<SeededMailbox> _mailboxes;
    private readonly string _username;
    private readonly string _password;
    private readonly bool _rejectAuth;
    private readonly bool _dropOnIdle;
    private readonly HashSet<(string Path, uint Uid)> _seen = new();

    public string? LastCreateArgument { get; private set; }

    private LoopbackImapServer(
        TcpListener listener,
        IEnumerable<SeededMailbox> mailboxes,
        string username,
        string password,
        bool rejectAuth,
        bool dropOnIdle)
    {
        _listener = listener;
        _mailboxes = mailboxes.ToList();
        _username = username;
        _password = password;
        _rejectAuth = rejectAuth;
        _dropOnIdle = dropOnIdle;
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = AcceptLoopAsync(_cts.Token);
    }

    public int Port { get; }

    public static LoopbackImapServer Start(
        IEnumerable<SeededMailbox> mailboxes,
        string username = "alice@example.com",
        string password = "s3cret-password",
        bool rejectAuth = false,
        bool dropOnIdle = false)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return new LoopbackImapServer(listener, mailboxes, username, password, rejectAuth, dropOnIdle);
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

        await writer.WriteLineAsync("* OK [CAPABILITY IMAP4rev1 AUTH=PLAIN AUTH=LOGIN LOGIN NAMESPACE] IMAP ready")
            .ConfigureAwait(false);

        string? selectedPath = null;

        while (true)
        {
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line is null)
            {
                return;
            }

            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            var tag = parts[0];
            var command = parts.Length > 1 ? parts[1] : string.Empty;
            var upper = command.ToUpperInvariant();

            if (upper.StartsWith("AUTHENTICATE PLAIN", StringComparison.Ordinal))
            {
                string? payload = null;
                var tokens = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length >= 3)
                {
                    payload = tokens[2];
                }
                else
                {
                    await writer.WriteLineAsync("+").ConfigureAwait(false);
                    payload = await reader.ReadLineAsync().ConfigureAwait(false);
                }

                if (_rejectAuth || !CredentialsMatchPlain(payload))
                {
                    await writer.WriteLineAsync($"{tag} NO AUTHENTICATE failed").ConfigureAwait(false);
                }
                else
                {
                    await writer.WriteLineAsync($"{tag} OK AUTHENTICATE completed").ConfigureAwait(false);
                }
            }
            else if (upper.StartsWith("AUTHENTICATE LOGIN", StringComparison.Ordinal))
            {
                await writer.WriteLineAsync("+ VXNlcm5hbWU6").ConfigureAwait(false);
                var userB64 = await reader.ReadLineAsync().ConfigureAwait(false);
                await writer.WriteLineAsync("+ UGFzc3dvcmQ6").ConfigureAwait(false);
                var passB64 = await reader.ReadLineAsync().ConfigureAwait(false);
                var user = DecodeBase64(userB64);
                var pass = DecodeBase64(passB64);
                if (_rejectAuth || user != _username || pass != _password)
                {
                    await writer.WriteLineAsync($"{tag} NO AUTHENTICATE failed").ConfigureAwait(false);
                }
                else
                {
                    await writer.WriteLineAsync($"{tag} OK AUTHENTICATE completed").ConfigureAwait(false);
                }
            }
            else if (upper.StartsWith("LOGIN ", StringComparison.Ordinal))
            {
                if (_rejectAuth)
                {
                    await writer.WriteLineAsync($"{tag} NO LOGIN failed").ConfigureAwait(false);
                }
                else
                {
                    await writer.WriteLineAsync($"{tag} OK LOGIN completed").ConfigureAwait(false);
                }
            }
            else if (upper.StartsWith("CAPABILITY", StringComparison.Ordinal))
            {
                await writer.WriteLineAsync("* CAPABILITY IMAP4rev1 IDLE MOVE AUTH=PLAIN AUTH=LOGIN LOGIN NAMESPACE")
                    .ConfigureAwait(false);
                await writer.WriteLineAsync($"{tag} OK CAPABILITY completed").ConfigureAwait(false);
            }
            else if (upper.StartsWith("NAMESPACE", StringComparison.Ordinal))
            {
                await writer.WriteLineAsync("* NAMESPACE ((\"\" \"/\")) NIL NIL").ConfigureAwait(false);
                await writer.WriteLineAsync($"{tag} OK NAMESPACE completed").ConfigureAwait(false);
            }
            else if (upper.StartsWith("CREATE", StringComparison.Ordinal))
            {
                var argument = command.Length > 6 ? command[6..].Trim() : string.Empty;
                var name = Unquote(argument);
                LastCreateArgument = name;
                var path = name.TrimEnd('/');
                IReadOnlyList<string> attrs = name.EndsWith('/')
                    ? ["Noselect"]
                    : [];
                _mailboxes.Add(new SeededMailbox(path, attrs, []));
                await writer.WriteLineAsync($"{tag} OK CREATE completed").ConfigureAwait(false);
            }
            else if (upper.StartsWith("LIST", StringComparison.Ordinal))
            {
                foreach (var mailbox in _mailboxes)
                {
                    var attrs = string.Join(' ', mailbox.Attributes.Select(a => "\\" + a));
                    if (attrs.Length == 0)
                    {
                        attrs = "\\HasNoChildren";
                    }
                    else if (!mailbox.Attributes.Contains("HasNoChildren", StringComparer.OrdinalIgnoreCase))
                    {
                        attrs += " \\HasNoChildren";
                    }

                    await writer.WriteLineAsync($"* LIST ({attrs}) \"/\" \"{mailbox.Path}\"")
                        .ConfigureAwait(false);
                }

                await writer.WriteLineAsync($"{tag} OK LIST completed").ConfigureAwait(false);
            }
            else if (upper == "IDLE")
            {
                var mailbox = FindMailbox(selectedPath);
                var count = mailbox?.Messages.Count ?? 0;
                await writer.WriteLineAsync("+ idling").ConfigureAwait(false);
                if (_dropOnIdle)
                {
                    return;
                }

                await writer.WriteLineAsync($"* {count + 1} EXISTS").ConfigureAwait(false);
                while (true)
                {
                    var done = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (done is null || done.Equals("DONE", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                }

                await writer.WriteLineAsync($"{tag} OK IDLE terminated").ConfigureAwait(false);
            }
            else if (upper.StartsWith("SELECT ", StringComparison.Ordinal)
                     || upper.StartsWith("EXAMINE ", StringComparison.Ordinal))
            {
                selectedPath = Unquote(command.Split(' ', 2)[1]);
                var mailbox = FindMailbox(selectedPath);
                var count = mailbox?.Messages.Count ?? 0;
                await writer.WriteLineAsync($"* {count} EXISTS").ConfigureAwait(false);
                await writer.WriteLineAsync("* 0 RECENT").ConfigureAwait(false);
                await writer.WriteLineAsync("* OK [UIDVALIDITY 1] UIDs valid").ConfigureAwait(false);
                await writer.WriteLineAsync($"* OK [UIDNEXT {count + 1}] Predicted next UID")
                    .ConfigureAwait(false);
                await writer.WriteLineAsync("* FLAGS (\\Answered \\Flagged \\Deleted \\Seen \\Draft)")
                    .ConfigureAwait(false);
                await writer.WriteLineAsync("* OK [PERMANENTFLAGS (\\Deleted \\Seen \\*)] Limited")
                    .ConfigureAwait(false);
                var access = upper.StartsWith("EXAMINE", StringComparison.Ordinal) ? "READ-ONLY" : "READ-WRITE";
                await writer.WriteLineAsync($"{tag} OK [{access}] completed").ConfigureAwait(false);
            }
            else if (upper.Contains("MOVE", StringComparison.Ordinal))
            {
                await writer.WriteLineAsync($"{tag} OK MOVE completed").ConfigureAwait(false);
            }
            else if (upper.Contains("FETCH", StringComparison.Ordinal))
            {
                var mailbox = FindMailbox(selectedPath);
                if (mailbox is null || mailbox.Messages.Count == 0)
                {
                    await writer.WriteLineAsync($"{tag} OK FETCH completed").ConfigureAwait(false);
                    continue;
                }

                var onlyUids = ParseUidSet(command);
                var wantBody = upper.Contains("BODY.PEEK[]", StringComparison.Ordinal)
                               || upper.Contains("BODY[]", StringComparison.Ordinal)
                               || upper.Contains("RFC822", StringComparison.Ordinal);

                for (var i = 0; i < mailbox.Messages.Count; i++)
                {
                    var msg = mailbox.Messages[i];
                    var seq = i + 1;
                    var uid = msg.Uid;
                    if (onlyUids is not null && !onlyUids.Contains(uid))
                    {
                        continue;
                    }
                    var flags = msg.IsRead || _seen.Contains((selectedPath ?? string.Empty, uid)) ? "\\Seen" : "";
                    var flagPart = string.IsNullOrEmpty(flags) ? "FLAGS ()" : $"FLAGS ({flags})";
                    var date = msg.InternalDate.ToString("dd-MMM-yyyy HH:mm:ss +0000",
                        System.Globalization.CultureInfo.InvariantCulture);

                    if (wantBody)
                    {
                        var bytes = Encoding.ASCII.GetBytes(msg.Rfc822);
                        await writer
                            .WriteLineAsync(
                                $"* {seq} FETCH (UID {uid} {flagPart} INTERNALDATE \"{date}\" BODY[] {{{bytes.Length}}}")
                            .ConfigureAwait(false);
                        await stream.WriteAsync(bytes).ConfigureAwait(false);
                        await writer.WriteLineAsync(")").ConfigureAwait(false);
                    }
                    else
                    {
                        // Only items MailKit requests for our summary fetch — avoid broken ENVELOPE/BODYSTRUCTURE.
                        await writer
                            .WriteLineAsync(
                                $"* {seq} FETCH (UID {uid} {flagPart} INTERNALDATE \"{date}\")")
                            .ConfigureAwait(false);
                    }
                }

                await writer.WriteLineAsync($"{tag} OK FETCH completed").ConfigureAwait(false);
            }
            else if (upper.Contains("STORE", StringComparison.Ordinal))
            {
                var mailbox = FindMailbox(selectedPath);
                var add = System.Text.RegularExpressions.Regex.Match(
                    command,
                    @"(?:UID\s+)?STORE\s+(\d+)\s+\+FLAGS",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var remove = System.Text.RegularExpressions.Regex.Match(
                    command,
                    @"(?:UID\s+)?STORE\s+(\d+)\s+-FLAGS",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (mailbox is not null && add.Success && uint.TryParse(add.Groups[1].Value, out var storedUid))
                {
                    _seen.Add((mailbox.Path, storedUid));
                }
                else if (mailbox is not null && remove.Success && uint.TryParse(remove.Groups[1].Value, out var unseenUid))
                {
                    _seen.Remove((mailbox.Path, unseenUid));
                }

                await writer.WriteLineAsync($"{tag} OK STORE completed").ConfigureAwait(false);
            }            else if (upper.StartsWith("LOGOUT", StringComparison.Ordinal))
            {
                await writer.WriteLineAsync("* BYE").ConfigureAwait(false);
                await writer.WriteLineAsync($"{tag} OK LOGOUT completed").ConfigureAwait(false);
                return;
            }
            else
            {
                await writer.WriteLineAsync($"{tag} OK completed").ConfigureAwait(false);
            }
        }
    }

    private SeededMailbox? FindMailbox(string? path)
    {
        if (path is null)
        {
            return null;
        }

        return _mailboxes.FirstOrDefault(m =>
            string.Equals(m.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    private bool CredentialsMatchPlain(string? payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return false;
        }

        var decoded = DecodeBase64(payload);
        // PLAIN: \0 username \0 password
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

    private static HashSet<uint>? ParseUidSet(string command)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            command,
            @"UID\s+FETCH\s+([\d:,]+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        var uids = new HashSet<uint>();
        foreach (var part in match.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bounds = part.Split(':');
            if (bounds.Length == 1 && uint.TryParse(bounds[0], out var one))
            {
                uids.Add(one);
            }
            else if (bounds.Length == 2
                     && uint.TryParse(bounds[0], out var start)
                     && uint.TryParse(bounds[1], out var end))
            {
                for (var uid = start; uid <= end; uid++)
                {
                    uids.Add(uid);
                }
            }
        }

        return uids;
    }
    private static string Unquote(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            return value[1..^1];
        }

        return value;
    }
}

internal sealed record SeededMailbox(
    string Path,
    IReadOnlyList<string> Attributes,
    IReadOnlyList<SeededImapMessage> Messages);

internal sealed record SeededImapMessage(
    uint Uid,
    string Subject,
    string From,
    DateTimeOffset InternalDate,
    bool IsRead,
    string BodyText,
    IReadOnlyList<SeededImapAttachment>? Attachments = null)
{
    public string? HtmlBody { get; init; }

    public string? InternetMessageId { get; init; }

    public string? ReferencesHeader { get; init; }

    public string? To { get; init; }

    public string? Bcc { get; init; }

    public string? ReplyTo { get; init; }

    private string ThreadHeaders
    {
        get
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(InternetMessageId))
            {
                sb.Append("Message-ID: ").Append(InternetMessageId).Append("\r\n");
            }

            if (!string.IsNullOrWhiteSpace(ReferencesHeader))
            {
                sb.Append("References: ").Append(ReferencesHeader).Append("\r\n");
            }

            if (!string.IsNullOrWhiteSpace(To))
            {
                sb.Append("To: ").Append(To).Append("\r\n");
            }

            if (!string.IsNullOrWhiteSpace(Bcc))
            {
                sb.Append("Bcc: ").Append(Bcc).Append("\r\n");
            }

            if (!string.IsNullOrWhiteSpace(ReplyTo))
            {
                sb.Append("Reply-To: ").Append(ReplyTo).Append("\r\n");
            }

            return sb.ToString();
        }
    }

    public string Rfc822
    {
        get
        {
            if (Attachments is { Count: > 0 })
            {
                var boundary = "mailtide-boundary";
                var sb = new StringBuilder();
                sb.Append("From: ").Append(From).Append("\r\n");
                sb.Append("Subject: ").Append(Subject).Append("\r\n");
                sb.Append("Date: ").Append(InternalDate.UtcDateTime.ToString("r")).Append("\r\n");
                sb.Append(ThreadHeaders);
                sb.Append("MIME-Version: 1.0\r\n");
                sb.Append("Content-Type: multipart/mixed; boundary=\"").Append(boundary).Append("\"\r\n");
                sb.Append("\r\n");
                sb.Append("--").Append(boundary).Append("\r\n");
                sb.Append("Content-Type: text/plain; charset=utf-8\r\n\r\n");
                sb.Append(BodyText).Append("\r\n");
                foreach (var attachment in Attachments)
                {
                    sb.Append("--").Append(boundary).Append("\r\n");
                    sb.Append("Content-Type: ").Append(attachment.ContentType).Append("\r\n");
                    sb.Append("Content-Transfer-Encoding: base64\r\n");
                    if (!string.IsNullOrWhiteSpace(attachment.ContentId))
                    {
                        sb.Append("Content-ID: <").Append(attachment.ContentId).Append(">\r\n");
                    }

                    sb.Append("Content-Disposition: attachment; filename=\"")
                        .Append(attachment.FileName)
                        .Append("\"\r\n\r\n");
                    sb.Append(Convert.ToBase64String(attachment.Content)).Append("\r\n");
                }

                sb.Append("--").Append(boundary).Append("--\r\n");
                return sb.ToString();
            }

            if (!string.IsNullOrEmpty(HtmlBody) && string.IsNullOrEmpty(BodyText))
            {
                return
                    $"From: {From}\r\n" +
                    $"Subject: {Subject}\r\n" +
                    $"Date: {InternalDate.UtcDateTime:r}\r\n" +
                    ThreadHeaders +
                    "MIME-Version: 1.0\r\n" +
                    "Content-Type: text/html; charset=utf-8\r\n" +
                    "\r\n" +
                    $"{HtmlBody}\r\n";
            }

            return
                $"From: {From}\r\n" +
                $"Subject: {Subject}\r\n" +
                $"Date: {InternalDate.UtcDateTime:r}\r\n" +
                ThreadHeaders +
                "MIME-Version: 1.0\r\n" +
                "Content-Type: text/plain; charset=utf-8\r\n" +
                "\r\n" +
                $"{BodyText}\r\n";
        }
    }
}

internal sealed record SeededImapAttachment(
    string FileName,
    string ContentType,
    byte[] Content)
{
    public string? ContentId { get; init; }
}
