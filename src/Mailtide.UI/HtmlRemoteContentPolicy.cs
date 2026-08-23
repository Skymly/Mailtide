namespace Mailtide.UI;

/// <summary>
/// Decides which URIs stored HTML may load in NativeWebView.
/// Remote network schemes are denied so rendering a Message cannot phone home.
/// </summary>
public static class HtmlRemoteContentPolicy
{
    public const string OfflineContentSecurityPolicy =
        "default-src 'none'; img-src data:; style-src 'unsafe-inline'; font-src data:; media-src data:";

    public static bool IsAllowed(Uri? uri)
    {
        if (uri is null)
        {
            return false;
        }

        return uri.Scheme.Equals(Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase) is false
            && (uri.Scheme.Equals("about", StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals("data", StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals("blob", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsAllowed(string? uriString) =>
        Uri.TryCreate(uriString, UriKind.Absolute, out var uri) && IsAllowed(uri);

    public static string WrapForOfflineRender(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        return
            "<!DOCTYPE html><html><head><meta http-equiv=\"Content-Security-Policy\" content=\""
            + OfflineContentSecurityPolicy
            + "\"></head><body>"
            + html
            + "</body></html>";
    }
}
