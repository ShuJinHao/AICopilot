namespace AICopilot.BackendTests;

[Trait("Suite", "PilotAuthorizationWorkflowM2")]
public sealed class AICopilotM2_2ReadinessScopeTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void Batch5To10ReadinessPackage_ShouldExposeAuditTimelineAndKeepM7HardStopped()
    {
        var controller = Read("src/hosts/AICopilot.HttpApi/Controllers/AiGatewayController.cs");
        var workflow = Read("src/services/AICopilot.AiGatewayService/PilotAuthorization/PilotAuthorizationWorkflow.cs");
        var guard = Read("src/core/AICopilot.Core.AiGateway/Aggregates/PilotAuthorization/PilotAuthorizationSensitiveContentGuard.cs");
        var scope = Read("docs/AICopilotM2_2AuthorizationObservabilityReadinessScope.md");
        var m3 = Read("docs/AICopilotM3ModelApiPoolProductionizationDesignFreeze.md");
        var m4 = Read("docs/AICopilotM4RagGovernanceCompletionDesignFreeze.md");
        var m5 = Read("docs/AICopilotM5EnterpriseDataSourcePermissionDesignFreeze.md");
        var m7 = Read("docs/AICopilotM7DryRunReadinessPackage.md");

        controller.Should().Contain("pilot-authorization/submissions/{id:guid}/audit-timeline");
        workflow.Should().Contain("GetPilotAuthorizationAuditTimelineQuery")
            .And.Contain("SanitizeTimelineMetadata")
            .And.Contain("PilotAuthorization.Audit")
            .And.Contain("targetId: request.SubmissionId.ToString()");
        guard.Should().Contain("Authorization header material is not allowed")
            .And.Contain("Provider API key environment material is not allowed")
            .And.Contain("Database driver URL material is not allowed")
            .And.Contain("Sensitive Chinese security wording is not allowed");

        string.Join("\n", scope, m3, m4, m5, m7).Should()
            .Contain("ExecutionPermission=not granted")
            .And.Contain("GateState=BlockedUntilExplicitM7Authorization")
            .And.Contain("No real Pilot execution")
            .And.Contain("No GA declaration");

        var forbiddenExecutionState = "Execution" + "Granted";
        string.Join("\n", controller, workflow, scope, m7).Should().NotContain(forbiddenExecutionState);
    }

    [Fact]
    public void Batch5To10Docs_ShouldRejectForbiddenExpansionClaims()
    {
        var docs = string.Join(
            "\n",
            Read("docs/AICopilotM2_2AuthorizationObservabilityReadinessScope.md"),
            Read("docs/AICopilotM3ModelApiPoolProductionizationDesignFreeze.md"),
            Read("docs/AICopilotM4RagGovernanceCompletionDesignFreeze.md"),
            Read("docs/AICopilotM5EnterpriseDataSourcePermissionDesignFreeze.md"),
            Read("docs/AICopilotM7DryRunReadinessPackage.md"));

        foreach (var forbiddenClaim in ForbiddenClaims)
        {
            docs.Should().NotContain(forbiddenClaim);
        }
    }

    private static string Read(string relativePath)
    {
        var fullPath = Path.Combine(RepoRoot, relativePath);
        File.Exists(fullPath).Should().BeTrue($"required readiness file should exist: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AICopilot.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to locate AICopilot repository root.");
    }

    private static readonly string[] ForbiddenClaims =
    [
        "ExecutionPermission=granted",
        "query_cloud_data_readonly enabled",
        "query_cloud_data_readonly 已开放",
        "Cloud 写已开放",
        "Recipe/version 已开放",
        "正式 GA 已通过",
        "已执行真实 Pilot",
        "已配置真实 endpoint",
        "已配置真实 token"
    ];
}
