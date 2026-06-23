using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Aggregates.RoutingModel;
using AICopilot.Core.AiGateway.Aggregates.RuntimeSettings;
using AICopilot.Core.AiGateway.Aggregates.Skills;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.MigrationWorkApp;

internal static class MigrationWorkerAiGatewaySeeder
{
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

        var seedModel = await aiGatewayDbContext.LanguageModels.FirstOrDefaultAsync(
            model => model.Provider == "Example" && model.Name == "Disabled example model",
            cancellationToken);
        if (seedModel is null)
        {
            seedModel = new LanguageModel(
                "Example",
                "Disabled example model",
                "https://example.invalid/v1",
                null,
                new ModelParameters
                {
                    MaxTokens = 8192,
                    MaxOutputTokens = 1024,
                    Temperature = 0.7f
                },
                LanguageModelProtocolTypes.OpenAICompatible,
                LanguageModelUsage.Chat | LanguageModelUsage.Routing,
                isEnabled: false);
            aiGatewayDbContext.LanguageModels.Add(seedModel);
        }
        else
        {
            seedModel.UpdateInfo(
                "Example",
                "Disabled example model",
                "https://example.invalid/v1",
                LanguageModelProtocolTypes.OpenAICompatible);
            seedModel.UpdateApiKey(null);
            seedModel.UpdateParameters(new ModelParameters
            {
                MaxTokens = 8192,
                MaxOutputTokens = 1024,
                Temperature = 0.7f
            });
            seedModel.UpdateRuntimeFlags(LanguageModelUsage.Chat | LanguageModelUsage.Routing, isEnabled: false);
            seedModel.ResetConnectivityStatus();
        }

        foreach (var definition in BuiltInConversationTemplates.All)
        {
            var template = await aiGatewayDbContext.ConversationTemplates.FirstOrDefaultAsync(
                item => item.Code == definition.Code,
                cancellationToken);
            if (template is null)
            {
                aiGatewayDbContext.ConversationTemplates.Add(
                    BuiltInConversationTemplates.CreateTemplate(definition, seedModel.Id));
                continue;
            }

            template.UpdateInfo(
                definition.Name,
                definition.Description,
                definition.SystemPrompt,
                template.ModelId,
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
        var languageModels = await aiGatewayDbContext.LanguageModels.ToListAsync(cancellationToken);
        var usableRoutingModel = languageModels
            .FirstOrDefault(model => model.IsEnabled && model.SupportsUsage(LanguageModelUsage.Routing));
        var activeConfiguration = routingConfigurations.FirstOrDefault(configuration => configuration.IsActive);
        if (activeConfiguration is not null &&
            languageModels.All(model =>
                model.Id != activeConfiguration.ModelId ||
                !model.IsEnabled ||
                !model.SupportsUsage(LanguageModelUsage.Routing)))
        {
            activeConfiguration.Deactivate();
        }

        if (usableRoutingModel is null)
        {
            foreach (var configuration in routingConfigurations.Where(configuration => configuration.IsActive))
            {
                configuration.Deactivate();
            }
        }
        else if (routingConfigurations.All(configuration => !configuration.IsActive))
        {
            aiGatewayDbContext.RoutingModelConfigurations.Add(new RoutingModelConfiguration(
                "Default routing model",
                usableRoutingModel.Id,
                isActive: true));
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
