using AICopilot.EntityFrameworkCore;
using AICopilot.Security.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AICopilot.MigrationWorkApp;

internal static class MigrationWorkerSecretMigrator
{
    public static async Task<SecretMigrationResult> MigrateAsync(
        AiGatewayDbContext aiGatewayDbContext,
        RagDbContext ragDbContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(aiGatewayDbContext);
        ArgumentNullException.ThrowIfNull(ragDbContext);

        var strategy = aiGatewayDbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await aiGatewayDbContext.Database.OpenConnectionAsync(cancellationToken);
            await using var transaction = await aiGatewayDbContext.Database.BeginTransactionAsync(cancellationToken);
            var committed = false;

            try
            {
                var ragOptions = new DbContextOptionsBuilder<RagDbContext>()
                    .UseNpgsql(aiGatewayDbContext.Database.GetDbConnection())
                    .Options;

                await using var transactionalRagDbContext = new RagDbContext(ragOptions);
                await transactionalRagDbContext.Database.UseTransactionAsync(
                    transaction.GetDbTransaction(),
                    cancellationToken);

                var result = await MigrateInCurrentTransactionAsync(
                    aiGatewayDbContext,
                    transactionalRagDbContext,
                    cancellationToken);

                await transaction.CommitAsync(cancellationToken);
                committed = true;

                return result;
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

    private static async Task<SecretMigrationResult> MigrateInCurrentTransactionAsync(
        AiGatewayDbContext aiGatewayDbContext,
        RagDbContext ragDbContext,
        CancellationToken cancellationToken)
    {
        var languageModels = await aiGatewayDbContext.LanguageModels.ToListAsync(cancellationToken);
        var embeddingModels = await ragDbContext.EmbeddingModels.ToListAsync(cancellationToken);

        var languageResult = SecretMigrationPolicy.MigrateLanguageModelApiKeys(languageModels);
        var embeddingResult = SecretMigrationPolicy.MigrateEmbeddingModelApiKeys(embeddingModels);

        if (languageResult.HasChanges)
        {
            await aiGatewayDbContext.SaveChangesAsync(cancellationToken);
        }

        if (embeddingResult.HasChanges)
        {
            await ragDbContext.SaveChangesAsync(cancellationToken);
        }

        SecretMigrationPolicy.EnsureMigratedSecrets(
            languageModels.Select(model => model.ApiKey),
            "LanguageModel.ApiKey");
        SecretMigrationPolicy.EnsureMigratedSecrets(
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

        SecretMigrationPolicy.EnsureMigratedSecrets(languageApiKeys, "LanguageModel.ApiKey");
        SecretMigrationPolicy.EnsureMigratedSecrets(embeddingApiKeys, "EmbeddingModel.ApiKey");
    }
}
