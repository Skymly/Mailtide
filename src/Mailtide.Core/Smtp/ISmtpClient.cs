namespace Mailtide.Core.Smtp;

/// <summary>
/// Host/test-provided factory for protocol clients. Core never constructs real SMTP sockets.
/// </summary>
public interface ISmtpClientFactory
{
    ISmtpClient Create();
}

/// <summary>
/// Protocol port for submitting outbound Messages.
/// </summary>
public interface ISmtpClient : IAsyncDisposable
{
    Task ConnectAndAuthenticateAsync(
        string host,
        int port,
        string username,
        string password,
        CancellationToken cancellationToken = default);

    Task SubmitAsync(OutboundMessage message, CancellationToken cancellationToken = default);
}

public sealed record OutboundMessage(
    string FromAddress,
    IReadOnlyList<string> ToAddresses,
    string Subject,
    string BodyText);
