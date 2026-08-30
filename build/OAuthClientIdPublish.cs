#nullable enable
using System;
using System.Collections.Generic;

static class OAuthClientIdPublish
{
    public const string GoogleClientIdProperty = "MAILTIDE_GOOGLE_OAUTH_CLIENT_ID";
    public const string MicrosoftClientIdProperty = "MAILTIDE_MICROSOFT_OAUTH_CLIENT_ID";

    /// <summary>
    /// MSBuild properties to bake public OAuth client IDs into publish outputs.
    /// Blank / unset env values are omitted so Release packs still succeed.
    /// </summary>
    public static IReadOnlyDictionary<string, string> MsBuildPropertiesFromEnvironment(
        Func<string, string?>? getEnvironmentVariable = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        AddIfPresent(properties, GoogleClientIdProperty, getEnvironmentVariable(GoogleClientIdProperty));
        AddIfPresent(properties, MicrosoftClientIdProperty, getEnvironmentVariable(MicrosoftClientIdProperty));
        return properties;
    }

    static void AddIfPresent(IDictionary<string, string> properties, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        properties[name] = value.Trim();
    }
}
