namespace AICopilot.BackendTests;

[Trait("Suite", "PilotAuthorizationWorkflowM2")]
public sealed class AICopilotM2M9GovernanceScopeTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void M2Workflow_ShouldExposeRequiredApiRoutesStatesPermissionsAndAuditKeys()
    {
        var controller = ReadAiGatewayControllerSources();
        var aggregate = Read("src/core/AICopilot.Core.AiGateway/Aggregates/PilotAuthorization/PilotAuthorizationSubmission.cs");
        var permissions = Read("src/services/AICopilot.IdentityService/Authorization/PermissionCatalog.cs");
        var auditCodec = Read("src/infrastructure/AICopilot.EntityFrameworkCore/AuditLogs/AuditMetadataCodec.cs");

        foreach (var route in RequiredPilotAuthorizationRoutes)
        {
            controller.Should().Contain(route);
        }

        foreach (var status in RequiredPilotAuthorizationStatuses)
        {
            aggregate.Should().Contain(status);
        }

        foreach (var permission in RequiredPilotAuthorizationPermissions)
        {
            permissions.Should().Contain(permission);
        }

        foreach (var key in RequiredPilotAuthorizationAuditKeys)
        {
            auditCodec.Should().Contain(key);
        }

        var forbiddenExecutionState = "Execution" + "Granted";
        string.Join("\n", controller, aggregate).Should().NotContain(forbiddenExecutionState);
    }

    [Fact]
    public void M3ToM6GovernanceBaseline_ShouldRemainPresentWithoutSecretOrExecutionExpansion()
    {
        Read("src/services/AICopilot.AiGatewayService/Queries/Runtime/GetModelPools.cs")
            .Should().Contain("GetModelPoolsQuery");
        Read("src/services/AICopilot.AiGatewayService/Queries/Runtime/GetProviderReliability.cs")
            .Should().Contain("GetProviderReliabilityQuery");
        Read("src/services/AICopilot.Services.Contracts/Contracts/AiRuntimeContracts.cs")
            .Should().Contain("ModelProviderReliabilityDto");

        Read("src/services/AICopilot.RagService/Governance/KnowledgeGovernanceManagement.cs")
            .Should().Contain("KnowledgeSupplement")
            .And.Contain("SetKnowledgeSupplementEnabled");
        Read("src/services/AICopilot.RagService/Documents/DocumentManagement.cs")
            .Should().Contain("UpdateDocumentGovernance");

        Read("src/core/AICopilot.Core.DataAnalysis/Aggregates/BusinessDatabase/DataSourcePermissionGrant.cs")
            .Should().Contain("DataSourcePermissionGrant");
        Read("src/services/AICopilot.DataAnalysisService/BusinessDatabases/BusinessTextToSql.cs")
            .Should().Contain("BusinessTextToSql")
            .And.Contain("DataSource.TextToSql");

        Read("docs/AICopilotM2-M9连续推进执行记录.md")
            .Should().Contain("不配置真实 provider endpoint/token")
            .And.Contain("不输出 secret")
            .And.Contain("不输出 raw payload、raw business rows、完整 SQL")
            .And.Contain("不开放 Cloud 写、Recipe/version、自由 SQL");
    }

    [Fact]
    public void M7AndM9Packages_ShouldKeepHardStopAndFinalReviewBoundaries()
    {
        var m7 = Read("docs/AICopilotM7真实Pilot前硬停授权包.md");
        var m9 = Read("docs/AICopilotM9GPT总审包.md");
        var record = Read("docs/AICopilotM2-M9连续推进执行记录.md");

        m7.Should().Contain("ExecutionPermission=not granted")
            .And.Contain("GateState=BlockedUntilExplicitM7Authorization")
            .And.Contain("当前只允许 planning 和 readiness，不允许 execution");
        m9.Should().Contain("GPT/5.5 Pro")
            .And.Contain("不把 M9 审核包视为 GA 发布批准");
        record.Should().Contain("未配置真实 endpoint/token")
            .And.Contain("未开启 Cloud 写或自由 SQL")
            .And.Contain("未声明 GA");
    }

    private static string Read(string relativePath)
    {
        var fullPath = Path.Combine(RepoRoot, relativePath);
        File.Exists(fullPath).Should().BeTrue($"required governance file should exist: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    private static string ReadAiGatewayControllerSources()
    {
        var controllerPath = Path.Combine(RepoRoot, "src", "hosts", "AICopilot.HttpApi", "Controllers");
        return string.Join(
            "\n",
            Directory.GetFiles(controllerPath, "AiGateway*.cs")
                .OrderBy(file => file, StringComparer.Ordinal)
                .Select(File.ReadAllText));
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

    private static readonly string[] RequiredPilotAuthorizationRoutes =
    [
        "pilot-authorization/submissions",
        "pilot-authorization/submissions/{id:guid}",
        "pilot-authorization/submissions/{id:guid}/submit",
        "pilot-authorization/submissions/{id:guid}/approve-credential-window-planning",
        "pilot-authorization/submissions/{id:guid}/approve-limited-pilot-execution-planning",
        "pilot-authorization/submissions/{id:guid}/reject",
        "pilot-authorization/submissions/{id:guid}/revoke"
    ];

    private static readonly string[] RequiredPilotAuthorizationStatuses =
    [
        "Draft",
        "Submitted",
        "MachineRejected",
        "ReviewPending",
        "ApprovedForCredentialWindowPlanning",
        "ApprovedForLimitedPilotExecutionPlanning",
        "Rejected",
        "Expired",
        "Revoked"
    ];

    private static readonly string[] RequiredPilotAuthorizationPermissions =
    [
        "PilotAuthorization.Submit",
        "PilotAuthorization.View",
        "PilotAuthorization.Review",
        "PilotAuthorization.ApprovePlanning",
        "PilotAuthorization.Reject",
        "PilotAuthorization.Audit"
    ];

    private static readonly string[] RequiredPilotAuthorizationAuditKeys =
    [
        "pilotAuthorizationStatus",
        "endpointCount",
        "maxRows",
        "timeRangeDays",
        "ownerCount",
        "machineValidationStatus"
    ];
}
