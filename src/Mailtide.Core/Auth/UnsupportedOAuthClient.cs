namespace Mailtide.Core.Auth;

/// <summary>
/// Placeholder Host OAuth client for environments that have not wired system-browser OAuth yet.
/// </summary>
public sealed class UnsupportedOAuthClient : IOAuthClient
{
    public Task<OAuthAuthorizationResult> AuthorizeAsync(
        OAuthAuthorizeRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "OAuth browser authorization is not wired on this host yet.");

    public Task<OAuthAccessTokenResult> RefreshAsync(
        OAuthRefreshRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "OAuth token refresh is not wired on this host yet.");
}
