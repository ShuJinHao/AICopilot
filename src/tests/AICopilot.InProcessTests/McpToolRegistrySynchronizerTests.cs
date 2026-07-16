using AICopilot.AgentWorkflowTestKit;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Infrastructure.Mcp;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.InProcessTests;

public sealed class McpToolRegistrySynchronizerTests : ToolRegistryGovernanceTestBase
{
    [Fact]
    public async Task McpToolRegistrySynchronizer_ShouldUpsertDisabledTools_AndPreserveAdminSettings()
    {
        var repository = new InMemoryRepository<ToolRegistration>();
        var synchronizer = new McpToolRegistrySynchronizer(repository);

        await synchronizer.UpsertDiscoveredToolsAsync(
            "runtime-mcp",
            [
                new McpDiscoveredToolRegistration(
                    "mcp__runtime_mcp__read",
                    "read",
                    "Read MCP data.",
                    """{"type":"object"}""",
                    """{"type":"object"}""",
                    AiToolRiskLevel.Low)
            ],
            CancellationToken.None);

        var tool = repository.Items.Should().ContainSingle().Which;
        tool.ProviderType.Should().Be(ToolProviderType.Mcp);
        tool.TargetType.Should().Be(ToolRegistrationTargetType.McpServer);
        tool.TargetName.Should().Be("runtime-mcp");
        tool.IsEnabled.Should().BeFalse();
        tool.RequiresApproval.Should().BeTrue();

        tool.Update(
            tool.DisplayName,
            tool.Description,
            tool.ProviderType,
            tool.TargetType,
            tool.TargetName,
            tool.InputSchemaJson,
            tool.OutputSchemaJson,
            AiToolRiskLevel.Low,
            "AiGateway.ToolRegistry.Manage",
            requiresApproval: false,
            isEnabled: true,
            tool.TimeoutSeconds,
            ToolAuditLevel.Verbose,
            DateTimeOffset.UtcNow);

        await synchronizer.UpsertDiscoveredToolsAsync(
            "runtime-mcp",
            [
                new McpDiscoveredToolRegistration(
                    "mcp__runtime_mcp__read",
                    "read",
                    "Read MCP data after rediscovery.",
                    """{"type":"object","properties":{"input":{"type":"string"}}}""",
                    """{"type":"object"}""",
                    AiToolRiskLevel.RequiresApproval)
            ],
            CancellationToken.None);

        tool.IsEnabled.Should().BeTrue();
        tool.RequiresApproval.Should().BeFalse();
        tool.RequiredPermission.Should().Be("AiGateway.ToolRegistry.Manage");
        tool.AuditLevel.Should().Be(ToolAuditLevel.Verbose);
        tool.InputSchemaJson.Should().Contain("\"input\"");
    }
}
