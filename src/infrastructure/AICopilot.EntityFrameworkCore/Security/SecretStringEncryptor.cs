using System.Security.Cryptography;
using System.Text;

namespace AICopilot.EntityFrameworkCore.Security;

public static class SecretStringEncryptor
{
    private const string CipherPrefix = "encv1:";
    private const string EncryptionKeyEnvironmentVariable = "AICopilotSecurity__ApiKeyEncryptionKey";

    public static string? Encrypt(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return plaintext;
        }

        var key = LoadKey();

        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var payload = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, payload, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, payload, aes.IV.Length, cipherBytes.Length);

        return CipherPrefix + Convert.ToBase64String(payload);
    }

    public static string? Decrypt(string? storedValue)
    {
        if (string.IsNullOrEmpty(storedValue))
        {
            return storedValue;
        }

        if (!storedValue.StartsWith(CipherPrefix, StringComparison.Ordinal))
        {
            return storedValue;
        }

        var key = LoadKey();
        var payload = Convert.FromBase64String(storedValue[CipherPrefix.Length..]);

        if (payload.Length <= 16)
        {
            throw new InvalidOperationException("Encrypted secret payload is invalid.");
        }

        var iv = payload[..16];
        var cipherBytes = payload[16..];

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return Encoding.UTF8.GetString(plainBytes);
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
}
