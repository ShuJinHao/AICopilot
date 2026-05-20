using System.Text.Json;
using AICopilot.AiGatewayService.TrialOperations;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Aggregates.TrialOperations;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.BackendTests;

[Trait("Suite", "EnterpriseTrialOperationsP10")]
public sealed class EnterpriseTrialOperationsP10Tests
{
    [Fact]
    public void Campaign_ShouldAttachOnlySimulationOrSandboxEvidence_AndRejectProductionSource()
    {
        var now = DateTimeOffset.UtcNow;
        var task = CreateTask(now);
        var workspace = CreateFinalWorkspace(task, "SimulationBusiness", "SimulationBusiness", now);
        var campaign = CreateCampaign(now);
        var evidence = TrialTaskEvidence.FromTask(task, workspace);

        evidence.IsSuccess.Should().BeTrue();
        var run = campaign.AttachScenarioRun(
            "capacity-analysis",
            "AgentTaskEvidence",
            evidence.Value!.SourceMode,
            evidence.Value.Boundary,
            task.Id,
            evidence.Value.ArtifactIds,
            evidence.Value.QueryHashes,
            evidence.Value.ResultHashes,
            "Approved",
            TrialScenarioRunStatus.Passed,
            task.CreatedAt,
            task.CompletedAt,
            now);

        run.SourceMode.Should().Be("SimulationBusiness");
        run.QueryHashes.Should().Contain("query-hash-001");
        campaign.ScenarioRuns.Should().ContainSingle();

        var act = () => campaign.AttachScenarioRun(
            "production-read",
            "AgentTaskEvidence",
            "CloudReadonlyReal",
            "Production",
            AgentTaskId.New(),
            [],
            ["query-hash-prod"],
            [],
            "Approved",
            TrialScenarioRunStatus.Passed,
            now,
            now,
            now);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*trial_source_mode_blocked*");
    }

    [Fact]
    public void ReadinessGate_ShouldBlockWhenProductionToolEnabledOrOpenHighRiskExists()
    {
        var now = DateTimeOffset.UtcNow;
        var campaign = CreateCampaign(now);
        campaign.AttachScenarioRun(
            "sandbox-delivery",
            "CloudReadonlySandbox",
            "CloudReadonlySandbox",
            "SandboxControlledTrial",
            AgentTaskId.New(),
            [Guid.NewGuid()],
            ["sandbox-query-hash"],
            ["sandbox-result-hash"],
            "Approved",
            TrialScenarioRunStatus.Passed,
            now.AddMinutes(-5),
            now,
            now);
        campaign.UpsertRiskIssue(
            null,
            TrialRiskSeverity.High,
            "PilotBoundary",
            TrialRiskStatus.Open,
            "AI Platform",
            "tool-registry",
            "risk-resolution-hash",
            now);
        var productionTool = CreateProductionTool(now, isEnabled: true, isVisibleToPlanner: true, isExecutableByAgent: true);

        var assessment = PilotReadinessEvaluator.Evaluate(campaign, productionTool);

        assessment.Status.Should().Be("Blocked");
        assessment.Blockers.Should().Contain(item => item.Contains("risk_register", StringComparison.Ordinal));
        assessment.Blockers.Should().Contain(item => item.Contains("production_tool_closed", StringComparison.Ordinal));
    }

    [Fact]
    public void EvidencePackage_ShouldReturnOnlyReferencesHashesAndMetrics()
    {
        var now = DateTimeOffset.UtcNow;
        var campaign = CreateCampaign(now);
        campaign.AttachScenarioRun(
            "simulation-quality",
            "AgentTaskEvidence",
            "SimulationBusiness",
            "SimulationBusiness",
            AgentTaskId.New(),
            [Guid.NewGuid()],
            ["query-hash-quality"],
            ["result-hash-quality"],
            "Approved",
            TrialScenarioRunStatus.Passed,
            now.AddMinutes(-10),
            now,
            now);
        var assessment = PilotReadinessEvaluator.Evaluate(campaign, productionTool: null);

        var package = TrialEvidencePackageBuilder.Build(campaign, assessment);

        package.ReadinessStatus.Should().Be("ReadyForP11Planning");
        package.EvidenceItems.Should().ContainSingle()
            .Which.HashSamples.Should().Contain(["query-hash-quality", "result-hash-quality"]);
        package.ReportArtifactId.Should().BeNull();

        var json = JsonSerializer.Serialize(package, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json.Should().Contain("SimulationBusiness");
        json.Should().Contain("query-hash-quality");
        var lowerJson = json.ToLowerInvariant();
        lowerJson.Should().NotContain("token");
        lowerJson.Should().NotContain("connection");
        lowerJson.Should().NotContain("select ");
    }

    private static TrialCampaign CreateCampaign(DateTimeOffset now)
    {
        return new TrialCampaign(
            "P10 internal trial",
            ["SimulationBusiness", "CloudReadonlySandbox"],
            "AI Platform",
            now,
            now.AddDays(7),
            "P10 campaign summary",
            now);
    }

    private static AgentTask CreateTask(DateTimeOffset now)
    {
        var task = new AgentTask(
            new SessionId(Guid.NewGuid()),
            Guid.NewGuid(),
            "P10 trial task",
            "P10 trial task",
            AgentTaskType.ReportGeneration,
            AgentTaskRiskLevel.Medium,
            null,
            """{"plannerMode":"Dynamic"}""",
            now);
        task.AddStep(
            "Generate artifact",
            "Generate trial artifact.",
            AgentStepType.ArtifactGeneration,
            "generate_business_report",
            requiresApproval: false,
            now);
        task.ApprovePlan(now);
        return task;
    }

    private static ArtifactWorkspace CreateFinalWorkspace(
        AgentTask task,
        string sourceMode,
        string boundary,
        DateTimeOffset now)
    {
        var workspace = new ArtifactWorkspace(
            task.Id,
            $"ws_p10_{Guid.NewGuid():N}"[..38],
            "/tmp/p10",
            "/workspaces/p10",
            now);
        task.AttachWorkspace(workspace.Id, now);
        task.MarkWorkspaceReady(now);

        var artifact = workspace.AddDraftArtifact(
            ArtifactType.Markdown,
            "p10.md",
            "draft/p10.md",
            64,
            "text/markdown",
            task.Steps.Single().Id,
            now);
        artifact.ApplySourceMetadata(new ArtifactSourceMetadata(
            sourceMode,
            boundary,
            IsSimulation: sourceMode == "SimulationBusiness",
            IsSandbox: sourceMode == "CloudReadonlySandbox",
            SourceLabel: sourceMode,
            QueryHash: "query-hash-001",
            ResultHash: "result-hash-001",
            RowCount: 10,
            IsTruncated: false));
        artifact.Approve(now.AddMinutes(1));
        artifact.MarkFinal("final/p10.md", now.AddMinutes(2));
        workspace.FinalizeWorkspace(now.AddMinutes(2));
        task.WaitForFinalApproval(now.AddMinutes(1));
        task.MarkFinalized(now.AddMinutes(2));
        task.Complete("P10 final artifact approved.", now.AddMinutes(3));
        return workspace;
    }

    private static ToolRegistration CreateProductionTool(
        DateTimeOffset now,
        bool isEnabled,
        bool isVisibleToPlanner,
        bool isExecutableByAgent)
    {
        return new ToolRegistration(
            "query_cloud_data_readonly",
            "Query Cloud Data Readonly",
            "Production CloudReadonly tool remains closed until a later explicit pilot.",
            ToolProviderType.CloudReadonly,
            ToolRegistrationTargetType.AgentRuntime,
            "cloud-readonly",
            "{}",
            "{}",
            AiToolRiskLevel.Low,
            "AiGateway.ToolRegistry.Execute",
            requiresApproval: true,
            isEnabled,
            timeoutSeconds: 30,
            ToolAuditLevel.Standard,
            now,
            category: "CloudReadonly",
            businessDomains: ["Production"],
            dataBoundary: ToolDataBoundary.CloudReadonlySandboxOnly,
            isVisibleToPlanner,
            isExecutableByAgent,
            schemaVersion: 1,
            catalogVersion: 1,
            approvalPolicy: "ProductionDisabled");
    }
}
