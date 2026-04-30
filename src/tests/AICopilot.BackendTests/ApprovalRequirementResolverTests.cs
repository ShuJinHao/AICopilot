using AICopilot.AiGatewayService.Approvals;
using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;

namespace AICopilot.BackendTests;

[Trait("Suite", "Phase43SafetyQuality")]
public sealed class ApprovalRequirementResolverTests
{
    [Fact]
    public async Task GetMergedRequirementByToolNameAsync_ShouldPromoteOnsiteRequirement_WhenAnyPolicyRequiresIt()
    {
        var queryService = new InMemoryReadRepository<ApprovalPolicy>(
        [
            new ApprovalPolicy(
                "plugin-approval",
                "plugin",
                ApprovalTargetType.Plugin,
                "DiagnosticAdvisorPlugin",
                ["GenerateDiagnosticChecklist"],
                isEnabled: true,
                requiresOnsiteAttestation: false),
            new ApprovalPolicy(
                "mcp-approval",
                "mcp",
                ApprovalTargetType.McpServer,
                "advisory-mcp",
                ["GenerateDiagnosticChecklist"],
                isEnabled: true,
                requiresOnsiteAttestation: true)
        ]);

        var resolver = new ApprovalRequirementResolver(queryService);

        var requirement = await resolver.GetMergedRequirementByToolNameAsync("GenerateDiagnosticChecklist");

        requirement.RequiresApproval.Should().BeTrue();
        requirement.RequiresOnsiteAttestation.Should().BeTrue();
    }

    [Fact]
    public async Task GetRequirementsForTargetsAsync_ShouldFilterByTargetTypeAndTargetName()
    {
        var queryService = new InMemoryReadRepository<ApprovalPolicy>(
        [
            new ApprovalPolicy(
                "plugin-approval",
                "plugin",
                ApprovalTargetType.Plugin,
                "DiagnosticAdvisorPlugin",
                ["GenerateDiagnosticChecklist"],
                isEnabled: true,
                requiresOnsiteAttestation: true),
            new ApprovalPolicy(
                "other-target",
                "other",
                ApprovalTargetType.Plugin,
                "OtherPlugin",
                ["OtherTool"],
                isEnabled: true,
                requiresOnsiteAttestation: false)
        ]);

        var resolver = new ApprovalRequirementResolver(queryService);

        var result = await resolver.GetRequirementsForTargetsAsync(
            ApprovalTargetType.Plugin,
            ["DiagnosticAdvisorPlugin"]);

        result.Should().ContainKey("DiagnosticAdvisorPlugin");
        result["DiagnosticAdvisorPlugin"].Should().ContainKey("GenerateDiagnosticChecklist");
        result["DiagnosticAdvisorPlugin"]["GenerateDiagnosticChecklist"].RequiresOnsiteAttestation.Should().BeTrue();
        result.Should().NotContainKey("OtherPlugin");
    }
}
