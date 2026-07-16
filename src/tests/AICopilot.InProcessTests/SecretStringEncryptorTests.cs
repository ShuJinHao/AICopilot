using System.Security.Cryptography;
using System.Text;
using AICopilot.EntityFrameworkCore.Security;

namespace AICopilot.InProcessTests;

[Collection(SecretEnvironmentTestCollection.Name)]
public sealed class SecretStringEncryptorTests
{
    private const string EnvVarName = "AICopilotSecurity__ApiKeyEncryptionKey";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int LegacyIvSize = 16;

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
            SecretStringEncryptor.Encrypt(null).Should().BeNull();
            SecretStringEncryptor.Encrypt(string.Empty).Should().BeEmpty();
            SecretStringEncryptor.Decrypt(null).Should().BeNull();
            SecretStringEncryptor.Decrypt(string.Empty).Should().BeEmpty();
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVarName, original);
        }
    }

    [Fact]
    public void Decrypt_ShouldRejectUnprotectedStoredSecret()
    {
        var original = Environment.GetEnvironmentVariable(EnvVarName);
        Environment.SetEnvironmentVariable(EnvVarName, "unit-test-encryption-key");

        try
        {
            var unprotectedAction = () => SecretStringEncryptor.Decrypt("sk-legacy-plaintext");
            var minimumPayloadAction = () => SecretStringEncryptor.Decrypt(
                SecretStringEncryptor.CipherPrefix
                + Convert.ToBase64String(new byte[NonceSize + TagSize]));

            var authenticatedCipher = SecretStringEncryptor.Encrypt("sk-tamper-test")!;
            var tamperedPayload = Convert.FromBase64String(
                authenticatedCipher[SecretStringEncryptor.CipherPrefix.Length..]);
            tamperedPayload[^1] ^= 0x01;
            var tamperedAction = () => SecretStringEncryptor.Decrypt(
                SecretStringEncryptor.CipherPrefix + Convert.ToBase64String(tamperedPayload));

            unprotectedAction.Should().Throw<InvalidOperationException>()
                .WithMessage("*encv2:*");
            minimumPayloadAction.Should().Throw<InvalidOperationException>()
                .Which.Message.Should().Be("Encrypted secret payload is invalid.");
            var tamperedException = tamperedAction.Should().Throw<InvalidOperationException>().Which;
            tamperedException.Message.Should().Be("Encrypted secret authentication tag is invalid.");
            tamperedException.InnerException.Should().BeAssignableTo<CryptographicException>();
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVarName, original);
        }
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
            SecretStringEncryptor.ReEncryptLegacyCipher(null).Should().BeNull();
            SecretStringEncryptor.ReEncryptLegacyCipher(string.Empty).Should().BeEmpty();
            SecretStringEncryptor.ReEncryptLegacyCipher("sk-plain-secret")
                .Should().Be("sk-plain-secret");

            var invalidLegacyCipher = SecretStringEncryptor.LegacyCipherPrefix
                                      + Convert.ToBase64String(new byte[LegacyIvSize]);
            var invalidLegacyAction = () =>
                SecretStringEncryptor.ReEncryptLegacyCipher(invalidLegacyCipher);
            invalidLegacyAction.Should().Throw<InvalidOperationException>()
                .Which.Message.Should().Be("Legacy encrypted secret payload is invalid.");
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
