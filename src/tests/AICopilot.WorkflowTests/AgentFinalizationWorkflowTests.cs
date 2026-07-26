using System.Text;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Workspaces;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.AgentWorkflowTestKit;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.WorkflowTests;

public sealed class AgentFinalizationWorkflowTests : ToolRegistryGovernanceTestBase
{
    [Fact]
    public void FinalizationStageWrites_ShouldPreserveSubpathsContentAndRejectCaseInsensitiveCollisions()
    {
        var chartContent = Encoding.UTF8.GetBytes("""{"source":"charts"}""");
        var draftContent = Encoding.UTF8.GetBytes("""{"source":"draft"}""");

        var chart = AgentFinalizationNodeExecutor.CreateFinalStageWriteRequest(
            "charts/report.json",
            chartContent,
            "application/json");
        var draft = AgentFinalizationNodeExecutor.CreateFinalStageWriteRequest(
            "draft/report.json",
            draftContent,
            "application/json");

        chart.RelativePath.Should().Be("charts/report.json");
        draft.RelativePath.Should().Be("draft/report.json");
        chart.Content.Should().Equal(chartContent);
        draft.Content.Should().Equal(draftContent);
        AgentFinalizationNodeExecutor.HasCaseInsensitivePathCollision(
                [chart.RelativePath, draft.RelativePath])
            .Should()
            .BeFalse();
        AgentFinalizationNodeExecutor.HasCaseInsensitivePathCollision(
                ["draft/report.json", "DRAFT/REPORT.JSON"])
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task FinalizeAsync_ShouldEnqueueApprovedPausedCheckpointWithoutSynchronousPublication()
    {
        var now = DateTimeOffset.UtcNow;
        var planJson = AgentPlanV2TestData.Create(
            [
                new AgentPlanV2TestStep(
                    "Generate Markdown",
                    "Generate a governed Markdown artifact.",
                    AgentStepType.ArtifactGeneration,
                    "generate_markdown_report")
            ],
            executable: true,
            taskType: AgentTaskType.ReportGeneration,
            knowledgeBaseIds: null);
        var task = new AgentTask(
            SessionId.New(),
            UserId,
            "Finalize checkpoint",
            "Finalize checkpoint",
            AgentTaskType.ReportGeneration,
            AgentTaskRiskLevel.Low,
            null,
            planJson,
            now);
        var steps = AgentPlanV2TestData.AddTrackedPlanSteps(task, planJson, now);
        var generationStep = steps.Single(step =>
            string.Equals(step.ToolCode, "generate_markdown_report", StringComparison.Ordinal));
        var finalStep = steps.Single(step =>
            string.Equals(step.ToolCode, "finalize_artifacts", StringComparison.Ordinal));
        var workspace = new ArtifactWorkspace(
            task.Id,
            $"ws_{Guid.NewGuid():N}",
            "/tmp/aicopilot-finalization",
            "/workspaces/finalization",
            now);
        task.AttachWorkspace(workspace.Id, now);
        task.ConfirmExecutablePlan(
            task.PlanJson,
            steps.Where(step => step.RequiresApproval).Select(step => step.StepIndex).ToArray(),
            now);
        task.ApprovePlan(now);

        var runStartedAt = now.AddSeconds(1);
        task.Start(runStartedAt);
        var attempt = new AgentTaskRunAttempt(
            task.Id,
            1,
            AgentTaskRunTriggerType.Manual,
            "workflow-finalization",
            runStartedAt,
            TimeSpan.FromMinutes(5));
        task.BeginRunAttempt(
            attempt.Id,
            attempt.AttemptNo,
            attempt.LeaseId!.Value,
            attempt.LeaseOwner!,
            attempt.LeaseExpiresAt!.Value,
            runStartedAt);
        attempt.BindTaskFencingToken(task.RunFencingToken);

        var stepStartedAt = now.AddSeconds(2);
        generationStep.Start(stepStartedAt);
        var artifact = workspace.AddDraftArtifact(
            ArtifactType.Markdown,
            "report.md",
            "draft/report.md",
            6,
            "text/markdown",
            generationStep.Id,
            stepStartedAt.AddSeconds(1));
        generationStep.Complete(
            CanonicalJson.Serialize(new
            {
                status = "completed",
                resultType = "artifact",
                artifactType = "markdown",
                artifactId = artifact.Id.Value
            }),
            stepStartedAt.AddSeconds(2));

        var checkpointAt = now.AddSeconds(5);
        task.MarkWorkspaceReady(checkpointAt);
        task.WaitForFinalApproval(checkpointAt);
        attempt.WaitForApproval(checkpointAt, "Waiting for final output approval.");
        task.ReleaseRunLease(checkpointAt, clearActiveAttempt: false);

        var approval = new ApprovalRequest(
            task.Id,
            AgentApprovalType.FinalOutput,
            workspace.WorkspaceCode,
            task.UserId,
            checkpointAt);
        var approvedAt = checkpointAt.AddSeconds(1);
        approval.Approve(UserId, "approved", approvedAt);
        finalStep.Approve();

        var taskRepository = new InMemoryRepository<AgentTask>(task);
        var workspaceRepository = new InMemoryRepository<ArtifactWorkspace>(workspace);
        var approvalRepository = new InMemoryRepository<ApprovalRequest>(approval);
        var attemptStore = new InMemoryAgentTaskRunAttemptStore(attempt);
        var queueStore = new InMemoryAgentTaskRunQueueStore();
        var fileStore = new InMemoryArtifactWorkspaceFileStore();
        fileStore.AddFile(
            workspace.WorkspaceCode,
            artifact.RelativePath,
            Encoding.UTF8.GetBytes("report"),
            artifact.MimeType);
        var coordinator = new ArtifactWorkspaceLifecycleCoordinator(
            workspaceRepository,
            taskRepository,
            approvalRepository,
            attemptStore,
            fileStore,
            new AgentTaskRunQueue(
                queueStore,
                AgentPlanV2TestData.CreateDownstreamRuntimeHarnessFreshReadGate()),
            new AgentAuditRecorder(new CapturingAuditLogWriter()),
            new TestCurrentUser(UserId),
            new StubIdentityAccessService([AgentApprovalPermissions.FinalizeWorkspace]));

        var result = await coordinator.FinalizeAsync(
            workspace.WorkspaceCode,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(ArtifactWorkspaceStatus.Active.ToString());
        result.Value.Artifacts.Should().ContainSingle(item =>
            item.Status == ArtifactStatus.Draft.ToString() &&
            item.RelativePath == "draft/report.md");
        var queued = queueStore.Items.Should().ContainSingle().Which;
        queued.TriggerType.Should().Be(AgentTaskRunTriggerType.ApprovalResume);
        queued.Status.Should().Be(AgentTaskRunQueueStatus.Queued);
    }
}
