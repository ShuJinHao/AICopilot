namespace AICopilot.Services.Contracts;

public sealed record McpToolRegistryReadModel(
    string ToolCode,
    string ServerName,
    string ToolName,
    bool RuntimeAvailable,
    bool IsEnabled,
    string RiskLevel,
    bool RequiresApproval,
    string? RequiredPermission,
    DateTimeOffset UpdatedAt);

public interface IMcpToolRegistryReadService
{
    Task<IReadOnlyCollection<McpToolRegistryReadModel>> GetMcpToolRegistrationsAsync(
        CancellationToken cancellationToken = default);
}
