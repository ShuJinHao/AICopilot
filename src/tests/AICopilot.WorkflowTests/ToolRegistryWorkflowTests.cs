using System.Linq.Expressions;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using AICopilot.AgentPlugin;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Approvals;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Queries.Sessions;
using AICopilot.AiGatewayService.Runtime;
using AICopilot.AiGatewayService.Skills;
using AICopilot.AiGatewayService.Tools;
using AICopilot.AiGatewayService.Uploads;
using AICopilot.AiGatewayService.Workspaces;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Aggregates.Skills;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Aggregates.Uploads;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Domain;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using AICopilot.SharedKernel.Specification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using AICopilot.AgentWorkflowTestKit;

namespace AICopilot.WorkflowTests;

public sealed class ToolRegistryWorkflowTests : ToolRegistryGovernanceTestBase
{
    [Fact]
    public async Task AgentTaskRuntime_P0ProductionGate_ShouldRejectDraftPlanBeforeAttemptOrToolExecution()
    {
        var (task, workspace) = CreateApprovedTask("generate_chart_data");
        var attempts = new InMemoryAgentTaskRunAttemptStore();
        var executions = new InMemoryToolExecutionAuditStore();
        var runtime = CreateRuntime(
            new InMemoryRepository<AgentTask>(task),
            new InMemoryRepository<ArtifactWorkspace>(workspace),
            new InMemoryRepository<ApprovalRequest>(),
            executions,
            CreateGuard(CreateTool("generate_chart_data")),
            toolExecutors: [new TestAgentToolExecutor(_ => true)],
            runAttemptRepository: attempts);

        var result = await runtime.RunAsync(task, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors!.OfType<ApiProblemDescriptor>().Single().Code
            .Should().Be(AppProblemCodes.AgentPlanInvalid);
        attempts.Items.Should().BeEmpty();
        executions.Items.Should().BeEmpty();
        task.Status.Should().Be(AgentTaskStatus.PlanApproved);
    }

    [Fact]
    public async Task AgentTaskRuntime_ShouldRejectRun_WhenLeaseIsActive()
    {
        var (task, workspace) = CreateApprovedTask("generate_chart_data");
        var now = DateTimeOffset.UtcNow;
        var attempt = new AgentTaskRunAttempt(task.Id, 1, AgentTaskRunTriggerType.Manual, "test-runner", now, TimeSpan.FromMinutes(5));
        task.BeginRunAttempt(
            attempt.Id,
            attempt.AttemptNo,
            attempt.LeaseId!.Value,
            attempt.LeaseOwner!,
            attempt.LeaseExpiresAt!.Value,
            now);
        var executionRepository = new InMemoryToolExecutionAuditStore();
        var runtime = CreateRuntime(
            new InMemoryRepository<AgentTask>(task),
            new InMemoryRepository<ArtifactWorkspace>(workspace),
            new InMemoryRepository<ApprovalRequest>(),
            executionRepository,
            CreateGuard(CreateTool("generate_chart_data")),
            toolExecutors: [new TestAgentToolExecutor(_ => true)],
            runAttemptRepository: new InMemoryAgentTaskRunAttemptStore(attempt),
            freshReadGate: AgentPlanV2TestData.CreateDownstreamRuntimeHarnessFreshReadGate());

        var result = await runtime.RunAsync(task, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors!.OfType<ApiProblemDescriptor>().Single().Code
            .Should().Be(AppProblemCodes.AgentTaskRunInProgress);
        executionRepository.Items.Should().BeEmpty();
    }
    [Fact]
    public async Task AgentTaskRuntime_ShouldCreateRunAttempt_AndLinkExecutionRecord()
    {
        var (task, workspace) = CreateApprovedTask("generate_chart_data");
        var runAttemptRepository = new InMemoryAgentTaskRunAttemptStore();
        var executionRepository = new InMemoryToolExecutionAuditStore();
        var runtime = CreateRuntime(
            new InMemoryRepository<AgentTask>(task),
            new InMemoryRepository<ArtifactWorkspace>(workspace),
            new InMemoryRepository<ApprovalRequest>(),
            executionRepository,
            CreateGuard(
                CreateTool("generate_chart_data", ToolProviderType.Artifact),
                CreateTool(
                    "finalize_artifacts",
                    ToolProviderType.Artifact,
                    requiresApproval: true,
                    riskLevel: AiToolRiskLevel.RequiresApproval)),
            toolExecutors:
            [
                new ContextResultAgentToolExecutor(
                    "generate_chart_data",
                    context =>
                    {
                        var artifact = context.Workspace.AddDraftArtifact(
                            ArtifactType.Chart,
                            "chart.json",
                            "draft/chart.json",
                            1,
                            "application/json",
                            context.Step.Id,
                            DateTimeOffset.UtcNow);
                        return AgentToolExecutionResult.From(new
                        {
                            status = "completed",
                            resultType = "artifact",
                            artifactType = "chart",
                            artifactId = artifact.Id.Value
                        });
                    })
            ],
            runAttemptRepository: runAttemptRepository,
            freshReadGate: AgentPlanV2TestData.CreateDownstreamRuntimeHarnessFreshReadGate());

        var result = await runtime.RunAsync(task, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        task.RunAttemptCount.Should().Be(1);
        task.IsRunInProgress(DateTimeOffset.UtcNow).Should().BeFalse();
        var attempt = runAttemptRepository.Items.Should().ContainSingle().Which;
        attempt.AttemptNo.Should().Be(1);
        attempt.Status.Should().Be(AgentTaskRunAttemptStatus.WaitingApproval);
        task.ActiveRunAttemptId.Should().Be(attempt.Id);
        executionRepository.Items.Should().ContainSingle()
            .Which.RunAttemptId.Should().Be(attempt.Id);
    }
    [Fact]
    public void AgentPlanV2Fixture_ShouldRejectRetiredPersistedSkillField()
    {
        Action create = () => AgentPlanV2TestData.CreateSingleStep(
            "generate_pdf",
            executable: false,
            skillCode: "restricted_skill");

        create.Should().Throw<InvalidOperationException>()
            .WithMessage("*must not serialize or consume retired Skill fields*");
    }
    [Fact]
    public async Task RetryAgentTaskCommand_ShouldResetFailedStep_AndEnqueueRetry()
    {
        var (task, workspace) = CreateApprovedTask("generate_chart_data");
        var now = DateTimeOffset.UtcNow;
        task.Start(now);
        var step = task.Steps.Single(item => item.ToolCode == "generate_chart_data");
        step.Start(now);
        step.Fail("generator failed", now);
        task.Fail("generator failed", now);

        var taskRepository = new InMemoryRepository<AgentTask>(task);
        var workspaceRepository = new InMemoryRepository<ArtifactWorkspace>(workspace);
        var approvalRepository = new InMemoryRepository<ApprovalRequest>(
            new ApprovalRequest(task.Id, AgentApprovalType.ToolCall, step.Id.Value.ToString(), task.UserId, now));
        var queueRepository = new InMemoryAgentTaskRunQueueStore();
        var lifecycleCoordinator = CreateLifecycleCoordinator(
            taskRepository,
            workspaceRepository,
            approvalRepository,
            queueRepository,
            freshReadGate: AgentPlanV2TestData.CreateDownstreamRuntimeHarnessFreshReadGate());
        var executablePlanJson = task.PlanJson;
        var handler = new RetryAgentTaskCommandHandler(
            taskRepository,
            CreateAgentTaskDtoQueryService(workspaceRepository, approvalRepository, queueRepository),
            lifecycleCoordinator,
            new TestCurrentUser(UserId));

        var result = await handler.Handle(new RetryAgentTaskCommand(task.Id.Value), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        task.Status.Should().Be(AgentTaskStatus.PlanApproved);
        task.PlanJson.Should().Be(executablePlanJson);
        step.Status.Should().Be(AgentStepStatus.Pending);
        task.RunAttemptCount.Should().Be(0);
        approvalRepository.Items.Single().Status.Should().Be(AgentApprovalStatus.Cancelled);
        var item = queueRepository.Items.Should().ContainSingle().Which;
        item.TriggerType.Should().Be(AgentTaskRunTriggerType.Retry);
        item.Status.Should().Be(AgentTaskRunQueueStatus.Queued);
        result.Value!.QueuedRunId.Should().Be(item.Id.Value);
        result.Value.IsRunQueued.Should().BeTrue();
    }
    [Fact]
    public async Task RetryAgentTaskCommand_ShouldRejectTerminalTasks()
    {
        var (task, workspace) = CreateApprovedTask("generate_chart_data");
        task.Start(DateTimeOffset.UtcNow);
        task.MarkWorkspaceReady(DateTimeOffset.UtcNow);
        task.WaitForFinalApproval(DateTimeOffset.UtcNow);
        task.Complete("done", DateTimeOffset.UtcNow);
        var taskRepository = new InMemoryRepository<AgentTask>(task);
        var workspaceRepository = new InMemoryRepository<ArtifactWorkspace>(workspace);
        var approvalRepository = new InMemoryRepository<ApprovalRequest>();
        var queueRepository = new InMemoryAgentTaskRunQueueStore();
        var lifecycleCoordinator = CreateLifecycleCoordinator(
            taskRepository,
            workspaceRepository,
            approvalRepository,
            queueRepository,
            freshReadGate: AgentPlanV2TestData.CreateDownstreamRuntimeHarnessFreshReadGate());
        var handler = new RetryAgentTaskCommandHandler(
            taskRepository,
            CreateAgentTaskDtoQueryService(workspaceRepository, approvalRepository, queueRepository),
            lifecycleCoordinator,
            new TestCurrentUser(UserId));

        var result = await handler.Handle(new RetryAgentTaskCommand(task.Id.Value), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors!.OfType<ApiProblemDescriptor>().Single().Code
            .Should().Be(AppProblemCodes.AgentTaskRetryNotAllowed);
    }
    [Fact]
    public async Task CancelAgentTaskCommand_ShouldCancelActiveAttemptAndPendingApprovals()
    {
        var (task, workspace) = CreateApprovedTask("generate_chart_data");
        var now = DateTimeOffset.UtcNow;
        task.Start(now);
        task.WaitForToolApproval(now);
        var attempt = new AgentTaskRunAttempt(task.Id, 1, AgentTaskRunTriggerType.Manual, "test-runner", now, TimeSpan.FromMinutes(5));
        attempt.WaitForApproval(now, "Waiting for approval.");
        task.BeginRunAttempt(
            attempt.Id,
            attempt.AttemptNo,
            Guid.NewGuid(),
            "test-runner",
            now.AddMinutes(5),
            now);
        task.ReleaseRunLease(now, clearActiveAttempt: false);
        var approval = new ApprovalRequest(
            task.Id,
            AgentApprovalType.ToolCall,
            task.Steps.Single(item => item.ToolCode == "generate_chart_data").Id.Value.ToString(),
            task.UserId,
            now);
        var approvalRepository = new InMemoryRepository<ApprovalRequest>(approval);
        var runAttemptRepository = new InMemoryAgentTaskRunAttemptStore(attempt);
        var queueItem = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Manual, task.UserId, now);
        var queueRepository = new InMemoryAgentTaskRunQueueStore(queueItem);
        var taskRepository = new InMemoryRepository<AgentTask>(task);
        var workspaceRepository = new InMemoryRepository<ArtifactWorkspace>(workspace);
        var lifecycleCoordinator = CreateLifecycleCoordinator(
            taskRepository,
            workspaceRepository,
            approvalRepository,
            queueRepository,
            runAttemptRepository);
        var handler = new CancelAgentTaskCommandHandler(
            taskRepository,
            CreateAgentTaskDtoQueryService(workspaceRepository, approvalRepository, queueRepository),
            lifecycleCoordinator,
            new TestCurrentUser(UserId));

        var result = await handler.Handle(new CancelAgentTaskCommand(task.Id.Value), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        task.Status.Should().Be(AgentTaskStatus.Cancelled);
        attempt.Status.Should().Be(AgentTaskRunAttemptStatus.Cancelled);
        approval.Status.Should().Be(AgentApprovalStatus.Cancelled);
        queueItem.Status.Should().Be(AgentTaskRunQueueStatus.Cancelled);
    }
    [Fact]
    public async Task AgentTaskRunAttemptsQuery_ShouldPageAndIsolateByOwner()
    {
        var (task, _) = CreateApprovedTask("generate_chart_data");
        var foreignTask = CreateApprovedTask("generate_chart_data").Task;
        var first = new AgentTaskRunAttempt(task.Id, 1, AgentTaskRunTriggerType.Manual, "runner", DateTimeOffset.UtcNow.AddMinutes(-5), TimeSpan.FromMinutes(5));
        first.MarkFailed(AppProblemCodes.ArtifactGenerationFailed, "failed", DateTimeOffset.UtcNow.AddMinutes(-4));
        var second = new AgentTaskRunAttempt(task.Id, 2, AgentTaskRunTriggerType.Retry, "runner", DateTimeOffset.UtcNow.AddMinutes(-1), TimeSpan.FromMinutes(5));
        var foreign = new AgentTaskRunAttempt(foreignTask.Id, 1, AgentTaskRunTriggerType.Manual, "runner", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
        var handler = new GetAgentTaskRunAttemptsQueryHandler(
            CreateAuditQueryCoordinator(
                new InMemoryRepository<AgentTask>(task, foreignTask),
                runAttemptRepository: new InMemoryAgentTaskRunAttemptStore(first, second, foreign)));

        var result = await handler.Handle(new GetAgentTaskRunAttemptsQuery(task.Id.Value, 1, 10), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().HaveCount(2);
        result.Value.Items.Should().NotContain(item => item.TaskId == foreignTask.Id.Value);
        result.Value.Items.First().AttemptNo.Should().Be(2);

        var unauthorized = await new GetAgentTaskRunAttemptsQueryHandler(
                CreateAuditQueryCoordinator(
                    new InMemoryRepository<AgentTask>(task),
                    runAttemptRepository: new InMemoryAgentTaskRunAttemptStore(first, second),
                    currentUser: new TestCurrentUser(Guid.Parse("22222222-2222-4222-8222-222222222222"))))
            .Handle(new GetAgentTaskRunAttemptsQuery(task.Id.Value, 1, 10), CancellationToken.None);
        unauthorized.Status.Should().Be(ResultStatus.NotFound);
    }
    [Fact]
    public async Task AgentTaskRunQueueQuery_ShouldPageAndIsolateByOwner()
    {
        var (task, _) = CreateApprovedTask("generate_chart_data");
        var foreignTask = CreateApprovedTask("generate_chart_data").Task;
        var now = DateTimeOffset.UtcNow;
        var older = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Manual, task.UserId, now.AddMinutes(-5));
        older.MarkFailed("failed", "safe failure", now.AddMinutes(-4));
        var latest = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Retry, task.UserId, now.AddMinutes(-1));
        var foreign = new AgentTaskRunQueueItem(foreignTask.Id, AgentTaskRunTriggerType.Manual, foreignTask.UserId, now);
        var handler = new GetAgentTaskRunQueueQueryHandler(
            CreateAuditQueryCoordinator(
                new InMemoryRepository<AgentTask>(task, foreignTask),
                queueRepository: new InMemoryAgentTaskRunQueueStore(older, latest, foreign)));

        var result = await handler.Handle(new GetAgentTaskRunQueueQuery(task.Id.Value, 1, 10), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().HaveCount(2);
        result.Value.Items.Should().NotContain(item => item.TaskId == foreignTask.Id.Value);
        result.Value.Items.First().Id.Should().Be(latest.Id.Value);

        var unauthorized = await new GetAgentTaskRunQueueQueryHandler(
                CreateAuditQueryCoordinator(
                    new InMemoryRepository<AgentTask>(task),
                    queueRepository: new InMemoryAgentTaskRunQueueStore(older, latest),
                    currentUser: new TestCurrentUser(Guid.Parse("22222222-2222-4222-8222-222222222222"))))
            .Handle(new GetAgentTaskRunQueueQuery(task.Id.Value, 1, 10), CancellationToken.None);
        unauthorized.Status.Should().Be(ResultStatus.NotFound);
    }
    [Fact]
    public async Task AgentRunQueueEnqueue_ShouldPreventDuplicateActiveItems()
    {
        var (task, workspace) = CreateApprovedTask("generate_chart_data");
        var queueRepository = new InMemoryAgentTaskRunQueueStore();
        var taskRepository = new InMemoryRepository<AgentTask>(task);
        var workspaceRepository = new InMemoryRepository<ArtifactWorkspace>(workspace);
        var approvalRepository = new InMemoryRepository<ApprovalRequest>();
        var lifecycleCoordinator = CreateLifecycleCoordinator(
            taskRepository,
            workspaceRepository,
            approvalRepository,
            queueRepository,
            freshReadGate: AgentPlanV2TestData.CreateDownstreamRuntimeHarnessFreshReadGate());
        var handler = new RunAgentTaskCommandHandler(
            taskRepository,
            CreateAgentTaskDtoQueryService(workspaceRepository, approvalRepository, queueRepository),
            lifecycleCoordinator,
            new TestCurrentUser(UserId));

        var first = await handler.Handle(new RunAgentTaskCommand(task.Id.Value), CancellationToken.None);
        var duplicate = await handler.Handle(new RunAgentTaskCommand(task.Id.Value), CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        first.Value!.IsRunQueued.Should().BeTrue();
        queueRepository.Items.Should().ContainSingle();
        duplicate.IsSuccess.Should().BeFalse();
        duplicate.Errors!.OfType<ApiProblemDescriptor>().Single().Code
            .Should().Be(AppProblemCodes.AgentTaskRunInProgress);
    }
    [Fact]
    public async Task AgentRunQueueWorkerPositive_ShouldLeaseRunAndCompleteQueueItem()
    {
        var (task, _) = CreateApprovedTask("generate_chart_data");
        var taskRepository = new InMemoryRepository<AgentTask>(task);
        var queueRepository = new InMemoryAgentTaskRunQueueStore(
            new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Manual, task.UserId, DateTimeOffset.UtcNow));
        var attemptRepository = new InMemoryAgentTaskRunAttemptStore();
        using var provider = CreateQueueWorkerProvider(
            taskRepository,
            queueRepository,
            attemptRepository,
            new RecordingAgentTaskRuntime(attemptRepository));
        var worker = new AgentTaskRunQueueWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentTaskRunQueueWorker>.Instance);

        var processed = await worker.ProcessOnceAsync(CancellationToken.None);

        processed.Should().BeTrue();
        var queueItem = queueRepository.Items.Single();
        queueItem.Status.Should().Be(AgentTaskRunQueueStatus.Succeeded);
        queueItem.RunAttemptId.Should().NotBeNull();
        attemptRepository.Items.Should().ContainSingle()
            .Which.TriggerType.Should().Be(AgentTaskRunTriggerType.Manual);
    }
    [Fact]
    public async Task AgentApprovalResumeQueue_ShouldEnqueueAfterToolApproval()
    {
        var (task, workspace) = CreateApprovedTask("generate_pdf", requiresApproval: true);
        var now = DateTimeOffset.UtcNow;
        task.Start(now);
        var step = task.Steps.Single(item => item.ToolCode == "generate_pdf");
        step.Status.Should().Be(AgentStepStatus.WaitingApproval);
        var attempt = new AgentTaskRunAttempt(
            task.Id,
            1,
            AgentTaskRunTriggerType.Manual,
            "workflow-test",
            now,
            TimeSpan.FromMinutes(1));
        task.BeginRunAttempt(
            attempt.Id,
            attempt.AttemptNo,
            attempt.LeaseId!.Value,
            attempt.LeaseOwner!,
            attempt.LeaseExpiresAt!.Value,
            now);
        task.WaitForToolApproval(now);
        attempt.WaitForApproval(now, "Waiting for tool approval.");
        task.ReleaseRunLease(now);
        var approval = new ApprovalRequest(task.Id, AgentApprovalType.ToolCall, step.Id.Value.ToString(), task.UserId, now);
        var queueRepository = new InMemoryAgentTaskRunQueueStore();
        var attemptRepository = new InMemoryAgentTaskRunAttemptStore(attempt);
        var handler = new ApproveAgentApprovalCommandHandler(
            new AgentApprovalDecisionCoordinator(
                new InMemoryRepository<ApprovalRequest>(approval),
                new InMemoryRepository<AgentTask>(task),
                new InMemoryRepository<ArtifactWorkspace>(workspace),
                new AgentAuditRecorder(new CapturingAuditLogWriter()),
                new AgentTaskRunQueue(
                    queueRepository,
                    AgentPlanV2TestData.CreateDownstreamRuntimeHarnessFreshReadGate()),
                new TestCurrentUser(UserId),
                new StubIdentityAccessService([AgentApprovalPermissions.ApproveAgentToolCall]),
                new AgentPlanDraftConfirmationService(
                    CreatePlanToolGuard(CreateAgentRuntimeGuardWithCloudEnabled()),
                    AgentPlanV2TestData.CreateMatchingFreshReadGate(),
                    AgentPlanV2TestData.CreateMatchingRoutingSnapshotReader(),
                    new FixedCloudReadonlyAgentPlanService())));

        var result = await handler.Handle(new ApproveAgentApprovalCommand(approval.Id.Value, "approved"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        step.Status.Should().Be(AgentStepStatus.Approved);
        task.Status.Should().Be(AgentTaskStatus.Running);
        queueRepository.Items.Should().ContainSingle()
            .Which.TriggerType.Should().Be(AgentTaskRunTriggerType.ApprovalResume);
    }
    [Fact]
    public async Task AgentRunQueueLeaseExpired_ShouldFailStartedLeaseConservatively()
    {
        var (task, _) = CreateApprovedTask("generate_chart_data");
        var now = DateTimeOffset.UtcNow.AddMinutes(-10);
        var attempt = new AgentTaskRunAttempt(task.Id, 1, AgentTaskRunTriggerType.Manual, "expired-worker", now, TimeSpan.FromSeconds(1));
        task.BeginRunAttempt(
            attempt.Id,
            attempt.AttemptNo,
            attempt.LeaseId!.Value,
            attempt.LeaseOwner!,
            attempt.LeaseExpiresAt!.Value,
            now);
        var queueItem = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Manual, task.UserId, now);
        queueItem.AcquireLease(Guid.NewGuid(), "expired-worker", now, TimeSpan.FromSeconds(1));
        queueItem.MarkStarted(attempt.Id, now);
        var taskRepository = new InMemoryRepository<AgentTask>(task);
        var queueRepository = new InMemoryAgentTaskRunQueueStore(queueItem);
        var attemptRepository = new InMemoryAgentTaskRunAttemptStore(attempt);
        using var provider = CreateQueueWorkerProvider(
            taskRepository,
            queueRepository,
            attemptRepository,
            new ThrowingRuntime());
        var worker = new AgentTaskRunQueueWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentTaskRunQueueWorker>.Instance);

        var processed = await worker.ProcessOnceAsync(CancellationToken.None);

        processed.Should().BeFalse();
        queueItem.Status.Should().Be(AgentTaskRunQueueStatus.Failed);
        queueItem.FailureCode.Should().Be(AppProblemCodes.AgentTaskRunQueueLeaseExpired);
        task.Status.Should().Be(AgentTaskStatus.Failed);
        attempt.Status.Should().Be(AgentTaskRunAttemptStatus.Failed);
        attempt.FailureCode.Should().Be(AppProblemCodes.AgentTaskRunLeaseExpired);
    }
    [Fact]
    public async Task AgentWorkerHeartbeat_ShouldTrackIdleAndActiveState()
    {
        var (task, _) = CreateApprovedTask("generate_chart_data");
        var queueItem = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Manual, task.UserId, DateTimeOffset.UtcNow);
        var heartbeatStore = new InMemoryAgentWorkerHeartbeatStore();
        var heartbeatService = new AgentWorkerHeartbeatService(
            heartbeatStore,
            new FixedWorkspaceFingerprintProvider("hash-api"));

        await heartbeatService.MarkAsync("worker-1", "data-worker", "1.0.0", null, CancellationToken.None);
        await heartbeatService.MarkAsync("worker-1", "data-worker", "1.0.0", queueItem, CancellationToken.None);
        await heartbeatService.MarkAsync("worker-1", "data-worker", "1.0.0", null, CancellationToken.None);

        heartbeatStore.Items.Should().ContainSingle();
        var heartbeat = heartbeatStore.Items.Single();
        heartbeat.WorkerId.Should().Be("worker-1");
        heartbeat.WorkspaceRootHash.Should().Be("hash-api");
        heartbeat.ActiveQueueItemId.Should().BeNull();
        heartbeat.ActiveTaskId.Should().BeNull();
    }
    [Fact]
    public async Task AgentRunQueueSummary_ShouldCountStatusesAndOldestQueued()
    {
        var (task, _) = CreateApprovedTask("generate_chart_data");
        var now = DateTimeOffset.UtcNow;
        var oldest = now.AddMinutes(-10);
        var queued = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Manual, task.UserId, now, oldest);
        var leased = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Retry, task.UserId, now);
        leased.AcquireLease(Guid.NewGuid(), "worker", now, TimeSpan.FromMinutes(5));
        var staleLeased = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Retry, task.UserId, now.AddMinutes(-10));
        staleLeased.AcquireLease(Guid.NewGuid(), "worker", now.AddMinutes(-10), TimeSpan.FromSeconds(1));
        var failed = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Manual, task.UserId, now);
        failed.MarkFailed("failed", "safe", now);
        var deadLetter = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Manual, task.UserId, now);
        deadLetter.MarkDeadLetter("dead", "safe", now);
        var succeeded = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Manual, task.UserId, now);
        succeeded.MarkSucceeded(now);
        var heartbeat = new AgentWorkerHeartbeat("worker-1", "data-worker", now, "hash", "1.0.0");
        var handler = new GetAgentRunQueueSummaryQueryHandler(
            new InMemoryAgentTaskRunQueueStore(queued, leased, staleLeased, failed, deadLetter, succeeded),
            new InMemoryAgentWorkerHeartbeatStore(heartbeat));

        var result = await handler.Handle(new GetAgentRunQueueSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.QueuedCount.Should().Be(1);
        result.Value.LeasedCount.Should().Be(2);
        result.Value.FailedCount.Should().Be(1);
        result.Value.DeadLetterCount.Should().Be(1);
        result.Value.SucceededCount.Should().Be(1);
        result.Value.StaleLeasedCount.Should().Be(1);
        result.Value.OldestQueuedAt.Should().Be(oldest);
        result.Value.ActiveWorkerCount.Should().Be(1);
    }
    [Fact]
    public async Task AgentRunQueueGlobalQuery_ShouldFilterByStatusTriggerTaskAndRequester()
    {
        var (task, _) = CreateApprovedTask("generate_chart_data");
        var (otherTask, _) = CreateApprovedTask("generate_pdf");
        var now = DateTimeOffset.UtcNow;
        var first = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Manual, task.UserId, now);
        first.MarkFailed("failed", "safe", now);
        var second = new AgentTaskRunQueueItem(otherTask.Id, AgentTaskRunTriggerType.Retry, otherTask.UserId, now);
        var handler = new GetAgentRunQueueQueryHandler(new InMemoryAgentTaskRunQueueStore(first, second));

        var result = await handler.Handle(
            new GetAgentRunQueueQuery(
                PageIndex: 1,
                PageSize: 10,
                Status: "Failed",
                TriggerType: "Manual",
                TaskId: task.Id.Value,
                RequestedBy: task.UserId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().ContainSingle()
            .Which.Id.Should().Be(first.Id.Value);
    }
    [Fact]
    public async Task AgentRunQueueDeadLetter_ShouldAllowOnlySafeStatesAndWriteAudit()
    {
        var (task, _) = CreateApprovedTask("generate_chart_data");
        var now = DateTimeOffset.UtcNow;
        var failed = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Manual, task.UserId, now);
        failed.MarkFailed("worker_failed", "safe failure", now);
        var activeLeased = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Retry, task.UserId, now);
        activeLeased.AcquireLease(Guid.NewGuid(), "worker", now, TimeSpan.FromMinutes(5));
        var succeeded = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Manual, task.UserId, now);
        succeeded.MarkSucceeded(now);
        var repository = new InMemoryAgentTaskRunQueueStore(failed, activeLeased, succeeded);
        var audit = new CapturingAuditLogWriter();
        var handler = new DeadLetterAgentRunQueueItemCommandHandler(repository, audit);

        var moved = await handler.Handle(
            new DeadLetterAgentRunQueueItemCommand(failed.Id.Value, "move to dead letter"),
            CancellationToken.None);
        var activeDenied = await handler.Handle(
            new DeadLetterAgentRunQueueItemCommand(activeLeased.Id.Value, "bad"),
            CancellationToken.None);
        var succeededDenied = await handler.Handle(
            new DeadLetterAgentRunQueueItemCommand(succeeded.Id.Value, "bad"),
            CancellationToken.None);

        moved.IsSuccess.Should().BeTrue();
        failed.Status.Should().Be(AgentTaskRunQueueStatus.DeadLetter);
        audit.Requests.Should().ContainSingle()
            .Which.ActionCode.Should().Be("Agent.RunQueueDeadLetter");
        activeDenied.IsSuccess.Should().BeFalse();
        activeDenied.Errors!.OfType<ApiProblemDescriptor>().Single().Code
            .Should().Be(AppProblemCodes.AgentRunQueueDeadLetterNotAllowed);
        succeededDenied.IsSuccess.Should().BeFalse();
        succeededDenied.Errors!.OfType<ApiProblemDescriptor>().Single().Code
            .Should().Be(AppProblemCodes.AgentRunQueueDeadLetterNotAllowed);
    }
    [Fact]
    public async Task AgentTaskRuntime_ShouldRejectDisabledTool_AndWriteRejectedExecutionRecord()
    {
        var (task, workspace) = CreateApprovedTask("generate_chart_data");
        var taskRepository = new InMemoryRepository<AgentTask>(task);
        var workspaceRepository = new InMemoryRepository<ArtifactWorkspace>(workspace);
        var executionRepository = new InMemoryToolExecutionAuditStore();
        var runtime = CreateRuntime(
            taskRepository,
            workspaceRepository,
            new InMemoryRepository<ApprovalRequest>(),
            executionRepository,
            CreateGuard(CreateTool("generate_chart_data", isEnabled: false)),
            freshReadGate: AgentPlanV2TestData.CreateDownstreamRuntimeHarnessFreshReadGate());

        var result = await runtime.RunAsync(task, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        task.Status.Should().Be(AgentTaskStatus.Failed);
        task.Steps.Single(step => step.ToolCode == "generate_chart_data")
            .Status.Should().Be(AgentStepStatus.Failed);
        var record = executionRepository.Items.Should().ContainSingle().Which;
        record.ToolCode.Should().Be("generate_chart_data");
        record.Status.Should().Be(ToolExecutionStatus.Rejected);
        record.ErrorCode.Should().Be(AppProblemCodes.ToolDisabled);
    }
    [Fact]
    public async Task AgentTaskRuntime_ShouldUseRegistryApprovalRequirement_BeforeExecutingStep()
    {
        var (task, workspace) = CreateApprovedTask("generate_chart_data");
        var approvalRepository = new InMemoryRepository<ApprovalRequest>();
        var executionRepository = new InMemoryToolExecutionAuditStore();
        var runtime = CreateRuntime(
            new InMemoryRepository<AgentTask>(task),
            new InMemoryRepository<ArtifactWorkspace>(workspace),
            approvalRepository,
            executionRepository,
            CreateGuard(CreateTool("generate_chart_data", requiresApproval: true)),
            freshReadGate: AgentPlanV2TestData.CreateDownstreamRuntimeHarnessFreshReadGate());

        var result = await runtime.RunAsync(task, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        task.Status.Should().Be(AgentTaskStatus.WaitingToolApproval);
        task.Steps.Single(step => step.ToolCode == "generate_chart_data")
            .Status.Should().Be(AgentStepStatus.WaitingApproval);
        executionRepository.Items.Should().BeEmpty();
        var approval = approvalRepository.Items.Should().ContainSingle().Which;
        approval.ApprovalType.Should().Be(AgentApprovalType.ToolCall);
        approval.TargetId.Should().Be(task.Steps
            .Single(step => step.ToolCode == "generate_chart_data").Id.Value.ToString());
    }
    [Fact]
    public async Task AgentTaskRuntime_ShouldRedactFailedExecutionRecord()
    {
        var (task, workspace) = CreateApprovedTask("generate_chart_data");
        var executionRepository = new InMemoryToolExecutionAuditStore();
        var runtime = CreateRuntime(
            new InMemoryRepository<AgentTask>(task),
            new InMemoryRepository<ArtifactWorkspace>(workspace),
            new InMemoryRepository<ApprovalRequest>(),
            executionRepository,
            CreateGuard(CreateTool("generate_chart_data")),
            new ThrowingWorkspaceService(throwOnWrite: true),
            freshReadGate: AgentPlanV2TestData.CreateDownstreamRuntimeHarnessFreshReadGate());

        var result = await runtime.RunAsync(task, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var record = executionRepository.Items.Should().ContainSingle().Which;
        record.Status.Should().Be(ToolExecutionStatus.Failed);
        record.ErrorCode.Should().Be(AppProblemCodes.ArtifactGenerationFailed);
        record.ErrorMessage.Should().Contain("Tool execution failed.");
        record.ErrorMessage.Should().Contain("ErrorType=InvalidOperationException");
        record.ErrorMessage.Should().NotContain("apiKey");
        record.ErrorMessage.Should().NotContain("Password");
        record.ErrorMessage.Should().NotContain("sk-test");
        record.ErrorMessage.Should().NotContain("C:\\");
        record.ErrorMessage.Should().NotContain("super-secret");
    }
    [Fact]
    public async Task ToolExecutionRecordQuery_ShouldPageFilterRedactAndIsolateByTaskOwner()
    {
        var (task, _) = CreateApprovedTask("generate_chart_data");
        var failedRecord = new ToolExecutionRecord(
            task.Id,
            task.Steps.Single(step => step.ToolCode == "generate_chart_data").Id,
            "generate_pdf",
            @"apiKey: sk-test C:\secrets\input.txt",
            DateTimeOffset.UtcNow.AddMinutes(-1));
        failedRecord.MarkFailed(
            AppProblemCodes.ArtifactGenerationFailed,
            @"connection string=Host=db;Password=super-secret; C:\secrets\output.txt",
            """{"providerType":"Artifact","targetType":"AgentRuntime","timeoutSeconds":120,"auditLevel":"Standard"}""",
            DateTimeOffset.UtcNow);
        var succeededRecord = new ToolExecutionRecord(
            task.Id,
            task.Steps.Single(step => step.ToolCode == "generate_chart_data").Id,
            "generate_chart_data",
            "{}",
            DateTimeOffset.UtcNow.AddMinutes(-2));
        succeededRecord.MarkSucceeded(
            """{"sql":"select * from production_records","table":"production_records"}""",
            null,
            """{"providerType":"BuiltIn"}""",
            DateTimeOffset.UtcNow.AddMinutes(-2).AddSeconds(1));
        var foreignTask = CreateApprovedTask("generate_pdf").Task;
        var foreignRecord = new ToolExecutionRecord(
            foreignTask.Id,
            foreignTask.Steps.Single(step => step.ToolCode == "generate_pdf").Id,
            "generate_pdf",
            "{}",
            DateTimeOffset.UtcNow);

        var handler = new GetAgentTaskToolExecutionsQueryHandler(new AgentTaskToolExecutionQueryCoordinator(
            new InMemoryRepository<AgentTask>(task, foreignTask),
            new InMemoryToolExecutionAuditStore(failedRecord, succeededRecord, foreignRecord),
            new TestCurrentUser(UserId)));

        var result = await handler.Handle(
            new GetAgentTaskToolExecutionsQuery(task.Id.Value, 1, 10, "Failed", "generate_pdf"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().ContainSingle();
        result.Value.TotalCount.Should().Be(1);
        var dto = result.Value.Items.Single();
        dto.ToolCode.Should().Be("generate_pdf");
        dto.Status.Should().Be("Failed");
        dto.ErrorCode.Should().Be(AppProblemCodes.ArtifactGenerationFailed);
        dto.InputSummary.Should().Contain("apiKey=******");
        dto.InputSummary.Should().Contain("[redacted-path]");
        dto.ErrorMessage.Should().Contain("connection string=******");
        dto.ErrorMessage.Should().NotContain("sk-test");
        dto.ErrorMessage.Should().NotContain("super-secret");
        dto.ErrorMessage.Should().NotContain("C:\\");

        var allRecords = await handler.Handle(
            new GetAgentTaskToolExecutionsQuery(task.Id.Value, 1, 10),
            CancellationToken.None);
        allRecords.Value!.Items.Should().HaveCount(2);
        allRecords.Value.Items.Should().NotContain(item => item.TaskId == foreignTask.Id.Value);
        allRecords.Value.Items.Single(item => item.ToolCode == "generate_chart_data")
            .OutputSummary!.ToLowerInvariant().Should().NotContain("select");

        var unauthorizedHandler = new GetAgentTaskToolExecutionsQueryHandler(new AgentTaskToolExecutionQueryCoordinator(
            new InMemoryRepository<AgentTask>(task),
            new InMemoryToolExecutionAuditStore(failedRecord),
            new TestCurrentUser(Guid.Parse("22222222-2222-4222-8222-222222222222"))));
        var unauthorized = await unauthorizedHandler.Handle(
            new GetAgentTaskToolExecutionsQuery(task.Id.Value, 1, 10),
            CancellationToken.None);
        unauthorized.Status.Should().Be(ResultStatus.NotFound);
    }
    [Fact]
    public async Task AgentAuditSummary_ShouldMergeToolRecordsAndFailureSummary()
    {
        var (task, workspace) = CreateApprovedTask("generate_pdf");
        var now = DateTimeOffset.UtcNow;
        task.Start(now);
        var step = task.Steps.Single(item => item.ToolCode == "generate_pdf");
        step.Start(now);
        step.Fail(@"apiKey: sk-test C:\secrets\report.txt Host=db;Password=super-secret;", now);
        task.Fail("report generation failed", now);

        var record = new ToolExecutionRecord(
            task.Id,
            step.Id,
            "generate_pdf",
            "{}",
            now.AddSeconds(-1));
        record.MarkFailed(
            AppProblemCodes.ArtifactGenerationFailed,
            @"apiKey: sk-test C:\secrets\report.txt Host=db;Password=super-secret;",
            """{"providerType":"Artifact","targetType":"AgentRuntime","timeoutSeconds":120,"auditLevel":"Standard","workspaceCode":"ws_test"}""",
            now);

        var auditLog = new AuditLogSummaryDto(
            Guid.NewGuid(),
            AuditActionGroups.AiGateway,
            "Agent.Plan",
            "AgentTask",
            task.Id.Value.ToString(),
            task.TaskCode,
            "test-user",
            "User",
            AuditResults.Succeeded,
            "Plan created.",
            [],
            new Dictionary<string, string> { ["taskId"] = task.Id.Value.ToString() },
            now.UtcDateTime.AddMinutes(-5));
        var handler = new GetAgentTaskAuditSummaryQueryHandler(
            CreateAuditQueryCoordinator(
                new InMemoryRepository<AgentTask>(task),
                workspaceRepository: new InMemoryRepository<ArtifactWorkspace>(workspace),
                executionRepository: new InMemoryToolExecutionAuditStore(record),
                auditLogQueryService: new FixedAuditLogQueryService(auditLog)));

        var result = await handler.Handle(new GetAgentTaskAuditSummaryQuery(task.Id.Value), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(item => item.ActionCode == "Agent.Plan");
        result.Value.Should().Contain(item => item.ActionCode == "Agent.ToolExecutionRecord" &&
                                             item.Metadata["providerType"] == "Artifact" &&
                                             item.Metadata["targetType"] == "AgentRuntime" &&
                                             item.Metadata["auditLevel"] == "Standard");
        var failure = result.Value.Should().ContainSingle(item => item.ActionCode == "Agent.FailureSummary").Subject;
        failure.Metadata["errorCode"].Should().Be(AppProblemCodes.ArtifactGenerationFailed);
        failure.Summary.Should().NotContain("sk-test");
        failure.Summary.Should().NotContain("super-secret");
        failure.Summary.Should().NotContain("C:\\");
    }
    [Fact]
    public async Task AgentTaskRuntime_ShouldExecuteCloudReadonlyTool_AndUseRowsInMarkdownReport()
    {
        var (task, workspace) = CreateCloudApprovedTask();
        var approvalRepository = new InMemoryRepository<ApprovalRequest>();
        var approval = new ApprovalRequest(
            task.Id,
            AgentApprovalType.ToolCall,
            task.Steps.First().Id.Value.ToString(),
            task.UserId,
            DateTimeOffset.UtcNow);
        approval.Approve(UserId, "approved", DateTimeOffset.UtcNow);
        approvalRepository.Add(approval);

        var executionRepository = new InMemoryToolExecutionAuditStore();
        var workspaceService = new CapturingWorkspaceService(workspace);
        var semanticPlan = CreateDeviceSemanticPlan();
        var cloudClient = new RecordingCloudAiReadClient(new CloudAiReadResult<object>(
            "/api/v1/ai/read/devices",
            "Cloud AiRead devices",
            DateTimeOffset.UtcNow,
            20,
            false,
            [],
            [
                new Dictionary<string, object?>
                {
                    ["deviceCode"] = "DEV-001",
                    ["deviceName"] = "Cutter A",
                    ["status"] = "Running"
                }
            ]));
        var runtime = CreateRuntime(
            new InMemoryRepository<AgentTask>(task),
            new InMemoryRepository<ArtifactWorkspace>(workspace),
            approvalRepository,
            executionRepository,
            CreateGuard(
                CreateTool(
                    "query_cloud_data_readonly",
                    ToolProviderType.CloudReadonly,
                    isEnabled: true,
                    requiresApproval: true,
                    riskLevel: AiToolRiskLevel.RequiresApproval),
                CreateTool("generate_markdown_report", ToolProviderType.Artifact),
                CreateTool(
                    "finalize_artifacts",
                    ToolProviderType.Artifact,
                    requiresApproval: true,
                    riskLevel: AiToolRiskLevel.RequiresApproval)),
            workspaceService,
            CreateRealCloudReadonlyExecutor(new FixedSemanticQueryPlanner(semanticPlan), cloudClient),
            freshReadGate: AgentPlanV2TestData.CreateDownstreamRuntimeHarnessFreshReadGate());

        var result = await runtime.RunAsync(task, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        task.Status.Should().Be(AgentTaskStatus.WaitingFinalApproval);
        task.Steps.Where(step => step.ToolCode != "finalize_artifacts")
            .Should().OnlyContain(step => step.Status == AgentStepStatus.Completed);
        task.Steps.Single(step => step.ToolCode == "finalize_artifacts")
            .Status.Should().Be(AgentStepStatus.WaitingApproval);
        cloudClient.RequestedPlans.Should().ContainSingle()
            .Which.Intent.Should().Be("Analysis.Device.List");
        workspaceService.TextArtifacts.Should().ContainKey("draft/report.md");
        workspaceService.TextArtifacts["draft/report.md"].Should().Contain("DEV-001");
        executionRepository.Items.Should().HaveCount(2);
        executionRepository.Items.First().Status.Should().Be(ToolExecutionStatus.Succeeded);
        executionRepository.Items.First().OutputSummary.Should().Contain("\"resultType\": \"cloud-query-summary\"");
        executionRepository.Items.First().OutputSummary.Should().NotContain("Cloud AiRead");
        executionRepository.Items.First().OutputSummary!.ToLowerInvariant().Should().NotContain("select ");
        approvalRepository.Items.Should().ContainSingle(item =>
            item.ApprovalType == AgentApprovalType.FinalOutput &&
            item.Status == AgentApprovalStatus.Pending);
    }
    [Fact]
    public async Task AgentTaskRuntime_ShouldStopCloudReadonlyFlow_WhenCloudReadFails()
    {
        var (task, workspace) = CreateCloudApprovedTask();
        var approvalRepository = new InMemoryRepository<ApprovalRequest>();
        var approval = new ApprovalRequest(
            task.Id,
            AgentApprovalType.ToolCall,
            task.Steps.First().Id.Value.ToString(),
            task.UserId,
            DateTimeOffset.UtcNow);
        approval.Approve(UserId, "approved", DateTimeOffset.UtcNow);
        approvalRepository.Add(approval);

        var executionRepository = new InMemoryToolExecutionAuditStore();
        var workspaceService = new CapturingWorkspaceService(workspace);
        var runtime = CreateRuntime(
            new InMemoryRepository<AgentTask>(task),
            new InMemoryRepository<ArtifactWorkspace>(workspace),
            approvalRepository,
            executionRepository,
            CreateGuard(
                CreateTool(
                    "query_cloud_data_readonly",
                    ToolProviderType.CloudReadonly,
                    isEnabled: true,
                    requiresApproval: true,
                    riskLevel: AiToolRiskLevel.RequiresApproval),
                CreateTool("generate_markdown_report", ToolProviderType.Artifact)),
            workspaceService,
            CreateRealCloudReadonlyExecutor(
                new FixedSemanticQueryPlanner(CreateDeviceSemanticPlan()),
                new FailingCloudAiReadClient(new CloudAiReadException(
                    CloudAiReadProblemCodes.MissingRequiredParameter,
                    @"apiKey: sk-test C:\cloud\secret.txt Host=db;Password=super-secret; missing device/time"))),
            freshReadGate: AgentPlanV2TestData.CreateDownstreamRuntimeHarnessFreshReadGate());

        var result = await runtime.RunAsync(task, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        task.Status.Should().Be(AgentTaskStatus.Failed);
        task.Steps.First().Status.Should().Be(AgentStepStatus.Failed);
        task.Steps.Skip(1).Should().OnlyContain(step => step.Status != AgentStepStatus.Completed);
        workspaceService.TextArtifacts.Should().NotContainKey("draft/report.md");
        var record = executionRepository.Items.Should().ContainSingle().Which;
        record.Status.Should().Be(ToolExecutionStatus.Failed);
        record.ErrorCode.Should().Be(CloudAiReadProblemCodes.MissingRequiredParameter);
        record.ErrorMessage.Should().Contain("Cloud read-only tool failed.");
        record.ErrorMessage.Should().Contain("ErrorCode=cloud_ai_read_missing_required_parameter");
        record.ErrorMessage.Should().Contain("ErrorType=CloudAiReadException");
        record.ErrorMessage.Should().NotContain("apiKey");
        record.ErrorMessage.Should().NotContain("Password");
        record.ErrorMessage.Should().NotContain("sk-test");
        record.ErrorMessage.Should().NotContain("C:\\");
        record.ErrorMessage.Should().NotContain("super-secret");
    }
    [Fact]
    public async Task AgentTaskRuntime_ShouldEvaluateRagAdminAccessFromTaskOwner()
    {
        var knowledgeBaseId = Guid.NewGuid();
        var (task, workspace) = CreateRagApprovedTask(knowledgeBaseId);
        var executionRepository = new InMemoryToolExecutionAuditStore();
        var accessChecker = new RecordingKnowledgeBaseAccessChecker();
        var runtime = CreateRuntime(
            new InMemoryRepository<AgentTask>(task),
            new InMemoryRepository<ArtifactWorkspace>(workspace),
            new InMemoryRepository<ApprovalRequest>(),
            executionRepository,
            CreateGuard(CreateTool("rag_search", ToolProviderType.BuiltIn)),
            knowledgeBaseAccessCheckers: [accessChecker],
            identityAccessService: new StubIdentityAccessService([], roleName: "Admin"));

        var result = await runtime.RunAsync(task, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        task.Status.Should().Be(AgentTaskStatus.WaitingFinalApproval);
        accessChecker.ObservedUserId.Should().Be(UserId);
        accessChecker.ObservedIsAdmin.Should().BeTrue();
        executionRepository.Items.Should().ContainSingle().Which.Status.Should().Be(ToolExecutionStatus.Succeeded);
    }
    [Fact]
    public async Task GetSessionTimeline_ShouldExposeRagStepSummaryWithoutRawOutput()
    {
        var knowledgeBaseId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var session = new Session(UserId, ConversationTemplateId.New());
        var task = new AgentTask(
            session.Id,
            UserId,
            "RAG timeline summary",
            "RAG timeline summary",
            AgentTaskType.DataAnalysis,
            AgentTaskRiskLevel.Low,
            null,
            CreateRagPlanJson(knowledgeBaseId),
            now);
        var step = task.AddStep(
            "Search RAG",
            "Search knowledge base.",
            AgentStepType.DataQuery,
            "rag_search",
            requiresApproval: false,
            now);
        var sourceText = $"First paragraph\n{new string('x', 260)}";
        step.Start(now.AddSeconds(1));
        step.Complete(JsonSerializer.Serialize(new
        {
            status = "completed",
            lowConfidence = true,
            sources = new[]
            {
                new
                {
                    knowledgeBaseId,
                    documentId = 42,
                    documentName = "Device manual.pdf",
                    chunkIndex = 3,
                    score = 0.72,
                    isLowConfidence = false,
                    lowConfidenceReason = (string?)null,
                    text = sourceText
                }
            }
        }, JsonSerializerOptions.Web), now.AddSeconds(2));

        var timelineEvent = MessageEvent.FromProjection(
            session.Id,
            1,
            MessageEventType.AgentTaskStepCompleted,
            now.AddSeconds(2),
            agentTaskId: task.Id,
            agentStepId: step.Id);
        var handler = new GetSessionTimelineQueryHandler(new SessionTimelineQueryCoordinator(
            new InMemoryRepository<Session>(session),
            new InMemoryMessageTimelineProjectionStore(timelineEvent),
            new InMemoryRepository<AgentTask>(task),
            new InMemoryRepository<ApprovalRequest>(),
            new InMemoryRepository<ArtifactWorkspace>(),
            new TestCurrentUser(UserId)));

        var result = await handler.Handle(new GetSessionTimelineQuery(session.Id.Value), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var item = result.Value!.Items.Should().ContainSingle().Which;
        item.AgentStepOutputKind.Should().Be("RagSearch");
        item.AgentStepResultCount.Should().Be(1);
        item.AgentStepLowConfidence.Should().BeTrue();
        var source = item.AgentStepSources.Should().ContainSingle().Which;
        source.KnowledgeBaseId.Should().Be(knowledgeBaseId);
        source.DocumentId.Should().Be(42);
        source.DocumentName.Should().Be("Device manual.pdf");
        source.ChunkIndex.Should().Be(3);
        source.Score.Should().Be(0.72);
        source.TextPreview.Should().NotContain("\n");
        source.TextPreview.Should().EndWith("...");
        source.TextPreview!.Length.Should().BeLessThanOrEqualTo(220);
    }
    [Fact]
    public async Task GetSessionTimeline_ShouldResolveApprovalCurrentStateFromAggregate()
    {
        var now = DateTimeOffset.UtcNow;
        var session = new Session(UserId, ConversationTemplateId.New());
        var task = new AgentTask(
            session.Id,
            UserId,
            "Approve current state",
            "Approve current state",
            AgentTaskType.DataAnalysis,
            AgentTaskRiskLevel.Low,
            null,
            """{"steps":[]}""",
            now);
        var approval = new ApprovalRequest(
            task.Id,
            AgentApprovalType.Plan,
            task.Id.Value.ToString(),
            UserId,
            now.AddSeconds(1));
        approval.Approve(UserId, "approved", now.AddSeconds(2));
        var timelineEvent = MessageEvent.FromProjection(
            session.Id,
            1,
            MessageEventType.ApprovalDecided,
            now.AddSeconds(2),
            agentTaskId: task.Id,
            approvalRequestId: approval.Id,
            payloadJson: """{"approvalStatus":"Pending"}""");
        var handler = new GetSessionTimelineQueryHandler(new SessionTimelineQueryCoordinator(
            new InMemoryRepository<Session>(session),
            new InMemoryMessageTimelineProjectionStore(timelineEvent),
            new InMemoryRepository<AgentTask>(task),
            new InMemoryRepository<ApprovalRequest>(approval),
            new InMemoryRepository<ArtifactWorkspace>(),
            new TestCurrentUser(UserId)));

        var result = await handler.Handle(new GetSessionTimelineQuery(session.Id.Value), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var item = result.Value!.Items.Should().ContainSingle().Which;
        item.EventType.Should().Be(nameof(MessageEventType.ApprovalDecided));
        item.ApprovalRequestId.Should().Be(approval.Id.Value);
        item.ApprovalStatus.Should().Be(nameof(AgentApprovalStatus.Approved));
        item.ApprovalDecidedAt.Should().Be(now.AddSeconds(2));
        item.ApprovalTargetName.Should().Be(task.Title);
    }
    [Fact]
    public async Task AgentTaskRuntime_ShouldRejectBuiltInContractDurableSplitBeforeAnySuccessSideEffect()
    {
        const string toolCode = "builtin_split_output_fixture";
        const string outputSchema =
            """{"type":"object","properties":{"status":{"type":"string"}},"required":["status"],"additionalProperties":false}""";
        var tool = CreateTool(toolCode, outputSchemaJson: outputSchema);
        var contractOutput = ToolOutputSchemaValidator.ValidateAndCanonicalize(
            new { status = "ok" },
            outputSchema);
        contractOutput.IsValid.Should().BeTrue(contractOutput.Error);
        var secretDurableOutput = ToolOutputSchemaValidator.CanonicalizeForPersistence(new
        {
            status = "ok",
            token = "sk-test",
            path = @"C:\server\secret.txt",
            sql = "SELECT * FROM payroll",
            authorization = "Bearer abc123",
            unixPath = "/var/private/provider.pem"
        });
        secretDurableOutput.IsValid.Should().BeTrue(secretDurableOutput.Error);
        var executionResult = new AgentToolExecutionResult(
            AgentToolOutputSnapshot.FromValidated(contractOutput),
            AgentToolOutputSnapshot.FromValidated(secretDurableOutput));
        var (task, workspace) = CreateDownstreamRuntimeApprovedTask(toolCode);
        var taskRepository = new InMemoryRepository<AgentTask>(task);
        var workspaceRepository = new InMemoryRepository<ArtifactWorkspace>(workspace);
        var approvalRepository = new InMemoryRepository<ApprovalRequest>();
        var executionRepository = new InMemoryToolExecutionAuditStore();
        var runtime = CreateRuntime(
            taskRepository,
            workspaceRepository,
            approvalRepository,
            executionRepository,
            CreateGuard(tool),
            toolExecutors: [new FixedResultAgentToolExecutor(toolCode, executionResult)],
            freshReadGate: AgentPlanV2TestData.CreateDownstreamRuntimeHarnessFreshReadGate());

        var result = await runtime.RunAsync(task, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        task.Status.Should().Be(AgentTaskStatus.Failed);
        var step = task.Steps.Should().ContainSingle().Which;
        step.Status.Should().Be(AgentStepStatus.Failed);
        step.OutputJson.Should().BeNull();
        var record = executionRepository.Items.Should().ContainSingle().Which;
        record.Status.Should().Be(ToolExecutionStatus.Failed);
        record.ErrorCode.Should().Be(AppProblemCodes.ToolOutputSchemaInvalid);
        record.OutputSummary.Should().BeNull();
        var dto = await CreateAgentTaskDtoQueryService(
                workspaceRepository,
                approvalRepository,
                new InMemoryAgentTaskRunQueueStore())
            .MapAsync(task, CancellationToken.None);
        var durableSurfaces = string.Join('\n',
            task.FinalSummary,
            step.ErrorMessage,
            step.OutputJson,
            record.OutputSummary,
            record.ErrorMessage,
            record.AuditMetadata,
            JsonSerializer.Serialize(dto));
        durableSurfaces.Should().NotContain("sk-test");
        durableSurfaces.Should().NotContain("C:\\server");
        durableSurfaces.Should().NotContain("SELECT * FROM payroll");
        durableSurfaces.Should().NotContain("Bearer abc123");
        durableSurfaces.Should().NotContain("/var/private/provider.pem");
    }

    [Fact]
    public async Task AgentTaskRuntime_ShouldRejectOutputMismatchBeforeAnySuccessSideEffect()
    {
        const string toolCode = "invalid_runtime_output_fixture";
        const string maliciousProperty =
            "Bearer abc123 C:\\private\\provider.sql SELECT * FROM payroll -----BEGIN PRIVATE KEY-----";
        var tool = CreateTool(
            toolCode,
            outputSchemaJson:
            """{"type":"object","properties":{"ok":{"type":"boolean"}},"required":["ok"],"additionalProperties":false}""");
        var (task, workspace) = CreateDownstreamRuntimeApprovedTask(toolCode);
        var approvalRepository = new InMemoryRepository<ApprovalRequest>();
        var executionRepository = new InMemoryToolExecutionAuditStore();
        var attempts = new InMemoryAgentTaskRunAttemptStore();
        var runtime = CreateRuntime(
            new InMemoryRepository<AgentTask>(task),
            new InMemoryRepository<ArtifactWorkspace>(workspace),
            approvalRepository,
            executionRepository,
            CreateGuard(tool),
            toolExecutors:
            [
                new FixedResultAgentToolExecutor(
                    toolCode,
                    AgentToolExecutionResult.From(new Dictionary<string, object?>
                    {
                        [maliciousProperty] = true
                    }))
            ],
            runAttemptRepository: attempts,
            freshReadGate: AgentPlanV2TestData.CreateDownstreamRuntimeHarnessFreshReadGate());

        var result = await runtime.RunAsync(task, CancellationToken.None);

        result.IsSuccess.Should().BeTrue("runtime failures are persisted as a typed task outcome");
        task.Status.Should().Be(AgentTaskStatus.Failed);
        var step = task.Steps.Should().ContainSingle().Which;
        step.Status.Should().Be(AgentStepStatus.Failed);
        step.OutputJson.Should().BeNull();
        var record = executionRepository.Items.Should().ContainSingle().Which;
        record.Status.Should().Be(ToolExecutionStatus.Failed);
        record.ErrorCode.Should().Be(AppProblemCodes.ToolOutputSchemaInvalid);
        record.OutputSummary.Should().BeNull();
        record.ArtifactId.Should().BeNull();
        approvalRepository.Items.Should().BeEmpty("no success checkpoint can be created after invalid output");
        workspace.Artifacts.Should().BeEmpty();
        attempts.Items.Should().ContainSingle().Which.Status.Should().Be(AgentTaskRunAttemptStatus.Failed);
        attempts.Items.Should().NotContain(item => item.Status == AgentTaskRunAttemptStatus.Succeeded);
        var dto = await CreateAgentTaskDtoQueryService(
                new InMemoryRepository<ArtifactWorkspace>(workspace),
                approvalRepository,
                new InMemoryAgentTaskRunQueueStore())
            .MapAsync(task, CancellationToken.None);
        var failureSurfaces = string.Join('\n',
            task.FinalSummary,
            step.ErrorMessage,
            step.OutputJson,
            record.ErrorMessage,
            record.OutputSummary,
            record.AuditMetadata,
            JsonSerializer.Serialize(dto));
        failureSurfaces.Should().NotContain("Bearer abc123");
        failureSurfaces.Should().NotContain("C:\\private");
        failureSurfaces.Should().NotContain("SELECT * FROM payroll");
        failureSurfaces.Should().NotContain("PRIVATE KEY");
    }

    [Fact]
    public async Task AgentTaskRuntime_ShouldRejectOversizedOutputBeforeAnySuccessSideEffect()
    {
        const string toolCode = "oversized_runtime_output_fixture";
        var tool = CreateTool(
            toolCode,
            outputSchemaJson:
            """{"type":"object","properties":{"value":{"type":"string"}},"required":["value"],"additionalProperties":false}""");
        var (task, workspace) = CreateDownstreamRuntimeApprovedTask(toolCode);
        var approvalRepository = new InMemoryRepository<ApprovalRequest>();
        var executionRepository = new InMemoryToolExecutionAuditStore();
        var attempts = new InMemoryAgentTaskRunAttemptStore();
        var runtime = CreateRuntime(
            new InMemoryRepository<AgentTask>(task),
            new InMemoryRepository<ArtifactWorkspace>(workspace),
            approvalRepository,
            executionRepository,
            CreateGuard(tool),
            toolExecutors:
            [
                new DeferredResultAgentToolExecutor(
                    toolCode,
                    () => AgentToolExecutionResult.From(new
                    {
                        value = new string('x', AgentStructuredPayloadPolicyV1.MaxInlineOutputUtf8Bytes)
                    }))
            ],
            runAttemptRepository: attempts,
            freshReadGate: AgentPlanV2TestData.CreateDownstreamRuntimeHarnessFreshReadGate());

        var result = await runtime.RunAsync(task, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        task.Status.Should().Be(AgentTaskStatus.Failed);
        task.Steps.Single().Status.Should().Be(AgentStepStatus.Failed);
        task.Steps.Single().OutputJson.Should().BeNull();
        var record = executionRepository.Items.Should().ContainSingle().Which;
        record.Status.Should().Be(ToolExecutionStatus.Failed);
        record.ErrorCode.Should().Be(AppProblemCodes.EvidencePayloadTooLarge);
        record.OutputSummary.Should().BeNull();
        approvalRepository.Items.Should().BeEmpty();
        workspace.Artifacts.Should().BeEmpty();
        attempts.Items.Should().ContainSingle().Which.Status.Should().Be(AgentTaskRunAttemptStatus.Failed);
        attempts.Items.Should().NotContain(item => item.Status == AgentTaskRunAttemptStatus.Succeeded);
    }

    [Fact]
    public async Task AgentTaskRuntime_ShouldBlockMcpTool_WhenRuntimeSafetyPolicyRejectsIt()
    {
        static async Task AssertBlockedAsync(
            string serverName,
            string toolName,
            AiToolExternalSystemType externalSystemType,
            bool readOnlyDeclared,
            bool? mcpReadOnlyHint,
            bool? mcpDestructiveHint,
            AiToolCapabilityKind capabilityKind = AiToolCapabilityKind.ReadOnlyQuery)
        {
            using var provider = new ServiceCollection().BuildServiceProvider();
            var loader = new AgentPluginLoader([], provider);
            var toolCode = AiToolIdentity.CreateRuntimeName(AiToolTargetType.McpServer, serverName, toolName);
            var invoked = false;
            loader.RegisterAgentPlugin(new GenericBridgePlugin
            {
                Name = serverName,
                Description = "MCP runtime bridge",
                ChatExposureMode = ChatExposureMode.Advisory,
                Tools =
                [
                    new AiToolDefinition
                    {
                        Name = toolCode,
                        ToolName = toolName,
                        Description = "Query device logs without changing state.",
                        Kind = AiToolCallKind.Mcp,
                        TargetType = AiToolTargetType.McpServer,
                        TargetName = serverName,
                        ServerName = serverName,
                        ExternalSystemType = externalSystemType,
                        CapabilityKind = capabilityKind,
                        RiskLevel = AiToolRiskLevel.Low,
                        ReadOnlyDeclared = readOnlyDeclared,
                        McpReadOnlyHint = mcpReadOnlyHint,
                        McpDestructiveHint = mcpDestructiveHint,
                        JsonSchema = JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone(),
                        ReturnJsonSchema = JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone(),
                        InvokeAsync = (_, _) =>
                        {
                            invoked = true;
                            return ValueTask.FromResult<object?>("unexpected");
                        }
                    }
                ]
            });
            (AgentTask Task, ArtifactWorkspace Workspace) fixture;
            try
            {
                fixture = CreateApprovedTask(toolCode, requiresApproval: true);
            }
            catch (InvalidOperationException exception)
            {
                exception.Message.Should().Contain("Test PlanDraft fixture violates Plan v2");
                invoked.Should().BeFalse();
                return;
            }

            var (task, workspace) = fixture;
            var approval = new ApprovalRequest(
                task.Id,
                AgentApprovalType.ToolCall,
                task.Steps.Single().Id.Value.ToString(),
                UserId,
                DateTimeOffset.UtcNow);
            approval.Approve(UserId, "approved", DateTimeOffset.UtcNow);
            var executionRepository = new InMemoryToolExecutionAuditStore();
            var runtime = CreateRuntime(
                new InMemoryRepository<AgentTask>(task),
                new InMemoryRepository<ArtifactWorkspace>(workspace),
                new InMemoryRepository<ApprovalRequest>(approval),
                executionRepository,
                CreateGuard(CreateTool(
                    toolCode,
                    ToolProviderType.Mcp,
                    ToolRegistrationTargetType.McpServer,
                    serverName,
                    requiresApproval: true,
                    riskLevel: AiToolRiskLevel.RequiresApproval)),
                toolExecutors: [new McpAgentToolExecutor(loader, provider)],
                freshReadGate: AgentPlanV2TestData.CreateDownstreamRuntimeHarnessFreshReadGate());

            var result = await runtime.RunAsync(task, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            invoked.Should().BeFalse("runtime safety must be re-evaluated before every invocation delegate call");
            task.Status.Should().Be(AgentTaskStatus.Failed);
            var record = executionRepository.Items.Should().ContainSingle().Which;
            record.Status.Should().Be(ToolExecutionStatus.Rejected);
            record.ErrorCode.Should().Be(AppProblemCodes.AgentPlanToolDenied);
        }

        await AssertBlockedAsync(
            "runtime-mcp",
            "query_device_logs",
            AiToolExternalSystemType.CloudReadOnly,
            readOnlyDeclared: false,
            mcpReadOnlyHint: true,
            mcpDestructiveHint: false);
        await AssertBlockedAsync(
            "gateway-a17",
            "deleteDevice",
            AiToolExternalSystemType.NonCloud,
            readOnlyDeclared: true,
            mcpReadOnlyHint: true,
            mcpDestructiveHint: false,
            capabilityKind: AiToolCapabilityKind.SideEffecting);
        await AssertBlockedAsync(
            "production-cloud",
            "query_device_logs",
            AiToolExternalSystemType.CloudReadOnly,
            readOnlyDeclared: true,
            mcpReadOnlyHint: true,
            mcpDestructiveHint: true);
        await AssertBlockedAsync(
            "unclassified-runtime",
            "query_device_logs",
            AiToolExternalSystemType.Unknown,
            readOnlyDeclared: true,
            mcpReadOnlyHint: true,
            mcpDestructiveHint: false);

        await AssertBuiltInPlanProviderDriftBlockedAsync();
    }

    private static async Task AssertBuiltInPlanProviderDriftBlockedAsync()
    {
        foreach (var providerType in new[] { ToolProviderType.Mcp, ToolProviderType.MockMcp })
        {
            await AssertProviderDriftBlockedAsync(providerType);
        }

        static async Task AssertProviderDriftBlockedAsync(ToolProviderType providerType)
        {
            const string toolCode = "generate_markdown_report";
            var now = DateTimeOffset.UtcNow;
            var requestedStep = new AgentPlanV2TestStep(
                "Generate Markdown",
                "Generate a controlled markdown draft.",
                AgentStepType.ArtifactGeneration,
                toolCode);
            var planJson = AgentPlanV2TestData.CreateCanonicalBuiltInPlanDraft(
                [requestedStep],
                AgentTaskType.ReportGeneration);
            var plan = JsonSerializer.Deserialize<AgentTaskPlanDocument>(planJson, AgentRuntimeJson.Options)!;
            var task = new AgentTask(
                SessionId.New(),
                UserId,
                "BuiltInOnly provider drift",
                "BuiltInOnly provider drift",
                AgentTaskType.ReportGeneration,
                AgentTaskRiskLevel.Low,
                null,
                planJson,
                now);
            foreach (var planStep in plan.Steps)
            {
                task.AddStep(
                    planStep.Title,
                    planStep.Description,
                    planStep.StepType,
                    planStep.ToolCode,
                    planStep.RequiresApproval,
                    now,
                    planStep.InputJson);
            }

            var workspace = new ArtifactWorkspace(
                task.Id,
                $"ws_{Guid.NewGuid():N}",
                @"C:\aicopilot-workspaces\built-in-provider-drift",
                "/api/aigateway/workspaces/built-in-provider-drift",
                now);
            task.AttachWorkspace(workspace.Id, now);
            task.ConfirmExecutablePlan(task.PlanJson, [], now);
            task.ApprovePlan(now);

            var seed = BuiltInToolRegistrations.FindAgentRuntimeTool(toolCode)!;
            var providerDrift = new ToolRegistration(
                seed.ToolCode,
                seed.DisplayName,
                seed.Description,
                providerType,
                seed.TargetType,
                seed.TargetName,
                seed.InputSchemaJson,
                seed.OutputSchemaJson,
                seed.RiskLevel,
                seed.RequiredPermission,
                seed.RequiresApproval,
                seed.IsEnabled,
                seed.TimeoutSeconds,
                seed.AuditLevel,
                now,
                seed.Category,
                seed.BusinessDomains,
                seed.DataBoundary,
                seed.IsVisibleToPlanner,
                seed.IsExecutableByAgent,
                seed.SchemaVersion,
                seed.CatalogVersion,
                seed.ApprovalPolicy);
            var executor = new CountingAgentToolExecutor(toolCode);
            var executions = new InMemoryToolExecutionAuditStore();
            var runtime = CreateRuntime(
                new InMemoryRepository<AgentTask>(task),
                new InMemoryRepository<ArtifactWorkspace>(workspace),
                new InMemoryRepository<ApprovalRequest>(),
                executions,
                CreateGuard(providerDrift),
                toolExecutors: [executor],
                freshReadGate: AgentPlanV2TestData.CreateDownstreamRuntimeHarnessFreshReadGate());

            var result = await runtime.RunAsync(task, CancellationToken.None);

            result.IsSuccess.Should().BeTrue(providerType.ToString());
            executor.InvocationCount.Should().Be(0, providerType.ToString());
            task.Status.Should().Be(AgentTaskStatus.Failed, providerType.ToString());
            task.Steps.Single(step => step.ToolCode == toolCode).Status
                .Should().Be(AgentStepStatus.Failed, providerType.ToString());
            task.Steps.Single(step => step.ToolCode == "finalize_artifacts").Status
                .Should().Be(AgentStepStatus.WaitingApproval, providerType.ToString());
            var rejected = executions.Items.Should().ContainSingle(providerType.ToString()).Which;
            rejected.Status.Should().Be(ToolExecutionStatus.Rejected, providerType.ToString());
            rejected.ErrorCode.Should().Be(AppProblemCodes.AgentPlanToolDenied, providerType.ToString());
            workspace.Artifacts.Should().BeEmpty(providerType.ToString());
        }
    }

    [Fact]
    public async Task AgentTaskRuntime_ShouldFailMcpTool_WhenRegistryInputSchemaDoesNotMatch()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var loader = new AgentPluginLoader([], provider);
        var toolCode = AiToolIdentity.CreateRuntimeName(AiToolTargetType.McpServer, "runtime-mcp", "query_status");
        var invoked = false;
        loader.RegisterAgentPlugin(new GenericBridgePlugin
        {
            Name = "runtime-mcp",
            Description = "MCP runtime bridge",
            ChatExposureMode = ChatExposureMode.Advisory,
            Tools =
            [
                new AiToolDefinition
                {
                    Name = toolCode,
                    ToolName = "query_status",
                    Kind = AiToolCallKind.Mcp,
                    TargetType = AiToolTargetType.McpServer,
                    TargetName = "runtime-mcp",
                    ServerName = "runtime-mcp",
                    ExternalSystemType = AiToolExternalSystemType.CloudReadOnly,
                    CapabilityKind = AiToolCapabilityKind.ReadOnlyQuery,
                    RiskLevel = AiToolRiskLevel.RequiresApproval,
                    ReadOnlyDeclared = true,
                    RequiresApproval = true,
                    JsonSchema = JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone(),
                    ReturnJsonSchema = JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone(),
                    InvokeAsync = (_, _) =>
                    {
                        invoked = true;
                        return ValueTask.FromResult<object?>("unexpected");
                    }
                }
            ]
        });
        var (task, workspace) = CreateApprovedTask(toolCode);
        var approval = new ApprovalRequest(task.Id, AgentApprovalType.ToolCall, task.Steps.Single().Id.Value.ToString(), UserId, DateTimeOffset.UtcNow);
        approval.Approve(UserId, "approved", DateTimeOffset.UtcNow);
        var executionRepository = new InMemoryToolExecutionAuditStore();
        var runtime = CreateRuntime(
            new InMemoryRepository<AgentTask>(task),
            new InMemoryRepository<ArtifactWorkspace>(workspace),
            new InMemoryRepository<ApprovalRequest>(approval),
            executionRepository,
            CreateGuard(CreateTool(
                toolCode,
                ToolProviderType.Mcp,
                ToolRegistrationTargetType.McpServer,
                "runtime-mcp",
                requiresApproval: true,
                riskLevel: AiToolRiskLevel.RequiresApproval,
                inputSchemaJson: """{"type":"object","required":["input"],"properties":{"input":{"type":"string"}}}""")),
            toolExecutors: [new McpAgentToolExecutor(loader, provider)],
            freshReadGate: AgentPlanV2TestData.CreateDownstreamRuntimeHarnessFreshReadGate());

        var result = await runtime.RunAsync(task, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        invoked.Should().BeFalse();
        task.Status.Should().Be(AgentTaskStatus.Failed);
        var record = executionRepository.Items.Should().ContainSingle().Which;
        record.Status.Should().Be(ToolExecutionStatus.Rejected);
        record.ErrorCode.Should().Be(AppProblemCodes.AgentPlanToolDenied);
    }

    private static (AgentTask Task, ArtifactWorkspace Workspace) CreateDownstreamRuntimeApprovedTask(
        string toolCode,
        bool requiresApproval = false)
    {
        var now = DateTimeOffset.UtcNow;
        var planJson = AgentPlanV2TestData.CreateSingleStep(
            toolCode,
            executable: false,
            requiresApproval: requiresApproval);
        var task = new AgentTask(
            SessionId.New(),
            UserId,
            "Downstream runtime fixture",
            "Downstream runtime fixture",
            AgentTaskType.ReportGeneration,
            AgentTaskRiskLevel.Low,
            null,
            planJson,
            now);
        var plan = JsonSerializer.Deserialize<AgentTaskPlanDocument>(planJson, AgentRuntimeJson.Options)!;
        var trackedSteps = plan.Steps
            .Select(step => task.AddStep(
                step.Title,
                step.Description,
                step.StepType,
                step.ToolCode,
                step.RequiresApproval,
                now,
                step.InputJson))
            .ToArray();
        var workspace = new ArtifactWorkspace(
            task.Id,
            $"ws_{Guid.NewGuid():N}",
            @"C:\aicopilot-workspaces\test",
            "/api/aigateway/workspaces/test",
            now);
        task.AttachWorkspace(workspace.Id, now);
        task.ConfirmExecutablePlan(
            planJson,
            trackedSteps.Where(step => step.RequiresApproval).Select(step => step.StepIndex).ToArray(),
            now);
        task.ApprovePlan(now);
        return (task, workspace);
    }


    private sealed class FixedResultAgentToolExecutor(
        string toolCode,
        AgentToolExecutionResult result) : IAgentToolExecutor
    {
        public bool CanExecute(ToolRegistration tool, AgentStep step)
        {
            return string.Equals(tool.ToolCode, toolCode, StringComparison.Ordinal);
        }

        public Task<AgentToolExecutionResult> ExecuteAsync(AgentToolExecutionContext context)
        {
            return Task.FromResult(result);
        }
    }

    private sealed class DeferredResultAgentToolExecutor(
        string toolCode,
        Func<AgentToolExecutionResult> resultFactory) : IAgentToolExecutor
    {
        public bool CanExecute(ToolRegistration tool, AgentStep step)
        {
            return string.Equals(tool.ToolCode, toolCode, StringComparison.Ordinal);
        }

        public Task<AgentToolExecutionResult> ExecuteAsync(AgentToolExecutionContext context)
        {
            return Task.FromResult(resultFactory());
        }
    }

    private sealed class ContextResultAgentToolExecutor(
        string toolCode,
        Func<AgentToolExecutionContext, AgentToolExecutionResult> resultFactory) : IAgentToolExecutor
    {
        public bool CanExecute(ToolRegistration tool, AgentStep step)
        {
            return string.Equals(tool.ToolCode, toolCode, StringComparison.Ordinal);
        }

        public Task<AgentToolExecutionResult> ExecuteAsync(AgentToolExecutionContext context)
        {
            return Task.FromResult(resultFactory(context));
        }
    }

    private sealed class CountingAgentToolExecutor(string toolCode) : IAgentToolExecutor
    {
        public int InvocationCount { get; private set; }

        public bool CanExecute(ToolRegistration tool, AgentStep step)
        {
            return string.Equals(tool.ToolCode, toolCode, StringComparison.Ordinal);
        }

        public Task<AgentToolExecutionResult> ExecuteAsync(AgentToolExecutionContext context)
        {
            InvocationCount++;
            return Task.FromResult(AgentToolExecutionResult.From(new { unexpected = true }));
        }
    }
}
