using System.Reflection;
using Mailtide.Core.Updates;

namespace Mailtide.Desktop.Host;

/// <summary>
/// Desktop-only coordinator: fetch latest Release, compare to this install's version.
/// </summary>
public sealed class DesktopUpdateCoordinator
{
    private readonly IReleaseUpdateSource _source;
    private readonly Func<string> _currentVersion;

    public DesktopUpdateCoordinator(
        IReleaseUpdateSource source,
        Func<string>? currentVersion = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _currentVersion = currentVersion ?? ReadEntryInformationalVersion;
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var current = _currentVersion();
        try
        {
            var remote = await _source.GetLatestAsync(cancellationToken).ConfigureAwait(false);
            return UpdateChecker.Evaluate(current, remote);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.Unavailable,
                ReleaseVersion.Normalize(current));
        }
    }

    public static string ReadEntryInformationalVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Strip any "+gitsha" build metadata NuGet/SDK may append.
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString() ?? ReleaseVersion.LocalFallback;
    }
}
