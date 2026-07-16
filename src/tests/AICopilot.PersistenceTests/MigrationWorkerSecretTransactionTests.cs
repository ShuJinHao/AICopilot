using System.Security.Cryptography;
using System.Text;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.Security;
using AICopilot.MigrationWorkApp;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AICopilot.PersistenceTests;

[Collection(PostgresPersistenceTestCollection.Name)]
public sealed class MigrationWorkerSecretTransactionTests(PostgresPersistenceFixture fixture)
{
    private const string EnvVarName = "AICopilotSecurity__ApiKeyEncryptionKey";
    private const string TestEncryptionKey = "unit-test-encryption-key";

    [Fact]
    public async Task MigrateAsync_ShouldRollbackLanguageAndEmbeddingChanges_WhenEmbeddingUpdateFails()
    {
        var original = Environment.GetEnvironmentVariable(EnvVarName);
        Environment.SetEnvironmentVariable(EnvVarName, TestEncryptionKey);

        try
        {
            await using var database = await PostgresScratchDatabase.CreateAsync(
                fixture.ConnectionString,
                "aicopilot_secret_migration");
            var aiGatewayOptions = PostgresPersistenceTestOptions.Create<AiGatewayDbContext>(
                database.ConnectionString,
                MigrationHistoryTables.AiGateway);
            var ragOptions = PostgresPersistenceTestOptions.Create<RagDbContext>(
                database.ConnectionString,
                MigrationHistoryTables.Rag);
            const string languagePlaintext = "sk-language-transaction";
            const string embeddingPlaintext = "sk-embedding-transaction";

            await using (var setupAiGateway = new AiGatewayDbContext(aiGatewayOptions))
            {
                await setupAiGateway.Database.MigrateAsync();
                setupAiGateway.LanguageModels.Add(CreateLanguageModel(languagePlaintext));
                await setupAiGateway.SaveChangesAsync();
            }

            await using (var setupRag = new RagDbContext(ragOptions))
            {
                await setupRag.Database.MigrateAsync();
                setupRag.EmbeddingModels.Add(CreateEmbeddingModel(embeddingPlaintext));
                await setupRag.SaveChangesAsync();
            }

            await using (var connection = new NpgsqlConnection(database.ConnectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE FUNCTION rag.reject_embedding_api_key_update()
                    RETURNS trigger
                    LANGUAGE plpgsql
                    AS $$
                    BEGIN
                        RAISE EXCEPTION 'embedding api key update rejected by test trigger';
                    END;
                    $$;

                    CREATE TRIGGER reject_embedding_api_key_update
                    BEFORE UPDATE OF api_key ON rag.embedding_models
                    FOR EACH ROW
                    EXECUTE FUNCTION rag.reject_embedding_api_key_update();
                    """;
                await command.ExecuteNonQueryAsync();
            }

            await using (var migratingAiGateway = new AiGatewayDbContext(aiGatewayOptions))
            await using (var migratingRag = new RagDbContext(ragOptions))
            {
                var action = () => MigrationWorkerSecretMigrator.MigrateAsync(
                    migratingAiGateway,
                    migratingRag,
                    CancellationToken.None);

                await action.Should().ThrowAsync<DbUpdateException>();
            }

            await using var verifyAiGateway = new AiGatewayDbContext(aiGatewayOptions);
            await using var verifyRag = new RagDbContext(ragOptions);
            (await verifyAiGateway.LanguageModels.AsNoTracking().SingleAsync()).ApiKey
                .Should().Be(languagePlaintext);
            (await verifyRag.EmbeddingModels.AsNoTracking().SingleAsync()).ApiKey
                .Should().Be(embeddingPlaintext);
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVarName, original);
        }
    }

    [Fact]
    public async Task MigrateAndVerifyAsync_ShouldProtectBothRealTablesAndRejectEachRawSecretField()
    {
        var original = Environment.GetEnvironmentVariable(EnvVarName);
        Environment.SetEnvironmentVariable(EnvVarName, TestEncryptionKey);

        try
        {
            await using var database = await PostgresScratchDatabase.CreateAsync(
                fixture.ConnectionString,
                "aicopilot_secret_verify");
            var aiGatewayOptions = PostgresPersistenceTestOptions.Create<AiGatewayDbContext>(
                database.ConnectionString,
                MigrationHistoryTables.AiGateway);
            var ragOptions = PostgresPersistenceTestOptions.Create<RagDbContext>(
                database.ConnectionString,
                MigrationHistoryTables.Rag);
            const string languagePlaintext = "sk-language-success";
            const string embeddingPlaintext = "sk-embedding-success";
            var embeddingLegacyCipher = CreateLegacyCipher(embeddingPlaintext);

            await using (var setupAiGateway = new AiGatewayDbContext(aiGatewayOptions))
            {
                await setupAiGateway.Database.MigrateAsync();
                setupAiGateway.LanguageModels.Add(CreateLanguageModel(languagePlaintext));
                await setupAiGateway.SaveChangesAsync();
            }

            await using (var setupRag = new RagDbContext(ragOptions))
            {
                await setupRag.Database.MigrateAsync();
                setupRag.EmbeddingModels.Add(CreateEmbeddingModel(embeddingLegacyCipher));
                await setupRag.SaveChangesAsync();
            }

            await using (var migratingAiGateway = new AiGatewayDbContext(aiGatewayOptions))
            await using (var migratingRag = new RagDbContext(ragOptions))
            {
                var result = await MigrationWorkerSecretMigrator.MigrateAsync(
                    migratingAiGateway,
                    migratingRag,
                    CancellationToken.None);

                result.LanguageModelPlaintextCount.Should().Be(1);
                result.EmbeddingModelLegacyCipherCount.Should().Be(1);
            }

            await using var verifyAiGateway = new AiGatewayDbContext(aiGatewayOptions);
            await using var verifyRag = new RagDbContext(ragOptions);
            var migratedLanguageSecret = (await verifyAiGateway.LanguageModels
                    .AsNoTracking()
                    .SingleAsync())
                .ApiKey;
            var migratedEmbeddingSecret = (await verifyRag.EmbeddingModels
                    .AsNoTracking()
                    .SingleAsync())
                .ApiKey;

            migratedLanguageSecret.Should().StartWith(SecretStringEncryptor.CipherPrefix);
            migratedLanguageSecret.Should().NotBe(languagePlaintext);
            SecretStringEncryptor.Decrypt(migratedLanguageSecret).Should().Be(languagePlaintext);
            migratedEmbeddingSecret.Should().StartWith(SecretStringEncryptor.CipherPrefix);
            migratedEmbeddingSecret.Should().NotBe(embeddingLegacyCipher);
            SecretStringEncryptor.Decrypt(migratedEmbeddingSecret).Should().Be(embeddingPlaintext);
            await MigrationWorkerSecretMigrator.VerifyAsync(
                verifyAiGateway,
                verifyRag,
                CancellationToken.None);

            const string rawLanguageSecret = "raw-language-secret";
            await verifyAiGateway.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE aigateway.language_models SET api_key = {rawLanguageSecret}");
            var verifyLanguageAction = () => MigrationWorkerSecretMigrator.VerifyAsync(
                verifyAiGateway,
                verifyRag,
                CancellationToken.None);

            var languageAssertion = await verifyLanguageAction.Should().ThrowAsync<InvalidOperationException>();
            languageAssertion.Which.Message.Should()
                .Contain("LanguageModel.ApiKey")
                .And.NotContain(rawLanguageSecret);

            await verifyAiGateway.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE aigateway.language_models SET api_key = {migratedLanguageSecret}");
            await MigrationWorkerSecretMigrator.VerifyAsync(
                verifyAiGateway,
                verifyRag,
                CancellationToken.None);

            const string rawEmbeddingSecret = "raw-embedding-secret";
            await verifyRag.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE rag.embedding_models SET api_key = {rawEmbeddingSecret}");
            var verifyEmbeddingAction = () => MigrationWorkerSecretMigrator.VerifyAsync(
                verifyAiGateway,
                verifyRag,
                CancellationToken.None);

            var embeddingAssertion = await verifyEmbeddingAction.Should().ThrowAsync<InvalidOperationException>();
            embeddingAssertion.Which.Message.Should()
                .Contain("EmbeddingModel.ApiKey")
                .And.NotContain(rawEmbeddingSecret);
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVarName, original);
        }
    }

    private static LanguageModel CreateLanguageModel(string apiKey)
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

    private static EmbeddingModel CreateEmbeddingModel(string apiKey)
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
