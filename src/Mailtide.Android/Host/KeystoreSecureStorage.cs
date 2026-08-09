using System.Security.Cryptography;
using System.Text;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using Mailtide.Core.Security;

namespace Mailtide.Android.Host;

/// <summary>
/// Android Credential store: AES key in AndroidKeyStore, ciphertext under app-private storage.
/// </summary>
internal sealed class KeystoreSecureStorage : ISecureStorage
{
    internal const string KeyAlias = "org.mailtide.credentials";
    private const string AndroidKeyStoreName = "AndroidKeyStore";
    private const string Transformation = "AES/GCM/NoPadding";
    private const int GcmTagLengthBits = 128;

    private readonly string _credentialsDirectory;
    private readonly object _gate = new();

    public KeystoreSecureStorage(string appDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataDirectory);
        _credentialsDirectory = Path.Combine(appDataDirectory, "credentials");
    }

    public Task StoreSecretAsync(string key, string secret, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(secret);
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(_credentialsDirectory);

        try
        {
            lock (_gate)
            {
                EnsureKey();
                var plainBytes = Encoding.UTF8.GetBytes(secret);
                var cipher = Cipher.GetInstance(Transformation)
                    ?? throw new SecureStorageException("Unable to create AES/GCM cipher.");
                var secretKey = RequireSecretKey();
                cipher.Init(Javax.Crypto.CipherMode.EncryptMode, secretKey);
                var iv = cipher.GetIV()
                    ?? throw new SecureStorageException("AndroidKeyStore did not return a GCM IV.");
                var cipherBytes = cipher.DoFinal(plainBytes)
                    ?? throw new SecureStorageException("AndroidKeyStore encryption returned no ciphertext.");

                var payload = new byte[1 + iv.Length + cipherBytes.Length];
                payload[0] = checked((byte)iv.Length);
                Buffer.BlockCopy(iv, 0, payload, 1, iv.Length);
                Buffer.BlockCopy(cipherBytes, 0, payload, 1 + iv.Length, cipherBytes.Length);
                File.WriteAllBytes(CredentialPath(key), payload);
            }
        }
        catch (SecureStorageException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new SecureStorageException("Failed to store Credential via Android Keystore.", ex);
        }

        return Task.CompletedTask;
    }

    public Task<string?> RetrieveSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();

        var path = CredentialPath(key);
        if (!File.Exists(path))
        {
            return Task.FromResult<string?>(null);
        }

        try
        {
            lock (_gate)
            {
                EnsureKey();
                var payload = File.ReadAllBytes(path);
                if (payload.Length < 2)
                {
                    throw new SecureStorageException("Stored Credential blob is truncated.");
                }

                var ivLength = payload[0];
                if (ivLength == 0 || payload.Length < 1 + ivLength)
                {
                    throw new SecureStorageException("Stored Credential blob has an invalid IV.");
                }

                var iv = new byte[ivLength];
                Buffer.BlockCopy(payload, 1, iv, 0, ivLength);
                var cipherBytes = new byte[payload.Length - 1 - ivLength];
                Buffer.BlockCopy(payload, 1 + ivLength, cipherBytes, 0, cipherBytes.Length);

                var cipher = Cipher.GetInstance(Transformation)
                    ?? throw new SecureStorageException("Unable to create AES/GCM cipher.");
                var secretKey = RequireSecretKey();
                cipher.Init(Javax.Crypto.CipherMode.DecryptMode, secretKey, new GCMParameterSpec(GcmTagLengthBits, iv));
                var plainBytes = cipher.DoFinal(cipherBytes)
                    ?? throw new SecureStorageException("AndroidKeyStore decryption returned no plaintext.");
                return Task.FromResult<string?>(Encoding.UTF8.GetString(plainBytes));
            }
        }
        catch (SecureStorageException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new SecureStorageException("Failed to retrieve Credential via Android Keystore.", ex);
        }
    }

    public Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();

        var path = CredentialPath(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private void EnsureKey()
    {
        var keyStore = KeyStore.GetInstance(AndroidKeyStoreName)
            ?? throw new SecureStorageException("AndroidKeyStore is unavailable.");
        keyStore.Load(null);

        if (keyStore.ContainsAlias(KeyAlias))
        {
            return;
        }

        try
        {
            var keyGenerator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, AndroidKeyStoreName)
                ?? throw new SecureStorageException("Unable to create AES KeyGenerator for AndroidKeyStore.");
            var spec = new KeyGenParameterSpec.Builder(
                    KeyAlias,
                    KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
                .SetBlockModes(KeyProperties.BlockModeGcm)
                .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
                .SetKeySize(256)
                .Build();
            keyGenerator.Init(spec);
            keyGenerator.GenerateKey();
        }
        catch (Exception ex)
        {
            throw new SecureStorageException("Failed to create AndroidKeyStore AES key for Credentials.", ex);
        }
    }

    private IKey RequireSecretKey()
    {
        var keyStore = KeyStore.GetInstance(AndroidKeyStoreName)
            ?? throw new SecureStorageException("AndroidKeyStore is unavailable.");
        keyStore.Load(null);
        var key = keyStore.GetKey(KeyAlias, null)
            ?? throw new SecureStorageException($"AndroidKeyStore key '{KeyAlias}' is missing.");
        return key;
    }

    private string CredentialPath(string key)
    {
        var fileName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
        return Path.Combine(_credentialsDirectory, fileName);
    }
}
