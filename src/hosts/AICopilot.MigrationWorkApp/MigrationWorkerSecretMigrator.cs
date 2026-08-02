using System.Runtime.ExceptionServices;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace AICopilot.MigrationWorkApp;

internal static class MigrationWorkerSecretMigrator
{
    public static async Task<SecretMigrationResult> MigrateAsync(
        DbContextOptions<AiGatewayDbContext> aiGatewayDbContextOptions,
        DbContextOptions<RagDbContext> ragDbContextOptions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(aiGatewayDbContextOptions);
        ArgumentNullException.ThrowIfNull(ragDbContextOptions);

        await using var executionStrategyContext =
            new AiGatewayDbContext(aiGatewayDbContextOptions);
        var strategy = executionStrategyContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            // A failed attempt may already have accepted encrypted values into its
            // ChangeTracker before the shared transaction rolls back. Every retry
            // therefore owns fresh contexts and reloads both source tables.
            await using var transactionalAiGatewayDbContext =
                new AiGatewayDbContext(aiGatewayDbContextOptions);
            await transactionalAiGatewayDbContext.Database.OpenConnectionAsync(cancellationToken);
            await using var transaction =
                await transactionalAiGatewayDbContext.Database.BeginTransactionAsync(cancellationToken);
            var committed = false;

            try
            {
                var transactionalRagOptions =
                    new DbContextOptionsBuilder<RagDbContext>(ragDbContextOptions)
                    .UseNpgsql(transactionalAiGatewayDbContext.Database.GetDbConnection())
                    .Options;

                await using var transactionalRagDbContext =
                    new RagDbContext(transactionalRagOptions);
                await transactionalRagDbContext.Database.UseTransactionAsync(
                    transaction.GetDbTransaction(),
                    cancellationToken);

                var result = await MigrateInCurrentTransactionAsync(
                    transactionalAiGatewayDbContext,
                    transactionalRagDbContext,
                    cancellationToken);

                await transaction.CommitAsync(cancellationToken);
                committed = true;

                return result;
            }
            catch (Exception exception)
            {
                var retryable = FindRetryableProviderException(exception);
                if (retryable is null)
                {
                    throw;
                }

                // The non-retrying strategy on the secondary context wraps a
                // provider transient in InvalidOperationException. Restore only
                // the explicit Npgsql transient so the outer strategy can retry.
                ExceptionDispatchInfo.Capture(retryable).Throw();
                throw;
            }
            finally
            {
                if (!committed && transaction.GetDbTransaction().Connection is not null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
            }
        });
    }

    private static Exception? FindRetryableProviderException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is NpgsqlException { IsTransient: true })
            {
                return current;
            }
        }

        return null;
    }

    private static async Task<SecretMigrationResult> MigrateInCurrentTransactionAsync(
        AiGatewayDbContext aiGatewayDbContext,
        RagDbContext ragDbContext,
        CancellationToken cancellationToken)
    {
        var languageModels = await aiGatewayDbContext.LanguageModels.ToListAsync(cancellationToken);
        var embeddingModels = await ragDbContext.EmbeddingModels.ToListAsync(cancellationToken);

        var languageResult = MigrateLanguageModelApiKeys(languageModels);
        var embeddingResult = MigrateEmbeddingModelApiKeys(embeddingModels);

        if (languageResult.HasChanges)
        {
            await aiGatewayDbContext.SaveChangesAsync(cancellationToken);
        }

        if (embeddingResult.HasChanges)
        {
            await ragDbContext.SaveChangesAsync(cancellationToken);
        }

        EnsureMigratedSecrets(
            languageModels.Select(model => model.ApiKey),
            "LanguageModel.ApiKey");
        EnsureMigratedSecrets(
            embeddingModels.Select(model => model.ApiKey),
            "EmbeddingModel.ApiKey");

        return new SecretMigrationResult(
            languageResult.LegacyCipherCount,
            languageResult.PlaintextCount,
            embeddingResult.LegacyCipherCount,
            embeddingResult.PlaintextCount);
    }

    public static async Task VerifyAsync(
        AiGatewayDbContext aiGatewayDbContext,
        RagDbContext ragDbContext,
        CancellationToken cancellationToken)
    {
        var languageApiKeys = await aiGatewayDbContext.LanguageModels
            .Select(model => model.ApiKey)
            .ToListAsync(cancellationToken);
        var embeddingApiKeys = await ragDbContext.EmbeddingModels
            .Select(model => model.ApiKey)
            .ToListAsync(cancellationToken);

        EnsureMigratedSecrets(languageApiKeys, "LanguageModel.ApiKey");
        EnsureMigratedSecrets(embeddingApiKeys, "EmbeddingModel.ApiKey");
    }

    internal static SecretMigrationSectionResult MigrateLanguageModelApiKeys(
        IEnumerable<LanguageModel> languageModels)
    {
        var models = languageModels.ToArray();
        EnsureReadableEncryptedSecrets(
            models.Select(model => model.ApiKey),
            "LanguageModel.ApiKey");

        var legacyCipherCount = 0;
        var plaintextCount = 0;

        foreach (var model in models)
        {
            var migratedApiKey = MigrateStoredSecret(
                model.ApiKey,
                ref legacyCipherCount,
                ref plaintextCount);
            if (string.Equals(migratedApiKey, model.ApiKey, StringComparison.Ordinal))
            {
                continue;
            }

            model.UpdateApiKey(migratedApiKey);
            model.ResetConnectivityStatus();
        }

        return new SecretMigrationSectionResult(legacyCipherCount, plaintextCount);
    }

    internal static SecretMigrationSectionResult MigrateEmbeddingModelApiKeys(
        IEnumerable<EmbeddingModel> embeddingModels)
    {
        var models = embeddingModels.ToArray();
        EnsureReadableEncryptedSecrets(
            models.Select(model => model.ApiKey),
            "EmbeddingModel.ApiKey");

        var legacyCipherCount = 0;
        var plaintextCount = 0;

        foreach (var model in models)
        {
            var migratedApiKey = MigrateStoredSecret(
                model.ApiKey,
                ref legacyCipherCount,
                ref plaintextCount);
            if (string.Equals(migratedApiKey, model.ApiKey, StringComparison.Ordinal))
            {
                continue;
            }

            model.Update(
                model.Name,
                model.Provider,
                model.BaseUrl,
                migratedApiKey,
                model.ModelName,
                model.Dimensions,
                model.MaxTokens,
                model.IsEnabled);
        }

        return new SecretMigrationSectionResult(legacyCipherCount, plaintextCount);
    }

    private static string? MigrateStoredSecret(
        string? storedValue,
        ref int legacyCipherCount,
        ref int plaintextCount)
    {
        if (string.IsNullOrWhiteSpace(storedValue))
        {
            return storedValue;
        }

        if (SecretStringEncryptor.IsEncrypted(storedValue))
        {
            return storedValue;
        }

        if (SecretStringEncryptor.IsLegacyEncrypted(storedValue))
        {
            legacyCipherCount++;
            return SecretStringEncryptor.ReEncryptLegacyCipher(storedValue);
        }

        plaintextCount++;
        return SecretStringEncryptor.Encrypt(storedValue.Trim());
    }

    internal static void EnsureMigratedSecrets(
        IEnumerable<string?> storedValues,
        string fieldName)
    {
        var nonEncryptedCount = 0;
        var unreadableEncryptedCount = 0;

        foreach (var storedValue in storedValues)
        {
            if (string.IsNullOrWhiteSpace(storedValue))
            {
                continue;
            }

            if (!SecretStringEncryptor.IsEncrypted(storedValue))
            {
                nonEncryptedCount++;
                continue;
            }

            try
            {
                _ = SecretStringEncryptor.Decrypt(storedValue);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Environment variable", StringComparison.Ordinal))
            {
                throw;
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException)
            {
                unreadableEncryptedCount++;
            }
        }

        if (nonEncryptedCount > 0 || unreadableEncryptedCount > 0)
        {
            throw new InvalidOperationException(
                $"{fieldName} migration left {nonEncryptedCount} non-encv2 secret value(s) and {unreadableEncryptedCount} unreadable encv2 secret value(s). Run the migration again or ask an administrator to re-enter affected API keys.");
        }
    }

    internal static void EnsureReadableEncryptedSecrets(
        IEnumerable<string?> storedValues,
        string fieldName)
    {
        var unreadableEncryptedCount = 0;

        foreach (var storedValue in storedValues)
        {
            if (string.IsNullOrWhiteSpace(storedValue) || !SecretStringEncryptor.IsEncrypted(storedValue))
            {
                continue;
            }

            try
            {
                _ = SecretStringEncryptor.Decrypt(storedValue);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Environment variable", StringComparison.Ordinal))
            {
                throw;
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException)
            {
                unreadableEncryptedCount++;
            }
        }

        if (unreadableEncryptedCount > 0)
        {
            throw new InvalidOperationException(
                $"{fieldName} migration found {unreadableEncryptedCount} unreadable encv2 secret value(s) before writing migrated secrets. Run the migration again or ask an administrator to re-enter affected API keys.");
        }
    }
}

internal sealed record SecretMigrationResult(
    int LanguageModelLegacyCipherCount,
    int LanguageModelPlaintextCount,
    int EmbeddingModelLegacyCipherCount,
    int EmbeddingModelPlaintextCount)
{
    public bool HasChanges =>
        LanguageModelLegacyCipherCount > 0 ||
        LanguageModelPlaintextCount > 0 ||
        EmbeddingModelLegacyCipherCount > 0 ||
        EmbeddingModelPlaintextCount > 0;
}

internal sealed record SecretMigrationSectionResult(
    int LegacyCipherCount,
    int PlaintextCount)
{
    public bool HasChanges => LegacyCipherCount > 0 || PlaintextCount > 0;
}
