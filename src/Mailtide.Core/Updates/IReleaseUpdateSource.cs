namespace Mailtide.Core.Updates;

/// <summary>
/// Host-provided port that fetches the latest GitHub Release for this install channel.
/// </summary>
public interface IReleaseUpdateSource
{
    Task<RemoteReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Host-provided port that opens the Person's update flow (download URL or release page).
/// </summary>
public interface IUpdateLauncher
{
    Task OpenAsync(UpdateCheckResult update, CancellationToken cancellationToken = default);
}
