using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using Mailtide.Core.Updates;

namespace Mailtide.Desktop.Host;

public enum DesktopReleasePlatform
{
    WindowsX64 = 0,
    LinuxX64 = 1,
}

/// <summary>
/// Fetches the latest GitHub Release and selects the Desktop asset for this platform.
/// </summary>
public sealed class GitHubReleasesUpdateSource : IReleaseUpdateSource, IDisposable
{
    public const string DefaultApiUrl =
        "https://api.github.com/repos/Skymly/Mailtide/releases/latest";

    public const string UserAgent = "Mailtide";

    private readonly DesktopReleasePlatform _platform;
    private readonly HttpClient _http;
    private readonly Uri _apiUrl;

    public GitHubReleasesUpdateSource(
        DesktopReleasePlatform platform,
        HttpMessageHandler? handler = null,
        string? apiUrl = null)
    {
        _platform = platform;
        _apiUrl = new Uri(apiUrl ?? DefaultApiUrl);
        _http = handler is null
            ? new HttpClient()
            : new HttpClient(handler, disposeHandler: false);
        _http.DefaultRequestHeaders.UserAgent.Clear();
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(UserAgent, "1"));
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public static DesktopReleasePlatform DetectPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return DesktopReleasePlatform.WindowsX64;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return DesktopReleasePlatform.LinuxX64;
        }

        throw new PlatformNotSupportedException(
            "Desktop self-update is only supported on Windows and Linux.");
    }

    public async Task<RemoteReleaseInfo?> GetLatestAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http
                .GetAsync(_apiUrl, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var document = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return ParseRelease(document.RootElement, _platform);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
        {
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return null;
        }
    }

    public void Dispose() => _http.Dispose();

    internal static RemoteReleaseInfo? ParseRelease(
        JsonElement root,
        DesktopReleasePlatform platform)
    {
        if (!root.TryGetProperty("tag_name", out var tagElement))
        {
            return null;
        }

        var tagName = tagElement.GetString();
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return null;
        }

        var htmlUrl = root.TryGetProperty("html_url", out var htmlElement)
            ? htmlElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(htmlUrl))
        {
            return null;
        }

        ReleaseAsset? asset = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in assets.EnumerateArray())
            {
                var name = item.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString()
                    : null;
                var url = item.TryGetProperty("browser_download_url", out var urlElement)
                    ? urlElement.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                if (MatchesPlatform(name, platform))
                {
                    asset = new ReleaseAsset(name, url);
                    break;
                }
            }
        }

        return new RemoteReleaseInfo(tagName, htmlUrl, asset);
    }

    internal static bool MatchesPlatform(string assetName, DesktopReleasePlatform platform) =>
        platform switch
        {
            DesktopReleasePlatform.WindowsX64 =>
                assetName.EndsWith("-win-x64-setup.exe", StringComparison.OrdinalIgnoreCase),
            DesktopReleasePlatform.LinuxX64 =>
                assetName.EndsWith("-linux-x64.AppImage", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
}
