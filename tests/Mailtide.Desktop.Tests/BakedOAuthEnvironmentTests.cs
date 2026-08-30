using System.Reflection;
using Mailtide.Desktop.Host;

[assembly: AssemblyMetadata("MAILTIDE_GOOGLE_OAUTH_CLIENT_ID", "test-google-from-metadata.apps.googleusercontent.com")]
[assembly: AssemblyMetadata("MAILTIDE_MICROSOFT_OAUTH_CLIENT_ID", "test-microsoft-from-metadata")]
[assembly: AssemblyMetadata("UNRELATED_KEY", "ignored")]

namespace Mailtide.Desktop.Tests;

[TestClass]
[DoNotParallelize]
public sealed class BakedOAuthEnvironmentTests
{
    private const string GoogleEnv = "MAILTIDE_GOOGLE_OAUTH_CLIENT_ID";
    private const string MicrosoftEnv = "MAILTIDE_MICROSOFT_OAUTH_CLIENT_ID";

    [TestMethod]
    public void Apply_sets_process_env_when_unset()
    {
        using var _ = EnvScope.Clear(GoogleEnv, MicrosoftEnv);

        BakedOAuthEnvironment.ApplyToProcessEnvironment(
        [
            new(GoogleEnv, "baked-google-client.apps.googleusercontent.com"),
            new(MicrosoftEnv, "baked-microsoft-client-id"),
        ]);

        Assert.AreEqual(
            "baked-google-client.apps.googleusercontent.com",
            Environment.GetEnvironmentVariable(GoogleEnv));
        Assert.AreEqual(
            "baked-microsoft-client-id",
            Environment.GetEnvironmentVariable(MicrosoftEnv));

        var options = DesktopOAuthOptions.FromEnvironment();
        Assert.AreEqual("baked-google-client.apps.googleusercontent.com", options.GoogleClientId);
        Assert.AreEqual("baked-microsoft-client-id", options.MicrosoftClientId);
    }

    [TestMethod]
    public void Apply_does_not_overwrite_existing_process_env()
    {
        using var _ = EnvScope.Set(
            (GoogleEnv, "person-exported-google"),
            (MicrosoftEnv, "person-exported-microsoft"));

        BakedOAuthEnvironment.ApplyToProcessEnvironment(
        [
            new(GoogleEnv, "baked-google"),
            new(MicrosoftEnv, "baked-microsoft"),
        ]);

        Assert.AreEqual("person-exported-google", Environment.GetEnvironmentVariable(GoogleEnv));
        Assert.AreEqual("person-exported-microsoft", Environment.GetEnvironmentVariable(MicrosoftEnv));
    }

    [TestMethod]
    public void Apply_skips_blank_baked_values()
    {
        using var _ = EnvScope.Clear(GoogleEnv, MicrosoftEnv);

        BakedOAuthEnvironment.ApplyToProcessEnvironment(
        [
            new(GoogleEnv, "   "),
            new(MicrosoftEnv, ""),
        ]);

        Assert.IsTrue(string.IsNullOrEmpty(Environment.GetEnvironmentVariable(GoogleEnv)));
        Assert.IsTrue(string.IsNullOrEmpty(Environment.GetEnvironmentVariable(MicrosoftEnv)));
    }

    [TestMethod]
    public void Apply_ignores_unknown_keys()
    {
        using var _ = EnvScope.Clear(GoogleEnv, MicrosoftEnv);

        BakedOAuthEnvironment.ApplyToProcessEnvironment(
        [
            new("SOME_OTHER_SECRET", "should-not-apply"),
        ]);

        Assert.IsTrue(string.IsNullOrEmpty(Environment.GetEnvironmentVariable(GoogleEnv)));
        Assert.IsTrue(string.IsNullOrEmpty(Environment.GetEnvironmentVariable(MicrosoftEnv)));
        Assert.IsTrue(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SOME_OTHER_SECRET")));
    }

    [TestMethod]
    public void ReadBakedClientIds_reads_assembly_metadata_for_known_keys()
    {
        var ids = BakedOAuthEnvironment.ReadBakedClientIds(typeof(MarkerWithOAuthMetadata).Assembly).ToList();

        Assert.AreEqual(2, ids.Count);
        Assert.AreEqual(
            "test-google-from-metadata.apps.googleusercontent.com",
            ids.Single(p => p.Key == GoogleEnv).Value);
        Assert.AreEqual(
            "test-microsoft-from-metadata",
            ids.Single(p => p.Key == MicrosoftEnv).Value);
    }

    private sealed class MarkerWithOAuthMetadata;

    private sealed class EnvScope : IDisposable
    {
        private readonly (string Name, string? Previous)[] _previous;

        private EnvScope((string Name, string? Previous)[] previous) => _previous = previous;

        public static EnvScope Clear(params string[] names)
        {
            var previous = names.Select(n => (n, Environment.GetEnvironmentVariable(n))).ToArray();
            foreach (var name in names)
            {
                Environment.SetEnvironmentVariable(name, null);
            }

            return new EnvScope(previous);
        }

        public static EnvScope Set(params (string Name, string Value)[] values)
        {
            var previous = values.Select(v => (v.Name, Environment.GetEnvironmentVariable(v.Name))).ToArray();
            foreach (var (name, value) in values)
            {
                Environment.SetEnvironmentVariable(name, value);
            }

            return new EnvScope(previous);
        }

        public void Dispose()
        {
            foreach (var (name, previous) in _previous)
            {
                Environment.SetEnvironmentVariable(name, previous);
            }
        }
    }
}
