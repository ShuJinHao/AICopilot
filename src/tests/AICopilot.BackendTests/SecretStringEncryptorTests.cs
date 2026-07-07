using System.Security.Cryptography;
using System.Text;
using AICopilot.EntityFrameworkCore.Security;

namespace AICopilot.BackendTests;

public sealed class SecretStringEncryptorTests
{
    private const string EnvVarName = "AICopilotSecurity__ApiKeyEncryptionKey";

    [Fact]
    public void EncryptAndDecrypt_ShouldRoundTripWithoutLeavingPlaintext()
    {
        var original = Environment.GetEnvironmentVariable(EnvVarName);
        Environment.SetEnvironmentVariable(EnvVarName, "unit-test-encryption-key");

        try
        {
            var cipher = SecretStringEncryptor.Encrypt("sk-unit-test-secret");
            var plain = SecretStringEncryptor.Decrypt(cipher);

            cipher.Should().StartWith("encv2:");
            cipher.Should().NotContain("sk-unit-test-secret");
            plain.Should().Be("sk-unit-test-secret");
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVarName, original);
        }
    }

    [Fact]
    public void Decrypt_ShouldRejectUnprotectedStoredSecret()
    {
        var action = () => SecretStringEncryptor.Decrypt("sk-legacy-plaintext");

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*encv2:*");
    }

    [Fact]
    public void Decrypt_ShouldRejectLegacyCipherAtRuntime()
    {
        var original = Environment.GetEnvironmentVariable(EnvVarName);
        Environment.SetEnvironmentVariable(EnvVarName, "unit-test-encryption-key");

        try
        {
            var legacyCipher = CreateLegacyCipher("sk-legacy-secret");
            var action = () => SecretStringEncryptor.Decrypt(legacyCipher);

            action.Should().Throw<InvalidOperationException>()
                .WithMessage("*legacy 'encv1:'*");
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVarName, original);
        }
    }

    [Fact]
    public void ReEncryptLegacyCipher_ShouldMigrateToAuthenticatedCipher()
    {
        var original = Environment.GetEnvironmentVariable(EnvVarName);
        Environment.SetEnvironmentVariable(EnvVarName, "unit-test-encryption-key");

        try
        {
            var legacyCipher = CreateLegacyCipher("sk-legacy-secret");
            var migratedCipher = SecretStringEncryptor.ReEncryptLegacyCipher(legacyCipher);

            migratedCipher.Should().StartWith(SecretStringEncryptor.CipherPrefix);
            migratedCipher.Should().NotContain("sk-legacy-secret");
            SecretStringEncryptor.Decrypt(migratedCipher).Should().Be("sk-legacy-secret");
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVarName, original);
        }
    }

    [Fact]
    public void EnsureConfigured_ShouldThrowWhenMasterKeyMissing()
    {
        var original = Environment.GetEnvironmentVariable(EnvVarName);
        Environment.SetEnvironmentVariable(EnvVarName, null);

        try
        {
            var action = () => SecretStringEncryptor.EnsureConfigured();

            action.Should().Throw<InvalidOperationException>()
                .WithMessage($"*{EnvVarName}*");
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVarName, original);
        }
    }

    private static string CreateLegacyCipher(string plaintext)
    {
        var key = SHA256.HashData(Encoding.UTF8.GetBytes("unit-test-encryption-key"));

        using var aes = Aes.Create();
        aes.Key = key;
        aes.Padding = PaddingMode.PKCS7;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var payload = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, payload, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, payload, aes.IV.Length, cipherBytes.Length);

        return SecretStringEncryptor.LegacyCipherPrefix + Convert.ToBase64String(payload);
    }
}
