using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.McpService.McpServers;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.BackendTests;

[Trait("Suite", "Phase43SafetyQuality")]
public sealed class McpServerManagementTests
{
    [Fact]
    public async Task GetServerQuery_ShouldIncludeToolPolicySummaries_ForAllowlistedTools()
    {
        var server = new McpServerInfo(
            "advisory-mcp",
            "advisory server",
            McpTransportType.Stdio,
            "dotnet",
            "server.dll",
            ChatExposureMode.Advisory,
            ["Echo", "Inspect"],
            true);

        var serverRepository = new InMemoryReadRepository<McpServerInfo>([server]);
        var approvalPolicyRepository = new InMemoryReadRepository<ApprovalPolicy>(
        [
            new ApprovalPolicy(
                "echo-approval",
                "requires approval",
                ApprovalTargetType.McpServer,
                "advisory-mcp",
                ["Echo"],
                isEnabled: true,
                requiresOnsiteAttestation: true),
            new ApprovalPolicy(
                "other-target",
                "ignored",
                ApprovalTargetType.McpServer,
                "another-mcp",
                ["Inspect"],
                isEnabled: true,
                requiresOnsiteAttestation: true),
            new ApprovalPolicy(
                "disabled-policy",
                "ignored",
                ApprovalTargetType.McpServer,
                "advisory-mcp",
                ["Inspect"],
                isEnabled: false,
                requiresOnsiteAttestation: true)
        ]);

        var handler = new GetMcpServerQueryHandler(serverRepository, approvalPolicyRepository);

        var result = await handler.Handle(new GetMcpServerQuery(server.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.ToolPolicySummaries.Should().HaveCount(2);
        result.Value.ToolPolicySummaries.Should().Contain(item =>
            item.ToolName == "Echo"
            && item.RequiresApproval
            && item.RequiresOnsiteAttestation);
        result.Value.ToolPolicySummaries.Should().Contain(item =>
            item.ToolName == "Inspect"
            && !item.RequiresApproval
            && !item.RequiresOnsiteAttestation);
    }

    [Fact]
    public async Task GetListQuery_ShouldProjectToolPolicySummaries_ForEachServer()
    {
        var alphaServer = new McpServerInfo(
            "alpha-mcp",
            "alpha server",
            McpTransportType.Stdio,
            "dotnet",
            "alpha.dll",
            ChatExposureMode.ObserveOnly,
            ["Echo"],
            true);

        var betaServer = new McpServerInfo(
            "beta-mcp",
            "beta server",
            McpTransportType.Stdio,
            "dotnet",
            "beta.dll",
            ChatExposureMode.Advisory,
            ["Inspect"],
            true);

        var serverRepository = new InMemoryReadRepository<McpServerInfo>([betaServer, alphaServer]);
        var approvalPolicyRepository = new InMemoryReadRepository<ApprovalPolicy>(
        [
            new ApprovalPolicy(
                "beta-approval",
                "requires approval",
                ApprovalTargetType.McpServer,
                "beta-mcp",
                ["Inspect"],
                isEnabled: true,
                requiresOnsiteAttestation: false)
        ]);

        var handler = new GetListMcpServersQueryHandler(serverRepository, approvalPolicyRepository);

        var result = await handler.Handle(new GetListMcpServersQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Select(item => item.Name).Should().Equal("alpha-mcp", "beta-mcp");
        result.Value.Single(item => item.Name == "alpha-mcp").ToolPolicySummaries.Should().ContainSingle(item =>
            item.ToolName == "Echo"
            && !item.RequiresApproval
            && !item.RequiresOnsiteAttestation);
        result.Value.Single(item => item.Name == "beta-mcp").ToolPolicySummaries.Should().ContainSingle(item =>
            item.ToolName == "Inspect"
            && item.RequiresApproval
            && !item.RequiresOnsiteAttestation);
    }
}
