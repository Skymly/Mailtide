namespace Mailtide.Core.Auth;

public enum OAuthProvider
{
    Google = 0,
    MicrosoftConsumer = 1,
}

public sealed record OAuthAuthorizeRequest(OAuthProvider Provider);

public sealed record OAuthTokenMetadata(
    OAuthProvider Provider,
    string Authority,
    string ClientId);

/// <summary>
/// Result of an OAuth authorize dance. <see cref="RefreshSecret"/> is the Account Credential.
/// </summary>
public sealed record OAuthAuthorizationResult(
    string EmailAddress,
    string RefreshSecret,
    OAuthTokenMetadata Metadata);

public sealed record OAuthRefreshRequest(
    string RefreshSecret,
    OAuthTokenMetadata Metadata);

public sealed record OAuthAccessTokenResult(string AccessToken);

/// <summary>
/// Host/test-provided OAuth port (authorize via system browser + token refresh).
/// </summary>
public interface IOAuthClient
{
    Task<OAuthAuthorizationResult> AuthorizeAsync(
        OAuthAuthorizeRequest request,
        CancellationToken cancellationToken = default);

    Task<OAuthAccessTokenResult> RefreshAsync(
        OAuthRefreshRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Thrown when the IdP rejects authorize/refresh; surfaces as Account error suggesting sign-in again.
/// </summary>
public sealed class OAuthAuthenticationException : Exception
{
    public OAuthAuthenticationException(string message)
        : base(message)
    {
    }

    public OAuthAuthenticationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
