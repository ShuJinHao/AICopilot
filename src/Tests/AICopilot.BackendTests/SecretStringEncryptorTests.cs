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

            cipher.Should().StartWith("encv1:");
            cipher.Should().NotContain("sk-unit-test-secret");
            plain.Should().Be("sk-unit-test-secret");
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
}
