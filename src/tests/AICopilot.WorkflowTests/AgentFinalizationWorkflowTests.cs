using System.Text;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Workspaces;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.AgentWorkflowTestKit;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Result;

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
    public async Task FinalizeAsync_ShouldFailClosedUntilDurableWorkerPublishes()
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
        var sourceBytes = Encoding.UTF8.GetBytes("report");
        var authority = await FinalOutputApprovalTestData.CreatePreApprovalAuthorityAsync(
            task,
            workspace,
            new Dictionary<Guid, byte[]>
            {
                [artifact.Id.Value] = sourceBytes
            },
            checkpointAt);
        task.WaitForFinalApproval(checkpointAt);
        authority.RunAttempt.WaitForApproval(
            checkpointAt,
            "Waiting for final output approval.");
        task.ReleaseRunLease(checkpointAt, clearActiveAttempt: false);

        var approval = ApprovalRequest.CreateFinalOutput(
            task.Id,
            task.UserId,
            checkpointAt,
            authority.Proof);
        var approvedAt = checkpointAt.AddSeconds(1);
        approval.Approve(UserId, "approved", approvedAt);
        finalStep.Approve();
        var resumeQueue = new AgentTaskRunQueueItem(
            task.Id,
            AgentTaskRunTriggerType.ApprovalResume,
            UserId,
            approvedAt,
            sourceApprovalRequestId: approval.Id);

        var taskRepository = new InMemoryRepository<AgentTask>(task);
        var workspaceRepository = new InMemoryRepository<ArtifactWorkspace>(workspace);
        var approvalRepository = new InMemoryRepository<ApprovalRequest>(approval);
        var attemptStore = new InMemoryAgentTaskRunAttemptStore(authority.RunAttempt);
        var queueStore = new InMemoryAgentTaskRunQueueStore(
            authority.OriginalQueueItem,
            resumeQueue);
        var fileStore = new InMemoryArtifactWorkspaceFileStore();
        fileStore.AddFile(
            workspace.WorkspaceCode,
            artifact.RelativePath,
            sourceBytes,
            artifact.MimeType);
        var auditWriter = new CapturingAuditLogWriter();
        var finalOutputApprovalCoordinator = new FinalOutputApprovalCoordinator(
            new ThrowingFinalOutputApprovalStore(),
            new FinalOutputApprovalProofFactory(
                attemptStore,
                authority.NodeRunStore,
                fileStore),
            approvalRepository,
            workspaceRepository,
            queueStore,
            new AgentAuditRecorder(auditWriter),
            auditWriter);
        var coordinator = new ArtifactWorkspaceLifecycleCoordinator(
            workspaceRepository,
            taskRepository,
            fileStore,
            finalOutputApprovalCoordinator,
            new TestCurrentUser(UserId),
            new StubIdentityAccessService([AgentApprovalPermissions.FinalizeWorkspace]));

        var result = await coordinator.FinalizeAsync(
            workspace.WorkspaceCode,
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors!
            .OfType<ApiProblemDescriptor>()
            .Should()
            .ContainSingle(problem =>
                problem.Code == AppProblemCodes.AgentFinalizationStateConflict);
        queueStore.Items
            .Where(item => item.TriggerType == AgentTaskRunTriggerType.ApprovalResume)
            .Should()
            .ContainSingle()
            .Which.Id.Should().Be(resumeQueue.Id);

        queueStore.Items.Remove(resumeQueue);
        var missingResume = await coordinator.FinalizeAsync(
            workspace.WorkspaceCode,
            CancellationToken.None);
        missingResume.IsSuccess.Should().BeFalse();
        missingResume.Errors!
            .OfType<ApiProblemDescriptor>()
            .Should()
            .ContainSingle(problem =>
                problem.Code == AppProblemCodes.AgentFinalizationStateConflict);
        workspace.Status.Should().Be(ArtifactWorkspaceStatus.Active);
        artifact.Status.Should().Be(ArtifactStatus.Draft);
    }

    private sealed class ThrowingFinalOutputApprovalStore : IFinalOutputApprovalStore
    {
        public Task<FinalOutputApprovalCommandResult> PrepareAsync(
            FinalOutputApprovalPreparation preparation,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Legacy finalize must not create an approval.");

        public Task<FinalOutputApprovalCommandResult> DecideAsync(
            FinalOutputApprovalDecision decision,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Legacy finalize must not decide an approval.");
    }
}
