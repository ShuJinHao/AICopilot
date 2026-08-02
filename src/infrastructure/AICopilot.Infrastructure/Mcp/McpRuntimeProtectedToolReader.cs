using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.Infrastructure.Mcp;

internal sealed class McpRuntimeProtectedToolReader(
    IMcpToolRegistryReadService toolRegistryReadService)
{
    public async Task<HashSet<string>> LoadProtectedToolNamesAsync(
        string serverName,
        CancellationToken cancellationToken)
    {
        var registrations = await toolRegistryReadService.GetMcpToolRegistrationsAsync(cancellationToken);
        return registrations
            .Where(registration =>
                registration.IsEnabled &&
                registration.RequiresApproval &&
                string.Equals(registration.ServerName, serverName, StringComparison.OrdinalIgnoreCase))
            .Select(registration => registration.ToolName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
