#nullable enable

static class ReleaseArtifacts
{
    public const string LocalFallbackVersion = "0.0.0-local";

    public static string NormalizeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return LocalFallbackVersion;
        }

        var trimmed = version.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        return string.IsNullOrWhiteSpace(trimmed) ? LocalFallbackVersion : trimmed;
    }

    public static string WindowsInstallerFileName(string version) =>
        $"Mailtide-{NormalizeVersion(version)}-win-x64-setup.exe";

    public static string AppImageFileName(string version) =>
        $"Mailtide-{NormalizeVersion(version)}-linux-x64.AppImage";

    public static string AndroidApkFileName(string version) =>
        $"Mailtide-{NormalizeVersion(version)}-android.apk";

    public static int AndroidVersionCode(string version)
    {
        var normalized = NormalizeVersion(version);
        var core = normalized.Split('-', 2)[0];
        var parts = core.Split('.');

        static int ParsePart(string[] values, int index)
        {
            if (index >= values.Length)
            {
                return 0;
            }

            return int.TryParse(values[index], out var n) ? System.Math.Max(0, n) : 0;
        }

        var major = ParsePart(parts, 0);
        var minor = ParsePart(parts, 1);
        var patch = ParsePart(parts, 2);
        var code = major * 10_000 + minor * 100 + patch;
        return System.Math.Max(1, code);
    }
}
