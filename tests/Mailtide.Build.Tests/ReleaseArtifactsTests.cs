namespace Mailtide.Build.Tests;

[TestClass]
public sealed class ReleaseArtifactsTests
{
    [TestMethod]
    public void NormalizeVersion_strips_leading_v_from_tag()
    {
        Assert.AreEqual("1.2.3", ReleaseArtifacts.NormalizeVersion("v1.2.3"));
        Assert.AreEqual("1.2.3", ReleaseArtifacts.NormalizeVersion("V1.2.3"));
    }

    [TestMethod]
    public void NormalizeVersion_defaults_local_when_missing()
    {
        Assert.AreEqual("0.0.0-local", ReleaseArtifacts.NormalizeVersion(null));
        Assert.AreEqual("0.0.0-local", ReleaseArtifacts.NormalizeVersion(""));
        Assert.AreEqual("0.0.0-local", ReleaseArtifacts.NormalizeVersion("   "));
    }

    [TestMethod]
    public void NormalizeVersion_keeps_plain_semver()
    {
        Assert.AreEqual("0.1.0", ReleaseArtifacts.NormalizeVersion("0.1.0"));
    }

    [TestMethod]
    public void Artifact_names_follow_stable_Release_pattern()
    {
        const string version = "1.2.3";

        Assert.AreEqual(
            "Mailtide-1.2.3-win-x64-setup.exe",
            ReleaseArtifacts.WindowsInstallerFileName(version));
        Assert.AreEqual(
            "Mailtide-1.2.3-linux-x64.AppImage",
            ReleaseArtifacts.AppImageFileName(version));
        Assert.AreEqual(
            "Mailtide-1.2.3-android.apk",
            ReleaseArtifacts.AndroidApkFileName(version));
    }

    [TestMethod]
    public void AndroidVersionCode_maps_semver_major_minor_patch()
    {
        Assert.AreEqual(10203, ReleaseArtifacts.AndroidVersionCode("1.2.3"));
        Assert.AreEqual(10000, ReleaseArtifacts.AndroidVersionCode("v1.0.0"));
        Assert.AreEqual(1, ReleaseArtifacts.AndroidVersionCode("0.0.0-local"));
    }
}
