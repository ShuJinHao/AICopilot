using AICopilot.HarnessTestKit;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Infrastructure.Mcp;
using AICopilot.SharedKernel.Ai;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace AICopilot.InProcessTests;

public sealed class McpToolRegistrySynchronizerTests
{
    [Fact]
    public async Task McpToolRegistrySynchronizer_ShouldAcceptClosedScalarOutput_AndDisableDeletedTool()
    {
        var repository = new InMemoryRepository<ToolRegistration>();
        var synchronizer = new McpToolRegistrySynchronizer(repository);

        await synchronizer.UpsertDiscoveredToolsAsync(
            "runtime-mcp",
            [
                new McpDiscoveredToolRegistration(
                    "mcp__runtime_mcp__queryecho",
                    "queryEcho",
                    "Return a schema-bound scalar.",
                    """{"type":"object","properties":{},"additionalProperties":false}""",
                    """{"type":"string"}""",
                    AiToolRiskLevel.Low)
            ],
            CancellationToken.None);

        var tool = repository.Items.Should().ContainSingle().Which;
        tool.OutputSchemaJson.Should().Be("""{"type":"string"}""");
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

        await synchronizer.UpsertDiscoveredToolsAsync(
            "runtime-mcp",
            [],
            CancellationToken.None);

        tool.IsEnabled.Should().BeFalse();
        tool.IsExecutableByAgent.Should().BeFalse();
        tool.RequiresApproval.Should().BeTrue();
    }

    [Fact]
    public void McpRuntimeToolContract_ShouldReturnRawClosedScalar_AndRejectUnknownShape()
    {
        using var schemaDocument = JsonDocument.Parse("""{"type":"string"}""");
        using var scalarDocument = JsonDocument.Parse("\"schema-bound\"");
        using var objectDocument = JsonDocument.Parse("""{"result":"legacy-wrapper"}""");

        var accepted = McpRuntimeToolContract.ValidateStructuredResult(
            schemaDocument.RootElement,
            new CallToolResult
            {
                Content = [],
                StructuredContent = scalarDocument.RootElement.Clone(),
                IsError = false
            });
        var rejected = McpRuntimeToolContract.ValidateStructuredResult(
            schemaDocument.RootElement,
            new CallToolResult
            {
                Content = [],
                StructuredContent = objectDocument.RootElement.Clone(),
                IsError = false
            });
        var missing = McpRuntimeToolContract.ValidateStructuredResult(
            schemaDocument.RootElement,
            new CallToolResult { Content = [], IsError = false });
        var remoteError = McpRuntimeToolContract.ValidateStructuredResult(
            schemaDocument.RootElement,
            new CallToolResult
            {
                Content = [],
                StructuredContent = scalarDocument.RootElement.Clone(),
                IsError = true
            });

        accepted.IsValid.Should().BeTrue();
        accepted.Value!.Value.GetString().Should().Be("schema-bound");
        rejected.IsValid.Should().BeFalse();
        missing.IsValid.Should().BeFalse();
        remoteError.IsValid.Should().BeFalse();
    }

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
                    """{"type":"object","properties":{},"additionalProperties":false}""",
                    """{"type":"object","properties":{},"additionalProperties":false}""",
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
            AiToolRiskLevel.High,
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
                    """{"type":"object","properties":{"input":{"type":"string"}},"additionalProperties":false}""",
                    """{"type":"object","properties":{},"additionalProperties":false}""",
                    AiToolRiskLevel.RequiresApproval)
            ],
            CancellationToken.None);

        tool.IsEnabled.Should().BeFalse();
        tool.IsExecutableByAgent.Should().BeFalse();
        tool.RequiresApproval.Should().BeTrue();
        tool.RequiredPermission.Should().Be("AiGateway.ToolRegistry.Manage");
        tool.AuditLevel.Should().Be(ToolAuditLevel.Verbose);
        tool.InputSchemaJson.Should().Contain("\"input\"");
        tool.RiskLevel.Should().Be(
            AiToolRiskLevel.High,
            "rediscovery must preserve stricter administrator governance");
        tool.SchemaVersion.Should().Be(2);
        tool.CatalogVersion.Should().Be(2);

        await synchronizer.UpsertDiscoveredToolsAsync(
            "runtime-mcp",
            [
                new McpDiscoveredToolRegistration(
                    "mcp__runtime_mcp__read",
                    "read",
                    "Same governed contract after rediscovery.",
                    """{"type":"object","properties":{"input":{"type":"string"}},"additionalProperties":false}""",
                    """{"type":"object","properties":{},"additionalProperties":false}""",
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
                    """{"type":"object","properties":{"input":{"type":"string"}},"additionalProperties":false}""",
                    """{"type":"object","properties":{},"additionalProperties":false}""",
                    AiToolRiskLevel.Medium)
            ],
            CancellationToken.None);

        tool.TargetName.Should().Be(
            "runtime-mcp",
            "a supplied code cannot alias a different serverName + toolName identity");
        tool.RiskLevel.Should().Be(
            AiToolRiskLevel.High,
            "risk precedence is explicit because AiToolRiskLevel enum values are not severity ordered");
        tool.SchemaVersion.Should().Be(2, "a rejected alias must not churn the governed contract");
        tool.CatalogVersion.Should().Be(2);
        tool.IsEnabled.Should().BeFalse();
        tool.IsExecutableByAgent.Should().BeFalse();
        tool.RequiresApproval.Should().BeTrue();
    }
}
