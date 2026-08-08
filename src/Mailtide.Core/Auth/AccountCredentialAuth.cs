using Mailtide.Core.Security;

namespace Mailtide.Core.Auth;

/// <summary>
/// Owns OAuth Credential obtain / refresh / invalidate for Accounts.
/// Sync and send only consume short-lived access tokens from this type.
/// </summary>
internal sealed class AccountCredentialAuth
{
    private readonly IOAuthClient _oauthClient;
    private readonly ISecureStorage _secureStorage;

    public AccountCredentialAuth(IOAuthClient oauthClient, ISecureStorage secureStorage)
    {
        _oauthClient = oauthClient;
        _secureStorage = secureStorage;
    }

    public async Task<OAuthAuthorizationResult> ObtainAsync(
        OAuthProvider provider,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _oauthClient
                .AuthorizeAsync(new OAuthAuthorizeRequest(provider), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OAuthAuthenticationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not NotSupportedException)
        {
            throw new OAuthAuthenticationException(
                "OAuth authorization failed.",
                ex);
        }
    }

    public async Task StoreRefreshSecretAsync(
        string credentialHandle,
        string refreshSecret,
        CancellationToken cancellationToken) =>
        await _secureStorage
            .StoreSecretAsync(credentialHandle, refreshSecret, cancellationToken)
            .ConfigureAwait(false);

    public async Task DeleteCredentialSecretAsync(
        string credentialHandle,
        CancellationToken cancellationToken) =>
        await _secureStorage
            .DeleteSecretAsync(credentialHandle, cancellationToken)
            .ConfigureAwait(false);

    public Task<string?> RetrieveCredentialSecretAsync(
        string credentialHandle,
        CancellationToken cancellationToken) =>
        _secureStorage.RetrieveSecretAsync(credentialHandle, cancellationToken);

    /// <summary>
    /// Returns a usable access token, or null when the OAuth Credential is invalid
    /// (caller should surface re-sign-in). Non-auth failures propagate.
    /// </summary>
    public async Task<string?> GetAccessTokenAsync(
        OAuthTokenMetadata metadata,
        string refreshSecret,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _oauthClient
                .RefreshAsync(new OAuthRefreshRequest(refreshSecret, metadata), cancellationToken)
                .ConfigureAwait(false);

            return string.IsNullOrWhiteSpace(result.AccessToken) ? null : result.AccessToken;
        }
        catch (OAuthAuthenticationException)
        {
            return null;
        }
    }

    public Task InvalidateAsync(string credentialHandle, CancellationToken cancellationToken) =>
        DeleteCredentialSecretAsync(credentialHandle, cancellationToken);
}
