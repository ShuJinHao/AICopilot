using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AICopilot.AgentPlugin;
using AICopilot.AiGatewayService.Tools;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.McpService.McpServers;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Repository;
using Microsoft.Extensions.DependencyInjection;

namespace AICopilot.ApplicationTests;

public sealed class McpToolGovernanceTests
{
    [Fact]
    public void ToolRegistrationOutputContractPolicy_ShouldAllowScalarAndArrayOnlyForMcp()
    {
        foreach (var schema in new[] { """{"type":"string"}""", """{"type":"array","items":{"type":"string"}}""" })
        {
            ToolRegistrationOutputContractPolicy.Validate(
                    "mcp__runtime_mcp__query",
                    ToolProviderType.Mcp,
                    schema,
                    schemaVersion: 1,
                    catalogVersion: 1)
                .IsValid.Should().BeTrue();

            ToolRegistrationOutputContractPolicy.Validate(
                    "local_tool",
                    ToolProviderType.BuiltIn,
                    schema,
                    schemaVersion: 1,
                    catalogVersion: 1)
                .IsValid.Should().BeFalse();

            ToolRegistrationOutputContractPolicy.Validate(
                    "cloud_readonly_tool",
                    ToolProviderType.CloudReadonly,
                    schema,
                    schemaVersion: 1,
                    catalogVersion: 1)
                .IsValid.Should().BeFalse();
        }
    }

    [Fact]
    public async Task GovernanceQuery_ShouldProjectAllowlistRegistryRuntimeAndOrphanStatuses()
    {
        var server = new McpServerInfo(
            "runtime-mcp",
            "runtime server",
            McpTransportType.Stdio,
            "dotnet",
            "server.dll",
            AiToolExternalSystemType.CloudReadOnly,
            AiToolCapabilityKind.ReadOnlyQuery,
            ChatExposureMode.Advisory,
            [
                new McpAllowedTool("query_allow_only", ReadOnlyDeclared: true, McpReadOnlyHint: true),
                new McpAllowedTool("query_disabled", ReadOnlyDeclared: true),
                new McpAllowedTool("query_ready", ReadOnlyDeclared: true),
                new McpAllowedTool("query_unavailable", ReadOnlyDeclared: true),
                new McpAllowedTool("query_blocked", ReadOnlyDeclared: true)
            ],
            true);
        var registry = new FakeMcpToolRegistryReadService(
            Registration("runtime-mcp", "query_disabled", runtimeAvailable: true, isEnabled: false),
            Registration("runtime-mcp", "query_ready", runtimeAvailable: true, isEnabled: true),
            Registration("runtime-mcp", "query_unavailable", runtimeAvailable: false, isEnabled: true),
            Registration("runtime-mcp", "query_blocked", runtimeAvailable: true, isEnabled: false, riskLevel: "Blocked"),
            Registration("runtime-mcp", "old", runtimeAvailable: false, isEnabled: false),
            Registration("deleted-mcp", "ghost", runtimeAvailable: false, isEnabled: true));
        var handler = new GetMcpToolGovernanceQueryHandler(
            new InMemoryReadRepository<McpServerInfo>([server]),
            registry);

        var result = await handler.Handle(new GetMcpToolGovernanceQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Summary.Total.Should().Be(7);
        result.Value.Summary.AllowlistedOnly.Should().Be(1);
        result.Value.Summary.RegisteredDisabled.Should().Be(1);
        result.Value.Summary.Ready.Should().Be(1);
        result.Value.Summary.RuntimeUnavailable.Should().Be(1);
        result.Value.Summary.Blocked.Should().Be(1);
        result.Value.Summary.OrphanedRegistration.Should().Be(2);
        result.Value.Items.Should().Contain(item =>
            item.ToolName == "query_allow_only" &&
            item.Status == "AllowlistedOnly" &&
            item.Allowlisted &&
            !item.Registered &&
            item.ReadOnlyDeclared &&
            item.McpReadOnlyHint == true);
        result.Value.Items.Should().Contain(item => item.ToolName == "query_disabled" && item.Status == "RegisteredDisabled");
        result.Value.Items.Should().Contain(item => item.ToolName == "query_ready" && item.Status == "Ready");
        result.Value.Items.Should().Contain(item => item.ToolName == "query_unavailable" && item.Status == "RuntimeUnavailable");
        result.Value.Items.Should().Contain(item => item.ToolName == "query_blocked" && item.Status == "Blocked");
        result.Value.Items.Should().Contain(item => item.ToolName == "old" && item.Status == "OrphanedRegistration");
        result.Value.Items.Should().Contain(item =>
            item.ServerName == "deleted-mcp" &&
            item.ServerId == null &&
            item.Status == "OrphanedRegistration");
    }

    [Fact]
    public async Task GovernanceQuery_ShouldFilterByStatusServerAndOrphanFlag()
    {
        var server = new McpServerInfo(
            "runtime-mcp",
            "runtime server",
            McpTransportType.Stdio,
            "dotnet",
            "server.dll",
            AiToolExternalSystemType.CloudReadOnly,
            AiToolCapabilityKind.ReadOnlyQuery,
            ChatExposureMode.Advisory,
            [new McpAllowedTool("query_ready", ReadOnlyDeclared: true)],
            true);
        var registry = new FakeMcpToolRegistryReadService(
            Registration("runtime-mcp", "query_ready", runtimeAvailable: true, isEnabled: true),
            Registration("runtime-mcp", "old", runtimeAvailable: false, isEnabled: false));
        var handler = new GetMcpToolGovernanceQueryHandler(
            new InMemoryReadRepository<McpServerInfo>([server]),
            registry);

        var ready = await handler.Handle(
            new GetMcpToolGovernanceQuery(ServerName: "runtime-mcp", Status: "ready"),
            CancellationToken.None);
        var noOrphans = await handler.Handle(
            new GetMcpToolGovernanceQuery(ServerId: server.Id.Value, IncludeOrphans: false),
            CancellationToken.None);
        var invalidStatus = await handler.Handle(
            new GetMcpToolGovernanceQuery(Status: "unknown"),
            CancellationToken.None);

        ready.IsSuccess.Should().BeTrue();
        ready.Value!.Items.Should().ContainSingle(item => item.Status == "Ready");
        noOrphans.IsSuccess.Should().BeTrue();
        noOrphans.Value!.Items.Should().ContainSingle(item => item.ToolName == "query_ready");
        invalidStatus.Status.Should().Be(AICopilot.SharedKernel.Result.ResultStatus.Invalid);
    }

    [Fact]
    public async Task McpToolRegistryReadService_ShouldProjectOnlyMcpRegistrationsAndRuntimeAvailability()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var loader = new AgentPluginLoader([], provider);
        var toolCode = AiToolIdentity.CreateRuntimeName(AiToolTargetType.McpServer, "runtime-mcp", "read_status");
        loader.RegisterAgentPlugin(new GenericBridgePlugin
        {
            Name = "runtime-mcp",
            Description = "runtime test bridge",
            ChatExposureMode = ChatExposureMode.Advisory,
            Tools =
            [
                new AiToolDefinition
                {
                    Name = toolCode,
                    ToolName = "read_status",
                    TargetType = AiToolTargetType.McpServer,
                    TargetName = "runtime-mcp"
                }
            ]
        });
        var mcpRegistration = CreateToolRegistration(
            toolCode,
            ToolProviderType.Mcp,
            ToolRegistrationTargetType.McpServer,
            "runtime-mcp",
            isEnabled: true,
            riskLevel: AiToolRiskLevel.High,
            requiredPermission: "AiGateway.Mcp.Query",
            auditLevel: ToolAuditLevel.Verbose,
            dataBoundary: ToolDataBoundary.GovernedBusinessReadOnly,
            schemaVersion: 7,
            timeoutSeconds: 37);
        var builtInRegistration = CreateToolRegistration(
            "read_uploaded_file",
            ToolProviderType.BuiltIn,
            ToolRegistrationTargetType.Plugin,
            "diagnostic-advisor",
            isEnabled: true);
        var service = new McpToolRegistryReadService(
            new InMemoryReadRepository<ToolRegistration>([mcpRegistration, builtInRegistration]),
            loader);

        var result = await service.GetMcpToolRegistrationsAsync(CancellationToken.None);

        var item = result.Should().ContainSingle().Subject;
        item.ToolCode.Should().Be(toolCode);
        item.ServerName.Should().Be("runtime-mcp");
        item.ToolName.Should().Be("read_status");
        item.RuntimeAvailable.Should().BeTrue();
        item.IsEnabled.Should().BeTrue();
        item.RiskLevel.Should().Be(nameof(AiToolRiskLevel.High));
        item.RequiresApproval.Should().BeTrue();
        item.RequiredPermission.Should().Be("AiGateway.Mcp.Query");
        item.AuditLevel.Should().Be(nameof(ToolAuditLevel.Verbose));
        item.DataBoundary.Should().Be(nameof(ToolDataBoundary.GovernedBusinessReadOnly));
        item.SchemaVersion.Should().Be(7);
        item.TimeoutSeconds.Should().Be(37);
    }

    private static McpToolRegistryReadModel Registration(
        string serverName,
        string toolName,
        bool runtimeAvailable,
        bool isEnabled,
        string riskLevel = "Low")
    {
        return new McpToolRegistryReadModel(
            AiToolIdentity.CreateRuntimeName(AiToolTargetType.McpServer, serverName, toolName),
            serverName,
            toolName,
            runtimeAvailable,
            isEnabled,
            riskLevel,
            RequiresApproval: riskLevel == "RequiresApproval",
            RequiredPermission: null,
            DateTimeOffset.UtcNow);
    }

    private static ToolRegistration CreateToolRegistration(
        string toolCode,
        ToolProviderType providerType,
        ToolRegistrationTargetType targetType,
        string targetName,
        bool isEnabled,
        AiToolRiskLevel riskLevel = AiToolRiskLevel.Low,
        string? requiredPermission = null,
        ToolAuditLevel auditLevel = ToolAuditLevel.Standard,
        ToolDataBoundary dataBoundary = ToolDataBoundary.NoData,
        int schemaVersion = 1,
        int timeoutSeconds = 120)
    {
        return new ToolRegistration(
            toolCode,
            toolCode,
            "test tool",
            providerType,
            targetType,
            targetName,
            """{"type":"object"}""",
            """{"type":"object"}""",
            riskLevel,
            requiredPermission,
            requiresApproval: false,
            isEnabled,
            timeoutSeconds,
            auditLevel,
            DateTimeOffset.UtcNow,
            dataBoundary: dataBoundary,
            schemaVersion: schemaVersion);
    }

    private sealed class FakeMcpToolRegistryReadService(params McpToolRegistryReadModel[] registrations)
        : IMcpToolRegistryReadService
    {
        public Task<IReadOnlyCollection<McpToolRegistryReadModel>> GetMcpToolRegistrationsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyCollection<McpToolRegistryReadModel>>(registrations);
        }
    }
}
