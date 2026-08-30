using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Mailtide.Desktop.Host;

/// <summary>
/// Applies Release-baked public OAuth client IDs into the process environment
/// so <see cref="DesktopOAuthOptions.FromEnvironment"/> can read them without
/// the Person exporting env vars. Process env always wins over baked values.
/// </summary>
internal static class BakedOAuthEnvironment
{
    internal const string GoogleClientIdKey = "MAILTIDE_GOOGLE_OAUTH_CLIENT_ID";
    internal const string MicrosoftClientIdKey = "MAILTIDE_MICROSOFT_OAUTH_CLIENT_ID";

    internal static void ApplyToProcessEnvironment() =>
        ApplyToProcessEnvironment(ReadBakedClientIds(typeof(BakedOAuthEnvironment).Assembly));

    internal static void ApplyToProcessEnvironment(
        IEnumerable<KeyValuePair<string, string>> bakedClientIds)
    {
        foreach (var pair in bakedClientIds)
        {
            if (!IsKnownClientIdKey(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(pair.Key)))
            {
                continue;
            }

            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }

    internal static IEnumerable<KeyValuePair<string, string>> ReadBakedClientIds(Assembly assembly)
    {
        return assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(attribute => IsKnownClientIdKey(attribute.Key))
            .Where(attribute => !string.IsNullOrWhiteSpace(attribute.Value))
            .Select(attribute => new KeyValuePair<string, string>(attribute.Key, attribute.Value!));
    }

    private static bool IsKnownClientIdKey(string key) =>
        key is GoogleClientIdKey or MicrosoftClientIdKey;
}
