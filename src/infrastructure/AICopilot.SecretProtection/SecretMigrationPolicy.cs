using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.EntityFrameworkCore.Security;

namespace AICopilot.Security.Secrets;

public static class SecretMigrationPolicy
{
    public static SecretMigrationSectionResult MigrateLanguageModelApiKeys(
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

    public static SecretMigrationSectionResult MigrateEmbeddingModelApiKeys(
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

    public static void EnsureMigratedSecrets(
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

    public static void EnsureReadableEncryptedSecrets(
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
}

public sealed record SecretMigrationResult(
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

public sealed record SecretMigrationSectionResult(
    int LegacyCipherCount,
    int PlaintextCount)
{
    public bool HasChanges => LegacyCipherCount > 0 || PlaintextCount > 0;
}
