namespace Mailtide.Android.Tests;

[TestClass]
public sealed class KeystoreRuntimeGateTests
{
    [TestMethod]
    public void Keystore_Account_Credential_runtime_tests_require_Android()
    {
        if (!OperatingSystem.IsAndroid())
        {
            Assert.Inconclusive(
                "KeystoreSecureStorage Account/Credential runtime coverage requires an Android device or emulator. " +
                "Desktop CI covers the same Core remove-clears-Credential contract via HostSecureStorageAccountTests.");
        }
    }
}
