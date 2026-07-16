using System.Security.Cryptography;
using System.Text;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.EntityFrameworkCore.Security;
using AICopilot.MigrationWorkApp;

namespace AICopilot.InProcessTests;

[Collection(SecretEnvironmentTestCollection.Name)]
public sealed class MigrationWorkerSecretMigratorTests
{
    internal const string EnvVarName = "AICopilotSecurity__ApiKeyEncryptionKey";
    internal const string TestEncryptionKey = "unit-test-encryption-key";

    [Fact]
    public async Task ExecutionPlan_CheckSecretsOnly_ShouldVerifyBeforeStopping()
    {
        var stages = new List<MigrationWorkerStage>();

        await MigrationWorkerExecutionPlan.RunAsync(
            checkSecretsOnly: true,
            (stage, cancellationToken) =>
            {
                cancellationToken.Should().Be(CancellationToken.None);
                stages.Add(stage);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        stages.Should().Equal(
            MigrationWorkerStage.VerifySecrets,
            MigrationWorkerStage.StopApplication);
    }

    [Fact]
    public async Task ExecutionPlan_FullMigration_ShouldRunStagesInRequiredOrder()
    {
        var stages = new List<MigrationWorkerStage>();

        await MigrationWorkerExecutionPlan.RunAsync(
            checkSecretsOnly: false,
            (stage, _) =>
            {
                stages.Add(stage);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        stages.Should().Equal(
            MigrationWorkerStage.MigrateDatabases,
            MigrationWorkerStage.MigrateSecrets,
            MigrationWorkerStage.SeedIdentity,
            MigrationWorkerStage.SeedAiGateway,
            MigrationWorkerStage.SeedCloudReadOnly,
            MigrationWorkerStage.SeedSimulation,
            MigrationWorkerStage.StopApplication);
    }

    [Fact]
    public async Task ExecutionPlan_SecretStageFailure_ShouldShortCircuitWithoutStoppingApplication()
    {
        var stages = new List<MigrationWorkerStage>();
        var secretFailure = new InvalidOperationException("secret migration failed");

        var action = () => MigrationWorkerExecutionPlan.RunAsync(
            checkSecretsOnly: false,
            (stage, _) =>
            {
                stages.Add(stage);
                return stage == MigrationWorkerStage.MigrateSecrets
                    ? Task.FromException(secretFailure)
                    : Task.CompletedTask;
            },
            CancellationToken.None);

        var assertion = await action.Should().ThrowAsync<InvalidOperationException>();

        assertion.Which.Should().BeSameAs(secretFailure);
        stages.Should().Equal(
            MigrationWorkerStage.MigrateDatabases,
            MigrationWorkerStage.MigrateSecrets);
        stages.Should().NotContain(MigrationWorkerStage.StopApplication);
    }

    [Fact]
    public void MigrateLanguageModelApiKeys_ShouldProtectLegacyCipherAndPlaintextSecrets()
    {
        var original = Environment.GetEnvironmentVariable(EnvVarName);
        Environment.SetEnvironmentVariable(EnvVarName, TestEncryptionKey);

        try
        {
            var alreadyProtected = SecretStringEncryptor.Encrypt("sk-language-protected")!;
            var legacyModel = CreateLanguageModel(CreateLegacyCipher("sk-language-legacy"));
            var plaintextModel = CreateLanguageModel("sk-language-plain");
            var protectedModel = CreateLanguageModel(alreadyProtected);
            legacyModel.MarkConnectivityFailed(DateTimeOffset.UtcNow, "legacy failed");
            plaintextModel.MarkConnectivityFailed(DateTimeOffset.UtcNow, "plaintext failed");

            var result = MigrationWorkerSecretMigrator.MigrateLanguageModelApiKeys(
                [legacyModel, plaintextModel, protectedModel]);

            result.LegacyCipherCount.Should().Be(1);
            result.PlaintextCount.Should().Be(1);
            SecretStringEncryptor.Decrypt(legacyModel.ApiKey).Should().Be("sk-language-legacy");
            SecretStringEncryptor.Decrypt(plaintextModel.ApiKey).Should().Be("sk-language-plain");
            protectedModel.ApiKey.Should().Be(alreadyProtected);
            legacyModel.ConnectivityStatus.Should().Be(LanguageModelConnectivityStatus.Unknown);
            plaintextModel.ConnectivityStatus.Should().Be(LanguageModelConnectivityStatus.Unknown);
            legacyModel.ApiKey.Should().NotContain("sk-language-legacy");
            plaintextModel.ApiKey.Should().NotContain("sk-language-plain");
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVarName, original);
        }
    }

    [Fact]
    public void MigrateLanguageModelApiKeys_ShouldRejectUnreadableProtectedSecretsBeforeChangingLaterModels()
    {
        var original = Environment.GetEnvironmentVariable(EnvVarName);
        Environment.SetEnvironmentVariable(EnvVarName, TestEncryptionKey);

        try
        {
            var unreadableProtectedModel = CreateLanguageModel("encv2:protected");
            var plaintextModel = CreateLanguageModel("sk-language-plain");

            var action = () => MigrationWorkerSecretMigrator.MigrateLanguageModelApiKeys(
                [unreadableProtectedModel, plaintextModel]);

            action.Should().Throw<InvalidOperationException>()
                .WithMessage("*LanguageModel.ApiKey*1 unreadable encv2 secret value*before writing migrated secrets*");
            plaintextModel.ApiKey.Should().Be("sk-language-plain");
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVarName, original);
        }
    }

    [Fact]
    public void MigrateLanguageModelApiKeys_ShouldPreflightUnreadableProtectedSecretsBeforeChangingEarlierModels()
    {
        var original = Environment.GetEnvironmentVariable(EnvVarName);
        Environment.SetEnvironmentVariable(EnvVarName, TestEncryptionKey);

        try
        {
            var legacyModel = CreateLanguageModel(CreateLegacyCipher("sk-language-legacy"));
            var originalLegacyApiKey = legacyModel.ApiKey;
            var unreadableProtectedModel = CreateLanguageModel("encv2:protected");

            var action = () => MigrationWorkerSecretMigrator.MigrateLanguageModelApiKeys(
                [legacyModel, unreadableProtectedModel]);

            action.Should().Throw<InvalidOperationException>()
                .WithMessage("*LanguageModel.ApiKey*1 unreadable encv2 secret value*before writing migrated secrets*");
            legacyModel.ApiKey.Should().Be(originalLegacyApiKey);
            legacyModel.ConnectivityStatus.Should().Be(LanguageModelConnectivityStatus.Unknown);
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVarName, original);
        }
    }

    [Fact]
    public void MigrateEmbeddingModelApiKeys_ShouldProtectLegacyCipherAndPlaintextSecrets()
    {
        var original = Environment.GetEnvironmentVariable(EnvVarName);
        Environment.SetEnvironmentVariable(EnvVarName, TestEncryptionKey);

        try
        {
            var alreadyProtected = SecretStringEncryptor.Encrypt("sk-embedding-protected")!;
            var legacyModel = CreateEmbeddingModel(CreateLegacyCipher("sk-embedding-legacy"));
            var plaintextModel = CreateEmbeddingModel("sk-embedding-plain");
            var protectedModel = CreateEmbeddingModel(alreadyProtected);

            var result = MigrationWorkerSecretMigrator.MigrateEmbeddingModelApiKeys(
                [legacyModel, plaintextModel, protectedModel]);

            result.LegacyCipherCount.Should().Be(1);
            result.PlaintextCount.Should().Be(1);
            SecretStringEncryptor.Decrypt(legacyModel.ApiKey).Should().Be("sk-embedding-legacy");
            SecretStringEncryptor.Decrypt(plaintextModel.ApiKey).Should().Be("sk-embedding-plain");
            protectedModel.ApiKey.Should().Be(alreadyProtected);
            legacyModel.ApiKey.Should().NotContain("sk-embedding-legacy");
            plaintextModel.ApiKey.Should().NotContain("sk-embedding-plain");
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVarName, original);
        }
    }

    [Fact]
    public void MigrateEmbeddingModelApiKeys_ShouldRejectUnreadableProtectedSecretsBeforeChangingLaterModels()
    {
        var original = Environment.GetEnvironmentVariable(EnvVarName);
        Environment.SetEnvironmentVariable(EnvVarName, TestEncryptionKey);

        try
        {
            var unreadableProtectedModel = CreateEmbeddingModel("encv2:protected");
            var plaintextModel = CreateEmbeddingModel("sk-embedding-plain");

            var action = () => MigrationWorkerSecretMigrator.MigrateEmbeddingModelApiKeys(
                [unreadableProtectedModel, plaintextModel]);

            action.Should().Throw<InvalidOperationException>()
                .WithMessage("*EmbeddingModel.ApiKey*1 unreadable encv2 secret value*before writing migrated secrets*");
            plaintextModel.ApiKey.Should().Be("sk-embedding-plain");
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVarName, original);
        }
    }

    [Fact]
    public void MigrateEmbeddingModelApiKeys_ShouldPreflightUnreadableProtectedSecretsBeforeChangingEarlierModels()
    {
        var original = Environment.GetEnvironmentVariable(EnvVarName);
        Environment.SetEnvironmentVariable(EnvVarName, TestEncryptionKey);

        try
        {
            var legacyModel = CreateEmbeddingModel(CreateLegacyCipher("sk-embedding-legacy"));
            var originalLegacyApiKey = legacyModel.ApiKey;
            var unreadableProtectedModel = CreateEmbeddingModel("encv2:protected");

            var action = () => MigrationWorkerSecretMigrator.MigrateEmbeddingModelApiKeys(
                [legacyModel, unreadableProtectedModel]);

            action.Should().Throw<InvalidOperationException>()
                .WithMessage("*EmbeddingModel.ApiKey*1 unreadable encv2 secret value*before writing migrated secrets*");
            legacyModel.ApiKey.Should().Be(originalLegacyApiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVarName, original);
        }
    }

    [Fact]
    public void EnsureMigratedSecrets_ShouldAcceptOnlyBlankOrAuthenticatedCipherValues()
    {
        var original = Environment.GetEnvironmentVariable(EnvVarName);
        Environment.SetEnvironmentVariable(EnvVarName, TestEncryptionKey);

        try
        {
            var authenticatedCipher = SecretStringEncryptor.Encrypt("sk-authenticated")!;

            var action = () => MigrationWorkerSecretMigrator.EnsureMigratedSecrets(
                [null, "", " ", authenticatedCipher],
                "LanguageModel.ApiKey");

            action.Should().NotThrow();
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVarName, original);
        }
    }

    [Fact]
    public void EnsureMigratedSecrets_ShouldRejectLegacyCipherAndPlaintextValues()
    {
        var original = Environment.GetEnvironmentVariable(EnvVarName);
        Environment.SetEnvironmentVariable(EnvVarName, TestEncryptionKey);

        try
        {
            var authenticatedCipher = SecretStringEncryptor.Encrypt("sk-authenticated")!;

            var action = () => MigrationWorkerSecretMigrator.EnsureMigratedSecrets(
                [authenticatedCipher, "encv1:legacy", "sk-plain"],
                "EmbeddingModel.ApiKey");

            action.Should().Throw<InvalidOperationException>()
                .WithMessage("*EmbeddingModel.ApiKey*2 non-encv2 secret value*");
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVarName, original);
        }
    }

    [Fact]
    public void EnsureMigratedSecrets_ShouldRejectMalformedAuthenticatedCipherValues()
    {
        var original = Environment.GetEnvironmentVariable(EnvVarName);
        Environment.SetEnvironmentVariable(EnvVarName, TestEncryptionKey);

        try
        {
            var action = () => MigrationWorkerSecretMigrator.EnsureMigratedSecrets(
                ["encv2:protected"],
                "LanguageModel.ApiKey");

            action.Should().Throw<InvalidOperationException>()
                .WithMessage("*LanguageModel.ApiKey*1 unreadable encv2 secret value*");
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVarName, original);
        }
    }

    [Fact]
    public void EnsureMigratedSecrets_ShouldRejectCipherValuesProtectedWithDifferentKey()
    {
        var original = Environment.GetEnvironmentVariable(EnvVarName);

        try
        {
            Environment.SetEnvironmentVariable(EnvVarName, "old-unit-test-encryption-key");
            var oldKeyCipher = SecretStringEncryptor.Encrypt("sk-old-key")!;

            Environment.SetEnvironmentVariable(EnvVarName, TestEncryptionKey);
            var action = () => MigrationWorkerSecretMigrator.EnsureMigratedSecrets(
                [oldKeyCipher],
                "EmbeddingModel.ApiKey");

            action.Should().Throw<InvalidOperationException>()
                .WithMessage("*EmbeddingModel.ApiKey*1 unreadable encv2 secret value*");
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVarName, original);
        }
    }

    internal static LanguageModel CreateLanguageModel(string apiKey)
    {
        return new LanguageModel(
            "OpenAI",
            $"chat-{Guid.NewGuid():N}",
            "https://example.test/v1",
            apiKey,
            new ModelParameters
            {
                MaxTokens = 4096,
                MaxOutputTokens = 1024,
                Temperature = 0.2f
            },
            LanguageModelProtocolTypes.OpenAICompatible,
            LanguageModelUsage.Chat,
            true);
    }

    internal static EmbeddingModel CreateEmbeddingModel(string apiKey)
    {
        return new EmbeddingModel(
            $"embedding-{Guid.NewGuid():N}",
            "OpenAI",
            "https://example.test/v1",
            "text-embedding-test",
            1536,
            8191,
            apiKey);
    }

    private static string CreateLegacyCipher(string plaintext)
    {
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(TestEncryptionKey));

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
