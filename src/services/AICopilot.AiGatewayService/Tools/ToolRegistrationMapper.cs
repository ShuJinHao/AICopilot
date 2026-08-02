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
            tool.IsExecutableByAgent,
            tool.SchemaVersion,
            tool.CatalogVersion,
            tool.CreatedAt,
            tool.UpdatedAt,
            runtimeAvailable,
            tool.ProviderType == ToolProviderType.Mcp ? tool.UpdatedAt : null,
            tool.ProviderType == ToolProviderType.Mcp ? tool.TargetName : null);
    }
}
