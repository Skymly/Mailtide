namespace Mailtide.Core.Auth;

/// <summary>
/// Placeholder until Desktop wires a real system-browser OAuth client (#16).
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
