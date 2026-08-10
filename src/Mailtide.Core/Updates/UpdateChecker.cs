namespace Mailtide.Core.Updates;

public sealed record ReleaseAsset(string Name, string DownloadUrl);

/// <summary>
/// Latest GitHub Release metadata after the Host selected a platform asset (if any).
/// </summary>
public sealed record RemoteReleaseInfo(
    string TagName,
    string HtmlUrl,
    ReleaseAsset? PlatformAsset);

public enum UpdateCheckStatus
{
    UpToDate = 0,
    UpdateAvailable = 1,
    Unavailable = 2,
}

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    string CurrentVersion,
    RemoteReleaseInfo? Remote = null);

/// <summary>
/// Pure version comparison against a remote Release. No network I/O.
/// </summary>
public static class UpdateChecker
{
    public static UpdateCheckResult Evaluate(string? currentVersion, RemoteReleaseInfo? remote)
    {
        var current = ReleaseVersion.Normalize(currentVersion);
        if (remote is null)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Unavailable, current);
        }

        if (ReleaseVersion.Compare(current, remote.TagName) >= 0)
        {
            return new UpdateCheckResult(UpdateCheckStatus.UpToDate, current, remote);
        }

        return new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, current, remote);
    }
}
