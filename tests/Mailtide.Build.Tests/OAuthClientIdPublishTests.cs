namespace Mailtide.Build.Tests;

[TestClass]
public sealed class OAuthClientIdPublishTests
{
    [TestMethod]
    public void MsBuildProperties_include_only_non_blank_env_values()
    {
        var properties = OAuthClientIdPublish.MsBuildPropertiesFromEnvironment(name => name switch
        {
            "MAILTIDE_GOOGLE_OAUTH_CLIENT_ID" => " google-placeholder.apps.googleusercontent.com ",
            "MAILTIDE_MICROSOFT_OAUTH_CLIENT_ID" => "microsoft-placeholder",
            _ => null,
        });

        Assert.AreEqual(2, properties.Count);
        Assert.AreEqual(
            "google-placeholder.apps.googleusercontent.com",
            properties["MAILTIDE_GOOGLE_OAUTH_CLIENT_ID"]);
        Assert.AreEqual("microsoft-placeholder", properties["MAILTIDE_MICROSOFT_OAUTH_CLIENT_ID"]);
    }

    [TestMethod]
    public void MsBuildProperties_omit_unset_or_blank_secrets()
    {
        var properties = OAuthClientIdPublish.MsBuildPropertiesFromEnvironment(name => name switch
        {
            "MAILTIDE_GOOGLE_OAUTH_CLIENT_ID" => "   ",
            "MAILTIDE_MICROSOFT_OAUTH_CLIENT_ID" => null,
            _ => "unexpected",
        });

        Assert.AreEqual(0, properties.Count);
    }

    [TestMethod]
    public void MsBuildProperties_can_pass_only_one_provider()
    {
        var properties = OAuthClientIdPublish.MsBuildPropertiesFromEnvironment(name => name switch
        {
            "MAILTIDE_GOOGLE_OAUTH_CLIENT_ID" => "only-google.apps.googleusercontent.com",
            _ => null,
        });

        Assert.AreEqual(1, properties.Count);
        Assert.IsTrue(properties.ContainsKey("MAILTIDE_GOOGLE_OAUTH_CLIENT_ID"));
        Assert.IsFalse(properties.ContainsKey("MAILTIDE_MICROSOFT_OAUTH_CLIENT_ID"));
    }
}
