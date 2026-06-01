using System.Text.Json;
using AICopilot.AgentPlugin;
using AICopilot.Core.AiGateway.Aggregates.Tools;

namespace AICopilot.AiGatewayService.Tools;

internal static class ToolRegistrationMapper
{
    public static ToolRegistrationDto Map(ToolRegistration tool, IAgentPluginCatalog? pluginCatalog = null)
    {
        var runtimeAvailable = tool.ProviderType != ToolProviderType.Mcp ||
                               pluginCatalog?.GetAllTools().Any(runtimeTool =>
                                   string.Equals(runtimeTool.Name, tool.ToolCode, StringComparison.OrdinalIgnoreCase)) == true;
        return new ToolRegistrationDto(
            tool.Id.Value,
            tool.ToolCode,
            tool.DisplayName,
            tool.Description,
            tool.ProviderType.ToString(),
            tool.TargetType.ToString(),
            tool.TargetName,
            tool.InputSchemaJson,
            tool.OutputSchemaJson,
            tool.RiskLevel.ToString(),
            tool.RequiredPermission,
            tool.RequiresApproval,
            tool.IsEnabled,
            tool.TimeoutSeconds,
            tool.AuditLevel.ToString(),
            tool.Category,
            tool.BusinessDomains,
            tool.DataBoundary.ToString(),
            tool.IsVisibleToPlanner,
            tool.IsExecutableByAgent,
            tool.SchemaVersion,
            tool.CatalogVersion,
            tool.ApprovalPolicy,
            tool.CreatedAt,
            tool.UpdatedAt,
            runtimeAvailable,
            tool.ProviderType == ToolProviderType.Mcp ? tool.UpdatedAt : null,
            tool.ProviderType == ToolProviderType.Mcp ? tool.TargetName : null);
    }

    public static ToolExecutionRecordDto Map(ToolExecutionRecord record)
    {
        var metadata = ParseMetadata(record.AuditMetadata);
        return new ToolExecutionRecordDto(
            record.Id.Value,
            record.TaskId.Value,
            record.StepId.Value,
            record.RunAttemptId?.Value,
            record.ToolCode,
            ToolExecutionRecordSanitizer.Sanitize(record.InputSummary, 2000),
            ToolExecutionRecordSanitizer.Sanitize(record.OutputSummary, 4000),
            record.Status.ToString(),
            record.StartedAt,
            record.CompletedAt,
            record.DurationMs,
            record.ErrorCode,
            ToolExecutionRecordSanitizer.Sanitize(record.ErrorMessage, 2000),
            record.ArtifactId,
            ToolExecutionRecordSanitizer.Sanitize(record.AuditMetadata, 4000),
            metadata.TryGetValue("providerKind", out var providerKind)
                ? providerKind
                : metadata.TryGetValue("providerType", out var providerType)
                    ? providerType
                    : "Unknown",
            metadata.TryGetValue("isMock", out var isMock) && bool.TryParse(isMock, out var parsedIsMock) && parsedIsMock,
            metadata.TryGetValue("approvalStatus", out var approvalStatus) ? approvalStatus : null,
            metadata.TryGetValue("resultHash", out var resultHash) ? resultHash : null);
    }

    private static IReadOnlyDictionary<string, string> ParseMetadata(string? auditMetadata)
    {
        if (string.IsNullOrWhiteSpace(auditMetadata))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            using var document = JsonDocument.Parse(auditMetadata);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            return document.RootElement
                .EnumerateObject()
                .ToDictionary(
                    property => property.Name,
                    property => property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? string.Empty
                        : property.Value.ToString(),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
