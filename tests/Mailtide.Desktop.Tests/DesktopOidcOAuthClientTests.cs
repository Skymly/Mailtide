using System.Collections.Specialized;
using System.Net;
using System.Text;
using System.Text.Json;
using Duende.IdentityModel.OidcClient.Browser;
using Mailtide.Core;
using Mailtide.Core.Auth;
using Mailtide.Desktop.Host;

namespace Mailtide.Desktop.Tests;

[TestClass]
public sealed class DesktopOidcOAuthClientTests
{
    [TestMethod]
    public async Task AuthorizeAsync_Google_returns_email_refresh_and_preset_authority()
    {
        var browser = new ScriptedBrowser();
        using var handler = new ScriptedOidcBackchannel(
            issuer: "https://accounts.google.com",
            authorizeEndpoint: "https://accounts.google.com/o/oauth2/v2/auth",
            tokenEndpoint: "https://oauth2.googleapis.com/token",
            email: "alice@gmail.com",
            refreshToken: "google-refresh-secret");

        var client = new DesktopOidcOAuthClient(
            new DesktopOAuthOptions(GoogleClientId: "test-google-client", MicrosoftClientId: "unused"),
            browser,
            handler);

        var result = await client.AuthorizeAsync(new OAuthAuthorizeRequest(OAuthProvider.Google));

        Assert.AreEqual("alice@gmail.com", result.EmailAddress);
        Assert.AreEqual("google-refresh-secret", result.RefreshSecret);
        Assert.AreEqual(OAuthProvider.Google, result.Metadata.Provider);
        Assert.AreEqual(GoogleMailPreset.Authority, result.Metadata.Authority);
        Assert.AreEqual("test-google-client", result.Metadata.ClientId);
        Assert.IsTrue(browser.WasInvoked);
        var authorizeQuery = ParseQuery(new Uri(browser.LastStartUrl!).Query);
        Assert.AreEqual("offline", authorizeQuery.Get("access_type"));
        Assert.AreEqual("consent", authorizeQuery.Get("prompt"));
    }

    [TestMethod]
    public async Task AuthorizeAsync_Microsoft_returns_consumers_authority_not_organizations()
    {
        var browser = new ScriptedBrowser();
        using var handler = new ScriptedOidcBackchannel(
            issuer: "https://login.microsoftonline.com/consumers/v2.0",
            authorizeEndpoint: "https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize",
            tokenEndpoint: "https://login.microsoftonline.com/consumers/oauth2/v2.0/token",
            email: "bob@outlook.com",
            refreshToken: "ms-refresh-secret");

        var client = new DesktopOidcOAuthClient(
            new DesktopOAuthOptions(GoogleClientId: "unused", MicrosoftClientId: "test-ms-client"),
            browser,
            handler);

        var result = await client.AuthorizeAsync(
            new OAuthAuthorizeRequest(OAuthProvider.MicrosoftConsumer));

        Assert.AreEqual("bob@outlook.com", result.EmailAddress);
        Assert.AreEqual("ms-refresh-secret", result.RefreshSecret);
        Assert.AreEqual(OAuthProvider.MicrosoftConsumer, result.Metadata.Provider);
        Assert.AreEqual(MicrosoftConsumerMailPreset.Authority, result.Metadata.Authority);
        StringAssert.Contains(result.Metadata.Authority, "/consumers");
        Assert.DoesNotContain("/organizations", result.Metadata.Authority, StringComparison.Ordinal);
        Assert.DoesNotContain("/common", result.Metadata.Authority, StringComparison.Ordinal);
        Assert.AreEqual("test-ms-client", result.Metadata.ClientId);
    }

    [TestMethod]
    public async Task AuthorizeAsync_missing_ClientId_fails_before_opening_browser()
    {
        var browser = new ScriptedBrowser();
        var client = new DesktopOidcOAuthClient(
            new DesktopOAuthOptions(GoogleClientId: null, MicrosoftClientId: null),
            browser);

        await Assert.ThrowsExactlyAsync<OAuthAuthenticationException>(
            () => client.AuthorizeAsync(new OAuthAuthorizeRequest(OAuthProvider.Google)));

        Assert.IsFalse(browser.WasInvoked);
    }

    [TestMethod]
    public async Task RefreshAsync_returns_access_token()
    {
        using var handler = new ScriptedOidcBackchannel(
            issuer: "https://accounts.google.com",
            authorizeEndpoint: "https://accounts.google.com/o/oauth2/v2/auth",
            tokenEndpoint: "https://oauth2.googleapis.com/token",
            email: "alice@gmail.com",
            refreshToken: "google-refresh-secret",
            accessTokenOnRefresh: "google-access-token");

        var client = new DesktopOidcOAuthClient(
            new DesktopOAuthOptions(GoogleClientId: "test-google-client", MicrosoftClientId: "unused"),
            new ScriptedBrowser(),
            handler);

        var result = await client.RefreshAsync(
            new OAuthRefreshRequest(
                "google-refresh-secret",
                new OAuthTokenMetadata(
                    OAuthProvider.Google,
                    GoogleMailPreset.Authority,
                    "test-google-client")));

        Assert.AreEqual("google-access-token", result.AccessToken);
    }

    [TestMethod]
    public async Task RefreshAsync_IdP_rejection_throws_OAuthAuthenticationException()
    {
        using var handler = new ScriptedOidcBackchannel(
            issuer: "https://accounts.google.com",
            authorizeEndpoint: "https://accounts.google.com/o/oauth2/v2/auth",
            tokenEndpoint: "https://oauth2.googleapis.com/token",
            email: "alice@gmail.com",
            refreshToken: "google-refresh-secret",
            refreshFail: true);

        var client = new DesktopOidcOAuthClient(
            new DesktopOAuthOptions(GoogleClientId: "test-google-client", MicrosoftClientId: "unused"),
            new ScriptedBrowser(),
            handler);

        await Assert.ThrowsExactlyAsync<OAuthAuthenticationException>(
            () => client.RefreshAsync(
                new OAuthRefreshRequest(
                    "google-refresh-secret",
                    new OAuthTokenMetadata(
                        OAuthProvider.Google,
                        GoogleMailPreset.Authority,
                        "test-google-client"))));
    }

    private sealed class ScriptedBrowser : IBrowser
    {
        public bool WasInvoked { get; private set; }

        public string? LastStartUrl { get; private set; }

        public Task<BrowserResult> InvokeAsync(
            BrowserOptions options,
            CancellationToken cancellationToken = default)
        {
            WasInvoked = true;
            LastStartUrl = options.StartUrl;
            var start = new Uri(options.StartUrl);
            var state = ParseQuery(start.Query).Get("state")
                ?? throw new InvalidOperationException("Authorize URL missing state.");
            var redirect = $"{options.EndUrl.TrimEnd('/')}/?code=test-auth-code&state={Uri.EscapeDataString(state)}";
            return Task.FromResult(
                new BrowserResult
                {
                    ResultType = BrowserResultType.Success,
                    Response = redirect,
                });
        }
    }

    private static NameValueCollection ParseQuery(string query)
    {
        var result = new NameValueCollection();
        var q = query.StartsWith('?') ? query[1..] : query;
        foreach (var pair in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0].Replace('+', ' '));
            var value = parts.Length > 1
                ? Uri.UnescapeDataString(parts[1].Replace('+', ' '))
                : string.Empty;
            result[key] = value;
        }

        return result;
    }

    private sealed class ScriptedOidcBackchannel : HttpMessageHandler
    {
        private readonly string _issuer;
        private readonly string _authorizeEndpoint;
        private readonly string _tokenEndpoint;
        private readonly string _email;
        private readonly string _refreshToken;
        private readonly string? _accessTokenOnRefresh;
        private readonly bool _refreshFail;

        public ScriptedOidcBackchannel(
            string issuer,
            string authorizeEndpoint,
            string tokenEndpoint,
            string email,
            string refreshToken,
            string? accessTokenOnRefresh = null,
            bool refreshFail = false)
        {
            _issuer = issuer;
            _authorizeEndpoint = authorizeEndpoint;
            _tokenEndpoint = tokenEndpoint;
            _email = email;
            _refreshToken = refreshToken;
            _accessTokenOnRefresh = accessTokenOnRefresh;
            _refreshFail = refreshFail;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains(".well-known/openid-configuration", StringComparison.Ordinal))
            {
                return Json(
                    new
                    {
                        issuer = _issuer,
                        authorization_endpoint = _authorizeEndpoint,
                        token_endpoint = _tokenEndpoint,
                        jwks_uri = $"{_issuer.TrimEnd('/')}/jwks",
                        userinfo_endpoint = $"{_issuer.TrimEnd('/')}/userinfo",
                        id_token_signing_alg_values_supported = new[] { "RS256" },
                        response_types_supported = new[] { "code" },
                        subject_types_supported = new[] { "public" },
                    });
            }

            if (path.EndsWith("/jwks", StringComparison.Ordinal))
            {
                return Json(new { keys = Array.Empty<object>() });
            }

            if (string.Equals(
                    request.RequestUri.GetLeftPart(UriPartial.Path).TrimEnd('/'),
                    _tokenEndpoint.TrimEnd('/'),
                    StringComparison.OrdinalIgnoreCase)
                || path.Contains("/token", StringComparison.Ordinal))
            {
                var body = request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var form = ParseQuery(body.StartsWith('?') ? body : "?" + body);
                if (string.Equals(form.Get("grant_type"), "refresh_token", StringComparison.Ordinal))
                {
                    if (_refreshFail)
                    {
                        return Json(
                            new { error = "invalid_grant", error_description = "refresh rejected" },
                            HttpStatusCode.BadRequest);
                    }

                    return Json(
                        new
                        {
                            access_token = _accessTokenOnRefresh ?? "refreshed-access",
                            token_type = "Bearer",
                            expires_in = 3600,
                        });
                }

                var idToken = CreateUnsignedJwt(
                    new Dictionary<string, object>
                    {
                        ["iss"] = _issuer,
                        ["sub"] = "subject-1",
                        ["aud"] = form.Get("client_id") ?? "client",
                        ["exp"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
                        ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        ["email"] = _email,
                    });

                return Json(
                    new
                    {
                        access_token = "test-access-token",
                        refresh_token = _refreshToken,
                        id_token = idToken,
                        token_type = "Bearer",
                        expires_in = 3600,
                    });
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"Unhandled: {request.RequestUri}"),
            };
        }

        private static HttpResponseMessage Json(object payload, HttpStatusCode status = HttpStatusCode.OK)
        {
            var json = JsonSerializer.Serialize(payload);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }

        private static string CreateUnsignedJwt(Dictionary<string, object> payload)
        {
            static string B64(byte[] bytes) =>
                Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

            var header = B64(Encoding.UTF8.GetBytes("""{"alg":"none","typ":"JWT"}"""));
            var body = B64(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));
            return $"{header}.{body}.";
        }
    }
}
