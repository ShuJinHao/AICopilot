using System.Security.Cryptography;
using System.Text;

namespace AICopilot.EntityFrameworkCore.Security;

public static class SecretStringEncryptor
{
    public const string CipherPrefix = "encv2:";
    public const string LegacyCipherPrefix = "encv1:";
    private const string EncryptionKeyEnvironmentVariable = "AICopilotSecurity__ApiKeyEncryptionKey";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int LegacyIvSize = 16;

    public static string? Encrypt(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return plaintext;
        }

        var key = LoadKey();
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        var payload = new byte[NonceSize + TagSize + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
        Buffer.BlockCopy(cipherBytes, 0, payload, NonceSize + TagSize, cipherBytes.Length);

        return CipherPrefix + Convert.ToBase64String(payload);
    }

    public static string? Decrypt(string? storedValue)
    {
        if (string.IsNullOrEmpty(storedValue))
        {
            return storedValue;
        }

        if (IsLegacyEncrypted(storedValue))
        {
            throw new InvalidOperationException(
                $"Stored secret uses legacy '{LegacyCipherPrefix}' format and must be re-encrypted by the migration worker before runtime use.");
        }

        if (!IsEncrypted(storedValue))
        {
            throw new InvalidOperationException(
                $"Stored secret must be encrypted with '{CipherPrefix}'. Re-save the configuration to protect the API key.");
        }

        var key = LoadKey();
        var payload = Convert.FromBase64String(storedValue[CipherPrefix.Length..]);

        if (payload.Length <= NonceSize + TagSize)
        {
            throw new InvalidOperationException("Encrypted secret payload is invalid.");
        }

        var nonce = payload[..NonceSize];
        var tag = payload[NonceSize..(NonceSize + TagSize)];
        var cipherBytes = payload[(NonceSize + TagSize)..];
        var plainBytes = new byte[cipherBytes.Length];

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException("Encrypted secret authentication tag is invalid.", ex);
        }

        return Encoding.UTF8.GetString(plainBytes);
    }

    public static bool IsEncrypted(string? storedValue)
    {
        return !string.IsNullOrEmpty(storedValue)
               && storedValue.StartsWith(CipherPrefix, StringComparison.Ordinal);
    }

    public static bool IsLegacyEncrypted(string? storedValue)
    {
        return !string.IsNullOrEmpty(storedValue)
               && storedValue.StartsWith(LegacyCipherPrefix, StringComparison.Ordinal);
    }

    public static string? ReEncryptLegacyCipher(string? storedValue)
    {
        if (string.IsNullOrEmpty(storedValue) || !IsLegacyEncrypted(storedValue))
        {
            return storedValue;
        }

        return Encrypt(DecryptLegacyCipher(storedValue));
    }

    public static void EnsureConfigured()
    {
        _ = LoadKey();
    }

    private static byte[] LoadKey()
    {
        var keyText = Environment.GetEnvironmentVariable(EncryptionKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(keyText))
        {
            throw new InvalidOperationException(
                $"Environment variable '{EncryptionKeyEnvironmentVariable}' is required to encrypt and decrypt model API keys.");
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(keyText.Trim()));
    }

    private static string DecryptLegacyCipher(string storedValue)
    {
        var key = LoadKey();
        var payload = Convert.FromBase64String(storedValue[LegacyCipherPrefix.Length..]);

        if (payload.Length <= LegacyIvSize)
        {
            throw new InvalidOperationException("Legacy encrypted secret payload is invalid.");
        }

        var iv = payload[..LegacyIvSize];
        var cipherBytes = payload[LegacyIvSize..];

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }
}
