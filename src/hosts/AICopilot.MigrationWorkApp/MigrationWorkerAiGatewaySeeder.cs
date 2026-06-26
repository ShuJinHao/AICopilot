using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Aggregates.RoutingModel;
using AICopilot.Core.AiGateway.Aggregates.RuntimeSettings;
using AICopilot.Core.AiGateway.Aggregates.Skills;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.Security;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.MigrationWorkApp;

internal static class MigrationWorkerAiGatewaySeeder
{
    public const string PrivateMiniMaxProvider = "MiniMax Private";
    public const string PrivateMiniMaxModelName = "MiniMax-M3-AWQ-INT4";
    public const string PrivateMiniMaxBaseUrl = "http://10.98.200.20:40034/v1";
    public const string PrivateMiniMaxPlaceholderApiKey = "dummy-key";
    public const string PrivateMiniMaxRoutingConfigurationName = "MiniMax private routing model";

    private static readonly LanguageModelUsage PrivateMiniMaxUsages =
        LanguageModelUsage.Chat | LanguageModelUsage.Routing | LanguageModelUsage.Planner;

    private static readonly string[] ForcedPrivateMiniMaxTemplateCodes =
    [
        "IntentRoutingAgent",
        "agent_planner",
        "agent_executor"
    ];

    public static async Task SeedDefaultsAsync(
        AiGatewayDbContext aiGatewayDbContext,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        if (!await aiGatewayDbContext.ChatRuntimeSettings.AnyAsync(
                settings => settings.Id == ChatRuntimeSettings.GlobalId,
                cancellationToken))
        {
            aiGatewayDbContext.ChatRuntimeSettings.Add(ChatRuntimeSettings.CreateDefault(now));
        }

        var obsoleteExampleModel = await aiGatewayDbContext.LanguageModels.FirstOrDefaultAsync(
            model => model.Provider == "Example" && model.Name == "Disabled example model",
            cancellationToken);

        var privateMiniMaxModel = await aiGatewayDbContext.LanguageModels.FirstOrDefaultAsync(
            model => model.Provider == PrivateMiniMaxProvider && model.Name == PrivateMiniMaxModelName,
            cancellationToken);
        if (privateMiniMaxModel is null)
        {
            privateMiniMaxModel = new LanguageModel(
                PrivateMiniMaxProvider,
                PrivateMiniMaxModelName,
                PrivateMiniMaxBaseUrl,
                ProtectSeedApiKey(PrivateMiniMaxPlaceholderApiKey),
                new ModelParameters
                {
                    MaxTokens = 32768,
                    MaxOutputTokens = 4096,
                    Temperature = 0.2f
                },
                LanguageModelProtocolTypes.OpenAICompatible,
                PrivateMiniMaxUsages,
                isEnabled: true);
            aiGatewayDbContext.LanguageModels.Add(privateMiniMaxModel);
        }
        else
        {
            privateMiniMaxModel.UpdateInfo(
                PrivateMiniMaxProvider,
                PrivateMiniMaxModelName,
                PrivateMiniMaxBaseUrl,
                LanguageModelProtocolTypes.OpenAICompatible);
            EnsureSeedApiKey(privateMiniMaxModel);
            privateMiniMaxModel.UpdateParameters(new ModelParameters
            {
                MaxTokens = 32768,
                MaxOutputTokens = 4096,
                Temperature = 0.2f
            });
            privateMiniMaxModel.UpdateRuntimeFlags(PrivateMiniMaxUsages, isEnabled: true);
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

            var modelId = ShouldBindTemplateToPrivateMiniMax(definition, template, obsoleteExampleModel)
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

        var defaultPolicy = await aiGatewayDbContext.ApprovalPolicies.FirstOrDefaultAsync(
            policy => policy.Name == "Default Agent Artifact Approval",
            cancellationToken);
        var highRiskTools = new[]
        {
            "generate_pdf",
            "generate_pptx",
            "generate_xlsx",
            "finalize_artifacts"
        };
        if (defaultPolicy is null)
        {
            aiGatewayDbContext.ApprovalPolicies.Add(new ApprovalPolicy(
                "Default Agent Artifact Approval",
                "Default approval gate for generated files and final output.",
                ApprovalTargetType.Plugin,
                "AgentTaskRuntime",
                highRiskTools,
                isEnabled: true,
                requiresOnsiteAttestation: false));
        }
        else
        {
            defaultPolicy.Update(
                "Default Agent Artifact Approval",
                "Default approval gate for generated files and final output.",
                ApprovalTargetType.Plugin,
                "AgentTaskRuntime",
                highRiskTools,
                isEnabled: true,
                requiresOnsiteAttestation: false);
        }

        var obsoleteToolCodes = BuiltInToolRegistrations.ObsoleteAgentRuntimeToolCodes.ToArray();
        var obsoleteTools = await aiGatewayDbContext.ToolRegistrations
            .Where(tool => obsoleteToolCodes.Contains(tool.ToolCode))
            .ToListAsync(cancellationToken);
        aiGatewayDbContext.ToolRegistrations.RemoveRange(obsoleteTools);

        foreach (var definition in BuiltInToolRegistrations.AgentRuntimeTools)
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
                    definition.IsVisibleToPlanner,
                    definition.IsExecutableByAgent,
                    definition.SchemaVersion,
                    definition.CatalogVersion,
                    definition.ApprovalPolicy));
                continue;
            }

            if (ProtectedCloudReadonlyToolPolicy.IsProtected(tool.ToolCode))
            {
                ProtectedCloudReadonlyToolPolicy.ForceDisabled(tool, now);
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
                ResolveBuiltInRequiredPermission(tool.RequiredPermission, definition),
                tool.RequiresApproval,
                tool.IsEnabled,
                tool.TimeoutSeconds,
                tool.AuditLevel,
                now,
                definition.Category,
                definition.BusinessDomains,
                definition.DataBoundary,
                definition.IsVisibleToPlanner && tool.IsVisibleToPlanner,
                definition.IsExecutableByAgent && tool.IsExecutableByAgent,
                definition.SchemaVersion,
                definition.CatalogVersion,
                string.IsNullOrWhiteSpace(tool.ApprovalPolicy) ? definition.ApprovalPolicy : tool.ApprovalPolicy);
        }

        await SeedSkillDefinitionsAsync(aiGatewayDbContext, now, cancellationToken);

        var routingConfigurations = await aiGatewayDbContext.RoutingModelConfigurations
            .ToListAsync(cancellationToken);
        var privateMiniMaxRoutingConfiguration = routingConfigurations.FirstOrDefault(configuration =>
            configuration.ModelId == privateMiniMaxModel.Id ||
            string.Equals(configuration.Name, PrivateMiniMaxRoutingConfigurationName, StringComparison.Ordinal));
        foreach (var configuration in routingConfigurations.Where(configuration => configuration.IsActive))
        {
            configuration.Deactivate();
        }

        if (privateMiniMaxRoutingConfiguration is null)
        {
            aiGatewayDbContext.RoutingModelConfigurations.Add(new RoutingModelConfiguration(
                PrivateMiniMaxRoutingConfigurationName,
                privateMiniMaxModel.Id,
                isActive: true));
        }
        else
        {
            privateMiniMaxRoutingConfiguration.Update(
                PrivateMiniMaxRoutingConfigurationName,
                privateMiniMaxModel.Id);
            privateMiniMaxRoutingConfiguration.Activate();
        }

        if (obsoleteExampleModel is not null)
        {
            aiGatewayDbContext.LanguageModels.Remove(obsoleteExampleModel);
        }

        await aiGatewayDbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedSkillDefinitionsAsync(
        AiGatewayDbContext aiGatewayDbContext,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var trackedToolEntries = aiGatewayDbContext.ChangeTracker
            .Entries<ToolRegistration>()
            .ToArray();
        var deletedToolCodes = trackedToolEntries
            .Where(entry => entry.State == EntityState.Deleted)
            .Select(entry => entry.Entity.ToolCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var trackedActiveToolCodes = trackedToolEntries
            .Where(entry => entry.State != EntityState.Deleted)
            .Select(entry => entry.Entity.ToolCode);
        var persistedToolCodes = await aiGatewayDbContext.ToolRegistrations
            .Select(tool => tool.ToolCode)
            .ToListAsync(cancellationToken);
        var existingToolCodeSet = persistedToolCodes
            .Concat(trackedActiveToolCodes)
            .Where(toolCode => !deletedToolCodes.Contains(toolCode))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in BuiltInSkillDefinitions.All)
        {
            var allowedToolCodes = definition.AllowedToolCodes
                .Where(existingToolCodeSet.Contains)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (allowedToolCodes.Length == 0)
            {
                continue;
            }

            var skill = await aiGatewayDbContext.SkillDefinitions.FirstOrDefaultAsync(
                item => item.SkillCode == definition.SkillCode,
                cancellationToken);
            if (skill is null)
            {
                aiGatewayDbContext.SkillDefinitions.Add(new SkillDefinition(
                    definition.SkillCode,
                    definition.DisplayName,
                    definition.Description,
                    allowedToolCodes,
                    definition.RiskLevel,
                    definition.ApprovalPolicy,
                    definition.AllowedDataSourceModes,
                    definition.AllowedKnowledgeScopes,
                    definition.OutputComponentTypes,
                    definition.IsEnabled,
                    isBuiltIn: true,
                    definition.Version,
                    now));
                continue;
            }

            skill.Update(
                definition.DisplayName,
                definition.Description,
                allowedToolCodes,
                definition.RiskLevel,
                definition.ApprovalPolicy,
                definition.AllowedDataSourceModes,
                definition.AllowedKnowledgeScopes,
                definition.OutputComponentTypes,
                skill.IsEnabled && definition.IsEnabled,
                isBuiltIn: true,
                definition.Version,
                now);
        }
    }

    private static bool ShouldBindTemplateToPrivateMiniMax(
        BuiltInConversationTemplateDefinition definition,
        ConversationTemplate template,
        LanguageModel? obsoleteExampleModel)
    {
        return ForcedPrivateMiniMaxTemplateCodes.Contains(definition.Code, StringComparer.OrdinalIgnoreCase) ||
               (obsoleteExampleModel is not null && template.ModelId == obsoleteExampleModel.Id);
    }

    private static void EnsureSeedApiKey(LanguageModel model)
    {
        if (string.IsNullOrWhiteSpace(model.ApiKey))
        {
            model.UpdateApiKey(ProtectSeedApiKey(PrivateMiniMaxPlaceholderApiKey));
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

    private static string? ResolveBuiltInRequiredPermission(
        string? currentPermission,
        ToolRegistrationSeed definition)
    {
        if (definition.ToolCode == "query_business_database_readonly" &&
            string.Equals(currentPermission, "DataSource.Query", StringComparison.Ordinal))
        {
            return definition.RequiredPermission;
        }

        return currentPermission;
    }
}
