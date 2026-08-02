using AICopilot.Services.Contracts;

namespace AICopilot.HarnessTestKit;

internal sealed class TestMcpToolRegistryReadService(
    params McpToolRegistryReadModel[] registrations) : IMcpToolRegistryReadService
{
    public Task<IReadOnlyCollection<McpToolRegistryReadModel>> GetMcpToolRegistrationsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<McpToolRegistryReadModel>>(registrations);
}
