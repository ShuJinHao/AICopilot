using AICopilot.AgentWorkflowTestKit;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Infrastructure.Mcp;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.InProcessTests;

public sealed class McpToolRegistrySynchronizerTests : ToolRegistryGovernanceTestBase
{
    [Fact]
    public async Task McpToolRegistrySynchronizer_ShouldRejectInvalidSchemaBeforeRegistration_AndDisablePriorVersion()
    {
        var repository = new InMemoryRepository<ToolRegistration>();
        var synchronizer = new McpToolRegistrySynchronizer(repository);
        var invalid = new McpDiscoveredToolRegistration(
            "mcp__runtime_mcp__read",
            "read",
            "Invalid open schema.",
            """{"type":"object","additionalProperties":true}""",
            """{"type":"object"}""",
            AiToolRiskLevel.Low);

        await synchronizer.UpsertDiscoveredToolsAsync(
            "runtime-mcp",
            [invalid],
            CancellationToken.None);

        repository.Items.Should().BeEmpty();

        await synchronizer.UpsertDiscoveredToolsAsync(
            "runtime-mcp",
            [invalid with
            {
                Description = "Valid strict schema.",
                InputSchemaJson = """{ "type":"object", "properties":{}, "additionalProperties":false }""",
                OutputSchemaJson = """{ "type":"object", "properties":{"ok":{"type":"boolean"}}, "required":["ok"], "additionalProperties":false }"""
            }],
            CancellationToken.None);
        var tool = repository.Items.Should().ContainSingle().Which;
        tool.InputSchemaJson.Should().Be(
            AgentCanonicalJsonV1.Canonicalize(tool.InputSchemaJson));
        tool.OutputSchemaJson.Should().Be(
            AgentCanonicalJsonV1.Canonicalize(tool.OutputSchemaJson));
        tool.Update(
            tool.DisplayName,
            tool.Description,
            tool.ProviderType,
            tool.TargetType,
            tool.TargetName,
            tool.InputSchemaJson,
            tool.OutputSchemaJson,
            tool.RiskLevel,
            tool.RequiredPermission,
            tool.RequiresApproval,
            isEnabled: true,
            tool.TimeoutSeconds,
            tool.AuditLevel,
            DateTimeOffset.UtcNow);

        var invalidOutput = invalid with
        {
            InputSchemaJson = """{"type":"object","properties":{},"additionalProperties":false}""",
            OutputSchemaJson = "{}"
        };
        await synchronizer.UpsertDiscoveredToolsAsync(
            "runtime-mcp",
            [invalidOutput],
            CancellationToken.None);

        tool.IsEnabled.Should().BeFalse();
        tool.IsExecutableByAgent.Should().BeFalse();
        tool.RequiresApproval.Should().BeTrue();
        tool.SchemaVersion.Should().Be(2);
        tool.CatalogVersion.Should().Be(2);
        tool.ApprovalPolicy.Should().Be("RediscoveryReviewRequired");
        var quarantinedAt = tool.UpdatedAt;

        await synchronizer.UpsertDiscoveredToolsAsync(
            "runtime-mcp",
            [invalidOutput],
            CancellationToken.None);

        tool.SchemaVersion.Should().Be(2, "repeated invalid discovery must not churn versions");
        tool.CatalogVersion.Should().Be(2);
        tool.UpdatedAt.Should().Be(quarantinedAt);
    }

    [Fact]
    public async Task McpToolRegistrySynchronizer_ShouldQuarantineGovernedDrift_AndPreserveAdminMetadata()
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

        tool.IsEnabled.Should().BeFalse();
        tool.IsExecutableByAgent.Should().BeFalse();
        tool.RequiresApproval.Should().BeTrue();
        tool.RequiredPermission.Should().Be("AiGateway.ToolRegistry.Manage");
        tool.AuditLevel.Should().Be(ToolAuditLevel.Verbose);
        tool.InputSchemaJson.Should().Contain("\"input\"");
        tool.RiskLevel.Should().Be(AiToolRiskLevel.RequiresApproval);
        tool.SchemaVersion.Should().Be(2);
        tool.CatalogVersion.Should().Be(2);
        tool.ApprovalPolicy.Should().Be("RediscoveryReviewRequired");

        await synchronizer.UpsertDiscoveredToolsAsync(
            "runtime-mcp",
            [
                new McpDiscoveredToolRegistration(
                    "mcp__runtime_mcp__read",
                    "read",
                    "Same governed contract after rediscovery.",
                    """{"type":"object","properties":{"input":{"type":"string"}}}""",
                    """{"type":"object"}""",
                    AiToolRiskLevel.RequiresApproval)
            ],
            CancellationToken.None);

        tool.SchemaVersion.Should().Be(2, "an identical governed contract must not churn versions");
        tool.CatalogVersion.Should().Be(2);
        tool.IsEnabled.Should().BeFalse("rediscovery cannot silently clear the review quarantine");

        await synchronizer.UpsertDiscoveredToolsAsync(
            "replacement-mcp",
            [
                new McpDiscoveredToolRegistration(
                    "mcp__runtime_mcp__read",
                    "read",
                    "Target drift must be reviewed.",
                    """{"type":"object","properties":{"input":{"type":"string"}}}""",
                    """{"type":"object"}""",
                    AiToolRiskLevel.Medium)
            ],
            CancellationToken.None);

        tool.TargetName.Should().Be("replacement-mcp");
        tool.RiskLevel.Should().Be(
            AiToolRiskLevel.RequiresApproval,
            "risk precedence is explicit because AiToolRiskLevel enum values are not severity ordered");
        tool.SchemaVersion.Should().Be(3);
        tool.CatalogVersion.Should().Be(3);
        tool.IsEnabled.Should().BeFalse();
        tool.IsExecutableByAgent.Should().BeFalse();
        tool.RequiresApproval.Should().BeTrue();
    }
}
