using Mailtide.Core.Auth;
using Mailtide.Core.Imap;
using Mailtide.Core.Store;

namespace Mailtide.Core;

public sealed partial class MailtideApp
{
    private readonly record struct AccountImapEndpoint(
        string Host,
        int Port,
        string EmailAddress,
        string CredentialHandle,
        CredentialKind CredentialKind,
        OAuthTokenMetadata? OAuthMetadata);

    private static AccountImapEndpoint ToImapEndpoint(AccountRecord account) =>
        new(
            account.ImapHost,
            account.ImapPort,
            account.EmailAddress,
            account.CredentialHandle,
            account.CredentialKind,
            account.CredentialKind == CredentialKind.OAuth ? RequireOAuthMetadata(account) : null);

    private async Task<(AccountImapEndpoint Endpoint, string? Secret)> BindImapEndpointAsync(
        AccountRecord account,
        CancellationToken cancellationToken)
    {
        var endpoint = ToImapEndpoint(account);
        var secret = await _auth
            .RetrieveCredentialSecretAsync(endpoint.CredentialHandle, cancellationToken)
            .ConfigureAwait(false);
        return (endpoint, secret);
    }

    private async Task<string?> ResolveImapSecretAsync(
        AccountImapEndpoint endpoint,
        string? secret,
        bool reportStatus,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        if (secret is null)
        {
            if (reportStatus)
            {
                SetStatus(accountId, AccountStatus.Error(AuthenticationFailedMessage));
                return null;
            }

            throw new InvalidOperationException(AuthenticationFailedMessage);
        }

        var protocolSecret = await ResolveProtocolSecretAsync(
                endpoint.CredentialKind,
                endpoint.OAuthMetadata,
                secret,
                endpoint.CredentialHandle,
                invalidateOnAuthFailure: true,
                cancellationToken)
            .ConfigureAwait(false);

        if (protocolSecret is null)
        {
            if (reportStatus)
            {
                SetStatus(accountId, AccountStatus.Error(AuthenticationFailedMessage));
                return null;
            }

            throw new InvalidOperationException(AuthenticationFailedMessage);
        }

        return protocolSecret;
    }

    private async Task UsingImapClientAsync(
        AccountImapEndpoint endpoint,
        string protocolSecret,
        Func<IImapClient, Task> action,
        CancellationToken cancellationToken)
    {
        await using var client = _imapClientFactory.Create();
        await client
            .ConnectAndAuthenticateAsync(
                endpoint.Host,
                endpoint.Port,
                endpoint.EmailAddress,
                protocolSecret,
                cancellationToken)
            .ConfigureAwait(false);
        await action(client).ConfigureAwait(false);
    }

    private async Task UsingAuthenticatedImapAsync(
        AccountImapEndpoint endpoint,
        string? secret,
        Func<IImapClient, Task> action,
        CancellationToken cancellationToken)
    {
        var protocolSecret = await ResolveImapSecretAsync(
                endpoint,
                secret,
                reportStatus: false,
                accountId: default,
                cancellationToken)
            .ConfigureAwait(false);
        await UsingImapClientAsync(endpoint, protocolSecret!, action, cancellationToken)
            .ConfigureAwait(false);
    }
}
