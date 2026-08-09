using Mailtide.Core.Auth;

namespace Mailtide.Android.Host;

/// <summary>
/// Public OAuth client IDs for Android system-browser flows.
/// Set via env: MAILTIDE_GOOGLE_OAUTH_CLIENT_ID, MAILTIDE_MICROSOFT_OAUTH_CLIENT_ID.
/// </summary>
public sealed class AndroidOAuthOptions
{
    public AndroidOAuthOptions(string? GoogleClientId, string? MicrosoftClientId)
    {
        this.GoogleClientId = GoogleClientId;
        this.MicrosoftClientId = MicrosoftClientId;
    }

    public string? GoogleClientId { get; }

    public string? MicrosoftClientId { get; }

    public static AndroidOAuthOptions FromEnvironment() =>
        new(
            Environment.GetEnvironmentVariable("MAILTIDE_GOOGLE_OAUTH_CLIENT_ID"),
            Environment.GetEnvironmentVariable("MAILTIDE_MICROSOFT_OAUTH_CLIENT_ID"));

    public string RequireClientId(OAuthProvider provider) =>
        provider switch
        {
            OAuthProvider.Google =>
                string.IsNullOrWhiteSpace(GoogleClientId)
                    ? throw new OAuthAuthenticationException(
                        "Google OAuth ClientId is not configured. Set MAILTIDE_GOOGLE_OAUTH_CLIENT_ID.")
                    : GoogleClientId,
            OAuthProvider.MicrosoftConsumer =>
                string.IsNullOrWhiteSpace(MicrosoftClientId)
                    ? throw new OAuthAuthenticationException(
                        "Microsoft OAuth ClientId is not configured. Set MAILTIDE_MICROSOFT_OAUTH_CLIENT_ID.")
                    : MicrosoftClientId,
            _ => throw new OAuthAuthenticationException(
                $"Unsupported OAuth provider '{provider}'."),
        };
}
