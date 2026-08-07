using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Mailtide.Core.Smtp;

/// <summary>
/// Creates MailKitLite-backed SMTP protocol clients.
/// </summary>
public sealed class MailKitSmtpClientFactory : ISmtpClientFactory
{
    public ISmtpClient Create() => new MailKitSmtpClient();
}

internal sealed class MailKitSmtpClient : ISmtpClient
{
    private SmtpClient? _client;

    public async Task ConnectAndAuthenticateAsync(
        string host,
        int port,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(password);

        _client?.Dispose();
        _client = new SmtpClient();

        try
        {
            await _client
                .ConnectAsync(host, port, SocketOptionsForPort(port), cancellationToken)
                .ConfigureAwait(false);
            await _client
                .AuthenticateAsync(username, password, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (AuthenticationException ex)
        {
            await DisposeClientAsync().ConfigureAwait(false);
            throw new SmtpAuthenticationException("SMTP authentication failed.", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not SmtpAuthenticationException)
        {
            await DisposeClientAsync().ConfigureAwait(false);
            throw new SmtpProtocolException("SMTP protocol failure.", ex);
        }
    }

    public async Task SubmitAsync(OutboundMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var client = EnsureAuthenticated();

        try
        {
            var mime = new MimeMessage();
            mime.From.Add(MailboxAddress.Parse(message.FromAddress));
            foreach (var to in message.ToAddresses)
            {
                mime.To.Add(MailboxAddress.Parse(to));
            }

            mime.Subject = message.Subject;
            mime.Body = new TextPart("plain")
            {
                Text = message.BodyText,
            };

            await client.SendAsync(mime, cancellationToken).ConfigureAwait(false);
        }
        catch (AuthenticationException ex)
        {
            throw new SmtpAuthenticationException("SMTP authentication failed.", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not SmtpAuthenticationException)
        {
            throw new SmtpProtocolException("SMTP protocol failure.", ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeClientAsync().ConfigureAwait(false);
    }

    private async Task DisposeClientAsync()
    {
        if (_client is null)
        {
            return;
        }

        try
        {
            if (_client.IsConnected)
            {
                await _client.DisconnectAsync(quit: true).ConfigureAwait(false);
            }
        }
        catch
        {
            // Best-effort disconnect on dispose.
        }
        finally
        {
            _client.Dispose();
            _client = null;
        }
    }

    private SmtpClient EnsureAuthenticated()
    {
        if (_client is null || !_client.IsAuthenticated)
        {
            throw new InvalidOperationException("SMTP client is not authenticated.");
        }

        return _client;
    }

    private static SecureSocketOptions SocketOptionsForPort(int port) =>
        port switch
        {
            993 or 465 => SecureSocketOptions.SslOnConnect,
            143 or 587 => SecureSocketOptions.StartTlsWhenAvailable,
            _ => SecureSocketOptions.None,
        };
}
