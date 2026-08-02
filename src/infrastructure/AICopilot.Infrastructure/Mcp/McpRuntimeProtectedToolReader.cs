using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.Infrastructure.Mcp;

internal sealed record McpRuntimeToolGovernance(
    string ToolCode,
    AiToolRiskLevel RiskLevel,
    bool RequiresApproval,
    string? RequiredPermission,
    string AuditLevel,
    string DataBoundary,
    int SchemaVersion,
    int TimeoutSeconds);

internal sealed class McpRuntimeToolGovernanceReader(
    IMcpToolRegistryReadService toolRegistryReadService)
{
    public async Task<IReadOnlyDictionary<string, McpRuntimeToolGovernance>> LoadAsync(
        string serverName,
        CancellationToken cancellationToken)
    {
        var registrations = await toolRegistryReadService.GetMcpToolRegistrationsAsync(cancellationToken);
        return registrations
            .Where(registration =>
                string.Equals(registration.ServerName, serverName, StringComparison.OrdinalIgnoreCase))
            .Select(TryMap)
            .Where(governance => governance is not null)
            .Cast<McpRuntimeToolGovernance>()
            .GroupBy(governance => governance.ToolCode, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
    }

    private static McpRuntimeToolGovernance? TryMap(McpToolRegistryReadModel registration)
    {
        return Enum.TryParse<AiToolRiskLevel>(
                   registration.RiskLevel,
                   ignoreCase: false,
                   out var riskLevel) &&
               !string.IsNullOrWhiteSpace(registration.AuditLevel) &&
               !string.IsNullOrWhiteSpace(registration.DataBoundary) &&
               registration.SchemaVersion > 0 &&
               registration.TimeoutSeconds is >= 1 and <= 600
            ? new McpRuntimeToolGovernance(
                registration.ToolCode,
                riskLevel,
                registration.RequiresApproval,
                registration.RequiredPermission,
                registration.AuditLevel,
                registration.DataBoundary,
                registration.SchemaVersion,
                registration.TimeoutSeconds)
            : null;
    }
}
