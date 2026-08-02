using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Globalization;

namespace AICopilot.MigrationWorkApp;

internal static class MigrationWorkerAiGatewaySeeder
{
    public const string PrivateMiniMaxProvider = "MiniMax Private";
    public const string PrivateMiniMaxModelName = "MiniMax-M3-AWQ-INT4";
    public const string PrivateMiniMaxDefaultBaseUrl = "http://model.internal.example:40034/v1";
    public const int PrivateMiniMaxContextWindowTokens = 65536;
    private static readonly LanguageModelUsage PrivateMiniMaxUsages =
        LanguageModelUsage.Chat;

    public static async Task SeedDefaultsAsync(
        AiGatewayDbContext aiGatewayDbContext,
        IConfiguration? configuration,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var privateModelSeed = PrivateModelSeedOptions.Load(configuration);

        var obsoleteExampleModel = await aiGatewayDbContext.LanguageModels.FirstOrDefaultAsync(
            model => model.Provider == "Example" && model.Name == "Disabled example model",
            cancellationToken);

        var privateMiniMaxModel = await aiGatewayDbContext.LanguageModels.FirstOrDefaultAsync(
            model => model.Provider == privateModelSeed.Provider && model.Name == privateModelSeed.ModelName,
            cancellationToken);
        if (privateMiniMaxModel is null)
        {
            privateMiniMaxModel = new LanguageModel(
                privateModelSeed.Provider,
                privateModelSeed.ModelName,
                privateModelSeed.BaseUrl,
                ProtectSeedApiKey(privateModelSeed.ApiKey),
                new ModelParameters
                {
                    MaxTokens = privateModelSeed.ContextWindowTokens,
                    MaxOutputTokens = privateModelSeed.MaxOutputTokens,
                    Temperature = privateModelSeed.Temperature
                },
                LanguageModelProtocolTypes.OpenAICompatible,
                PrivateMiniMaxUsages,
                isEnabled: privateModelSeed.Enabled);
            aiGatewayDbContext.LanguageModels.Add(privateMiniMaxModel);
        }
        else
        {
            EnsureSeedApiKey(privateMiniMaxModel);
        }

        foreach (var definition in BuiltInConversationTemplates.All)
        {
            var template = await aiGatewayDbContext.ConversationTemplates.FirstOrDefaultAsync(
                item => item.Code == definition.Code,
                cancellationToken);
            if (template is null)
            {
                aiGatewayDbContext.ConversationTemplates.Add(
                    BuiltInConversationTemplates.CreateTemplate(definition, privateMiniMaxModel.Id));
                continue;
            }

            var modelId = obsoleteExampleModel is not null && template.ModelId == obsoleteExampleModel.Id
                ? privateMiniMaxModel.Id
                : template.ModelId;

            template.UpdateInfo(
                definition.Name,
                definition.Description,
                definition.SystemPrompt,
                modelId,
                template.IsEnabled);
            template.MarkBuiltIn(definition.Code, definition.Scope, definition.Version);
        }

        foreach (var definition in BuiltInToolRegistrations.HarnessTools)
        {
            var tool = await aiGatewayDbContext.ToolRegistrations.FirstOrDefaultAsync(
                item => item.ToolCode == definition.ToolCode,
                cancellationToken);
            if (tool is null)
            {
                aiGatewayDbContext.ToolRegistrations.Add(new ToolRegistration(
                    definition.ToolCode,
                    definition.DisplayName,
                    definition.Description,
                    definition.ProviderType,
                    definition.TargetType,
                    definition.TargetName,
                    definition.InputSchemaJson,
                    definition.OutputSchemaJson,
                    definition.RiskLevel,
                    definition.RequiredPermission,
                    definition.RequiresApproval,
                    definition.IsEnabled,
                    definition.TimeoutSeconds,
                    definition.AuditLevel,
                    now,
                    definition.Category,
                    definition.BusinessDomains,
                    definition.DataBoundary,
                    definition.IsExecutableByAgent,
                    definition.SchemaVersion,
                    definition.CatalogVersion));
                continue;
            }

            tool.Update(
                definition.DisplayName,
                definition.Description,
                definition.ProviderType,
                definition.TargetType,
                definition.TargetName,
                definition.InputSchemaJson,
                definition.OutputSchemaJson,
                tool.RiskLevel,
                definition.RequiredPermission,
                tool.RequiresApproval,
                tool.IsEnabled,
                tool.TimeoutSeconds,
                tool.AuditLevel,
                now,
                definition.Category,
                definition.BusinessDomains,
                definition.DataBoundary,
                definition.IsExecutableByAgent && tool.IsExecutableByAgent,
                definition.SchemaVersion,
                definition.CatalogVersion);
        }

        if (obsoleteExampleModel is not null)
        {
            aiGatewayDbContext.LanguageModels.Remove(obsoleteExampleModel);
        }

        await aiGatewayDbContext.SaveChangesAsync(cancellationToken);
    }

    private static void EnsureSeedApiKey(LanguageModel model)
    {
        if (string.IsNullOrWhiteSpace(model.ApiKey))
        {
            return;
        }

        if (SecretStringEncryptor.IsLegacyEncrypted(model.ApiKey))
        {
            model.UpdateApiKey(SecretStringEncryptor.ReEncryptLegacyCipher(model.ApiKey));
            model.ResetConnectivityStatus();
            return;
        }

        if (!SecretStringEncryptor.IsEncrypted(model.ApiKey))
        {
            model.UpdateApiKey(ProtectSeedApiKey(model.ApiKey));
            model.ResetConnectivityStatus();
        }
    }

    private static string? ProtectSeedApiKey(string? apiKey)
    {
        return SecretStringEncryptor.Encrypt(apiKey);
    }

    private sealed record PrivateModelSeedOptions(
        string Provider,
        string ModelName,
        string BaseUrl,
        string? ApiKey,
        int ContextWindowTokens,
        int MaxOutputTokens,
        float Temperature,
        bool Enabled)
    {
        public static PrivateModelSeedOptions Load(IConfiguration? configuration)
        {
            var provider = Read(configuration, "Provider", "AICOPILOT_PRIVATE_MODEL_PROVIDER")
                ?? PrivateMiniMaxProvider;
            var modelName = Read(configuration, "ModelName", "AICOPILOT_PRIVATE_MODEL_NAME")
                ?? PrivateMiniMaxModelName;
            var baseUrl = Read(configuration, "BaseUrl", "AICOPILOT_PRIVATE_MODEL_BASE_URL")
                ?? PrivateMiniMaxDefaultBaseUrl;
            var apiKey = Read(configuration, "ApiKey", "AICOPILOT_PRIVATE_MODEL_API_KEY");
            var contextWindowTokens = ReadInt(
                configuration,
                "ContextWindowTokens",
                "AICOPILOT_PRIVATE_MODEL_CONTEXT_TOKENS",
                PrivateMiniMaxContextWindowTokens);
            var maxOutputTokens = ReadInt(
                configuration,
                "MaxOutputTokens",
                "AICOPILOT_PRIVATE_MODEL_MAX_OUTPUT_TOKENS",
                4096);
            var temperature = ReadFloat(
                configuration,
                "Temperature",
                "AICOPILOT_PRIVATE_MODEL_TEMPERATURE",
                0.2f);
            var enabled = ReadBool(configuration, "Enabled", "AICOPILOT_PRIVATE_MODEL_ENABLED", false);

            return new PrivateModelSeedOptions(
                provider.Trim(),
                modelName.Trim(),
                baseUrl.Trim(),
                apiKey?.Trim(),
                contextWindowTokens,
                maxOutputTokens,
                temperature,
                enabled);
        }

        private static string? Read(IConfiguration? configuration, string key, string environmentVariable)
        {
            var fromEnvironment = Environment.GetEnvironmentVariable(environmentVariable);
            if (!string.IsNullOrWhiteSpace(fromEnvironment))
            {
                return fromEnvironment;
            }

            var fromConfiguration = configuration?[$"AICopilot:PrivateModel:{key}"];
            return string.IsNullOrWhiteSpace(fromConfiguration) ? null : fromConfiguration;
        }

        private static int ReadInt(
            IConfiguration? configuration,
            string key,
            string environmentVariable,
            int fallback)
        {
            var value = Read(configuration, key, environmentVariable);
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            return int.TryParse(value, out var parsed) && parsed > 0
                ? parsed
                : throw new InvalidOperationException($"AICopilot:PrivateModel:{key} must be a positive integer.");
        }

        private static float ReadFloat(
            IConfiguration? configuration,
            string key,
            string environmentVariable,
            float fallback)
        {
            var value = Read(configuration, key, environmentVariable);
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : throw new InvalidOperationException($"AICopilot:PrivateModel:{key} must be a number.");
        }

        private static bool ReadBool(
            IConfiguration? configuration,
            string key,
            string environmentVariable,
            bool fallback)
        {
            var value = Read(configuration, key, environmentVariable);
            return string.IsNullOrWhiteSpace(value) ? fallback : bool.Parse(value);
        }
    }
}
