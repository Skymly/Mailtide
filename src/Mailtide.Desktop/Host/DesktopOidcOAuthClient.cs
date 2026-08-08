using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using Duende.IdentityModel.OidcClient;
using Duende.IdentityModel.OidcClient.Browser;
using Mailtide.Core;
using Mailtide.Core.Auth;

namespace Mailtide.Desktop.Host;

/// <summary>
/// Desktop Host OAuth client: system browser + loopback redirect via Duende OidcClient.
/// </summary>
public sealed class DesktopOidcOAuthClient : IOAuthClient
{
    public const string GoogleScope = "openid email https://mail.google.com/";

    public const string MicrosoftScope =
        "openid email offline_access https://outlook.office.com/IMAP.AccessAsUser.All https://outlook.office.com/SMTP.Send";

    private readonly DesktopOAuthOptions _options;
    private readonly IBrowser _browser;
    private readonly HttpMessageHandler? _backchannelHandler;

    public DesktopOidcOAuthClient(
        DesktopOAuthOptions options,
        IBrowser? browser = null,
        HttpMessageHandler? backchannelHandler = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _browser = browser ?? new LoopbackSystemBrowser();
        _backchannelHandler = backchannelHandler;
    }

    public async Task<OAuthAuthorizationResult> AuthorizeAsync(
        OAuthAuthorizeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var clientId = _options.RequireClientId(request.Provider);
            var oidc = CreateOidcClient(request.Provider, clientId);
            var login = await oidc
                .LoginAsync(new LoginRequest(), cancellationToken)
                .ConfigureAwait(false);

            if (login.IsError)
            {
                throw new OAuthAuthenticationException(
                    login.ErrorDescription ?? login.Error ?? "OAuth authorization failed.");
            }

            if (string.IsNullOrWhiteSpace(login.RefreshToken))
            {
                throw new OAuthAuthenticationException(
                    "OAuth authorization did not return a refresh Credential.");
            }

            var email = ResolveEmail(login.User);
            if (string.IsNullOrWhiteSpace(email))
            {
                throw new OAuthAuthenticationException(
                    "OAuth authorization did not return an email address.");
            }

            return new OAuthAuthorizationResult(
                EmailAddress: email,
                RefreshSecret: login.RefreshToken,
                Metadata: new OAuthTokenMetadata(
                    request.Provider,
                    MetadataAuthority(request.Provider),
                    clientId));
        }
        catch (OAuthAuthenticationException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new OAuthAuthenticationException("OAuth authorization failed.", ex);
        }
    }

    public async Task<OAuthAccessTokenResult> RefreshAsync(
        OAuthRefreshRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Metadata);

        try
        {
            var clientId = string.IsNullOrWhiteSpace(request.Metadata.ClientId)
                ? _options.RequireClientId(request.Metadata.Provider)
                : request.Metadata.ClientId;

            var oidc = CreateOidcClient(request.Metadata.Provider, clientId);
            var refreshed = await oidc
                .RefreshTokenAsync(request.RefreshSecret, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (refreshed.IsError)
            {
                throw new OAuthAuthenticationException(
                    refreshed.ErrorDescription ?? refreshed.Error ?? "OAuth refresh failed.");
            }

            if (string.IsNullOrWhiteSpace(refreshed.AccessToken))
            {
                throw new OAuthAuthenticationException("OAuth refresh did not return an access token.");
            }

            return new OAuthAccessTokenResult(refreshed.AccessToken);
        }
        catch (OAuthAuthenticationException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new OAuthAuthenticationException("OAuth refresh failed.", ex);
        }
    }

    private OidcClient CreateOidcClient(OAuthProvider provider, string clientId)
    {
        var redirectUri = _browser is LoopbackSystemBrowser loopback
            ? loopback.RedirectUri
            : $"http://127.0.0.1:{GetFreeTcpPort()}/";

        var options = new OidcClientOptions
        {
            Authority = DiscoveryAuthority(provider),
            ClientId = clientId,
            Scope = ScopeFor(provider),
            RedirectUri = redirectUri,
            Browser = _browser,
            LoadProfile = false,
        };

        // Google token host and Microsoft authorize path sit outside the authority base URL.
        options.Policy.Discovery.ValidateEndpoints = false;
        options.Policy.Discovery.AdditionalEndpointBaseAddresses.Add("https://oauth2.googleapis.com");
        options.Policy.Discovery.AdditionalEndpointBaseAddresses.Add(
            "https://login.microsoftonline.com/consumers");
        options.Policy.Discovery.AdditionalEndpointBaseAddresses.Add(
            "https://login.microsoftonline.com");

        if (_backchannelHandler is not null)
        {
            // Test seam: scripted discovery/token HTTP uses unsigned JWTs.
            options.BackchannelHandler = _backchannelHandler;
            options.Policy.Discovery.RequireHttps = false;
            options.IdentityTokenValidator = new NoValidationIdentityTokenValidator();
        }

        return new OidcClient(options);
    }

    private static string DiscoveryAuthority(OAuthProvider provider) =>
        provider switch
        {
            OAuthProvider.Google => GoogleMailPreset.Authority,
            OAuthProvider.MicrosoftConsumer => $"{MicrosoftConsumerMailPreset.Authority}/v2.0",
            _ => throw new OAuthAuthenticationException($"Unsupported OAuth provider '{provider}'."),
        };

    private static string MetadataAuthority(OAuthProvider provider) =>
        provider switch
        {
            OAuthProvider.Google => GoogleMailPreset.Authority,
            OAuthProvider.MicrosoftConsumer => MicrosoftConsumerMailPreset.Authority,
            _ => throw new OAuthAuthenticationException($"Unsupported OAuth provider '{provider}'."),
        };

    private static string ScopeFor(OAuthProvider provider) =>
        provider switch
        {
            OAuthProvider.Google => GoogleScope,
            OAuthProvider.MicrosoftConsumer => MicrosoftScope,
            _ => throw new OAuthAuthenticationException($"Unsupported OAuth provider '{provider}'."),
        };

    private static string? ResolveEmail(ClaimsPrincipal? user)
    {
        if (user is null)
        {
            return null;
        }

        return user.FindFirst("email")?.Value
            ?? user.FindFirst(ClaimTypes.Email)?.Value
            ?? user.FindFirst("preferred_username")?.Value;
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
