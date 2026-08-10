namespace Mailtide.Core.Updates;

/// <summary>
/// Normalized release version helpers aligned with Nuke <c>ReleaseArtifacts.NormalizeVersion</c>.
/// </summary>
public static class ReleaseVersion
{
    public const string LocalFallback = "0.0.0-local";

    public static string Normalize(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return LocalFallback;
        }

        var trimmed = version.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        return string.IsNullOrWhiteSpace(trimmed) ? LocalFallback : trimmed;
    }

    /// <summary>
    /// Compares two version strings after normalization. Returns negative if
    /// <paramref name="left"/> is older, zero if equal, positive if newer.
    /// </summary>
    public static int Compare(string? left, string? right)
    {
        var leftParts = Parse(Normalize(left));
        var rightParts = Parse(Normalize(right));

        var core = leftParts.Core.CompareTo(rightParts.Core);
        if (core != 0)
        {
            return core;
        }

        // No pre-release segment sorts after a pre-release (1.0.0 > 1.0.0-beta).
        var leftPre = leftParts.PreRelease;
        var rightPre = rightParts.PreRelease;
        if (leftPre is null && rightPre is null)
        {
            return 0;
        }

        if (leftPre is null)
        {
            return 1;
        }

        if (rightPre is null)
        {
            return -1;
        }

        return string.CompareOrdinal(leftPre, rightPre);
    }

    private static (Version Core, string? PreRelease) Parse(string normalized)
    {
        var dash = normalized.IndexOf('-');
        var coreText = dash >= 0 ? normalized[..dash] : normalized;
        var pre = dash >= 0 ? normalized[(dash + 1)..] : null;

        var segments = coreText.Split('.');
        var major = ParseSegment(segments, 0);
        var minor = ParseSegment(segments, 1);
        var patch = ParseSegment(segments, 2);
        return (new Version(major, minor, patch), pre);
    }

    private static int ParseSegment(string[] segments, int index)
    {
        if (index >= segments.Length)
        {
            return 0;
        }

        return int.TryParse(segments[index], out var n) ? Math.Max(0, n) : 0;
    }
}
