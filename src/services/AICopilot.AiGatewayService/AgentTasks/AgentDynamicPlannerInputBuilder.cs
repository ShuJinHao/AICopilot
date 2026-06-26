using System.Text.Json;
using AICopilot.AiGatewayService.Safety;
using AICopilot.AiGatewayService.Tools;

namespace AICopilot.AiGatewayService.AgentTasks;

internal static class AgentDynamicPlannerInputBuilder
{
    internal static object Build(AgentDynamicPlannerRequest request)
    {
        return new
        {
            goal = SanitizePlannerField(request.Goal, 2000),
            taskType = request.TaskType.ToString(),
            uploadIds = request.UploadIds.Select(id => id.ToString("N")).ToArray(),
            knowledgeBaseIds = request.KnowledgeBaseIds.Select(id => id.ToString("N")).ToArray(),
            dataSources = (request.DataSources ?? []).Select(source => new
            {
                id = source.Id.ToString("N"),
                name = SanitizePlannerField(source.Name, 160),
                externalSystemType = SanitizePlannerField(source.ExternalSystemType, 80),
                businessDomain = SanitizePlannerField(source.BusinessDomain, 120),
                isSimulation = source.IsSimulation,
                sourceLabel = SanitizePlannerField(source.SourceLabel, 160)
            }),
            businessDomains = (request.BusinessDomains ?? [])
                .Select(domain => SanitizePlannerField(domain, 120))
                .Where(domain => !string.IsNullOrWhiteSpace(domain))
                .ToArray(),
            queryMode = SanitizePlannerField(request.QueryMode, 80),
            artifactTypes = (request.ArtifactTypes ?? [])
                .Select(type => SanitizePlannerField(type, 40))
                .Where(type => !string.IsNullOrWhiteSpace(type))
                .ToArray(),
            trialScenario = string.IsNullOrWhiteSpace(request.TrialScenarioId)
                ? null
                : new
                {
                    id = SanitizePlannerField(request.TrialScenarioId, 160),
                    title = SanitizePlannerField(request.TrialScenarioTitle, 200),
                    isSimulationOnly = request.IsSimulationTrial
                },
            plannerToolCatalog = new
            {
                version = request.ToolCatalog.Version,
                availableToolCount = request.ToolCatalog.AvailableToolCount,
                mockMcpOnly = true,
                riskSummary = request.ToolCatalog.Tools
                    .GroupBy(tool => tool.RiskLevel, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase)
            },
            tools = request.ToolCatalog.Tools.Select(tool => new
            {
                toolCode = SanitizePlannerField(tool.ToolCode, 160),
                displayName = SanitizePlannerField(tool.DisplayName, 160),
                description = SanitizePlannerField(tool.Description, 1000),
                providerType = tool.ProviderType,
                providerKind = tool.ProviderKind,
                isMock = tool.IsMock,
                category = SanitizePlannerField(tool.Category, 120),
                businessDomains = (tool.BusinessDomains ?? [])
                    .Select(domain => SanitizePlannerField(domain, 120))
                    .Where(domain => !string.IsNullOrWhiteSpace(domain))
                    .ToArray(),
                dataBoundary = tool.DataBoundary,
                targetType = tool.TargetType,
                targetName = SanitizePlannerField(tool.TargetName, 200),
                riskLevel = tool.RiskLevel,
                requiresApproval = tool.RequiresApproval,
                approvalPolicy = SanitizePlannerField(tool.ApprovalPolicy, 120),
                schemaVersion = tool.SchemaVersion,
                catalogVersion = tool.CatalogVersion,
                timeoutSeconds = tool.TimeoutSeconds,
                auditLevel = tool.AuditLevel,
                runtimeAvailable = tool.RuntimeAvailable,
                inputSchema = tool.InputSchema ?? BuildFallbackSchemaSummary(tool.InputSchemaJson),
                outputSchema = tool.OutputSchema ?? BuildFallbackSchemaSummary(null)
            }),
            constraints = new
            {
                maxSteps = AgentDynamicPlannerLimits.MaxDynamicSteps,
                output = "json_only",
                cloudIntent = "backend_only",
                simulationOnly = request.IsSimulationTrial || (request.DataSources ?? []).Any(source => source.IsSimulation),
                mockMcpOnly = true,
                externalMcp = "disabled_in_p4",
                requiresDataApproval = request.RequiresDataApproval,
                forbidden = new[] { "shell", "arbitrary_path", "sql", "cloud_write", "real_external_mcp", "unregistered_tool", "non_simulation_business_source" }
            },
            runtimeSettings = new
            {
                request.RuntimeSettings.AgentPlanningHistoryCount,
                request.RuntimeSettings.ContextTokenLimit
            }
        };
    }

    private static PlannerToolSchemaSummary BuildFallbackSchemaSummary(string? schemaJson)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(schemaJson) ? "{}" : schemaJson);
            var root = document.RootElement;
            var type = root.ValueKind == JsonValueKind.Object &&
                       root.TryGetProperty("type", out var typeElement) &&
                       typeElement.ValueKind == JsonValueKind.String
                ? typeElement.GetString()
                : "object";
            return new PlannerToolSchemaSummary(
                SanitizePlannerField(type, 64) ?? "object",
                [],
                [],
                null,
                !string.IsNullOrWhiteSpace(schemaJson) && schemaJson.Length > 4000);
        }
        catch (JsonException)
        {
            return new PlannerToolSchemaSummary("object", [], [], null, true);
        }
    }

    private static string? SanitizePlannerField(string? value, int maxLength)
    {
        return ToolExecutionRecordSanitizer.Sanitize(
            CloudReadonlyAgentTextGuard.SanitizeForPlan(value, maxLength),
            maxLength);
    }
}
