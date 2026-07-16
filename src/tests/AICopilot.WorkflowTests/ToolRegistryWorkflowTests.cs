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
            runAttemptRepository: new InMemoryAgentTaskRunAttemptStore(attempt));

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
            CreateGuard(CreateTool("generate_chart_data")),
            toolExecutors: [new TestAgentToolExecutor(_ => true)],
            runAttemptRepository: runAttemptRepository);

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
    public async Task AgentTaskRuntime_ShouldRejectToolOutsidePersistedSkill()
    {
        var (task, workspace) = CreateApprovedTask("generate_pdf", skillCode: "restricted_skill");
        var executionRepository = new InMemoryToolExecutionAuditStore();
        var invoked = false;
        var runtime = CreateRuntime(
            new InMemoryRepository<AgentTask>(task),
            new InMemoryRepository<ArtifactWorkspace>(workspace),
            new InMemoryRepository<ApprovalRequest>(),
            executionRepository,
            CreateGuard(CreateTool("generate_pdf", ToolProviderType.Artifact, requiresApproval: true, riskLevel: AiToolRiskLevel.RequiresApproval)),
            toolExecutors: [new TestAgentToolExecutor(_ =>
            {
                invoked = true;
                return true;
            })],
            skillDefinitionGuard: CreateSkillGuard(CreateSkill("restricted_skill", ["generate_markdown_report"])));

        var result = await runtime.RunAsync(task, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        invoked.Should().BeFalse();
        task.Status.Should().Be(AgentTaskStatus.Failed);
        var record = executionRepository.Items.Should().ContainSingle().Which;
        record.Status.Should().Be(ToolExecutionStatus.Rejected);
        record.ErrorCode.Should().Be(AppProblemCodes.AgentPlanToolDenied);
        record.ErrorMessage.Should().Contain("outside skill");
    }
    [Fact]
    public async Task RetryAgentTaskCommand_ShouldResetFailedStep_AndEnqueueRetry()
    {
        var (task, workspace) = CreateApprovedTask("generate_chart_data");
        var now = DateTimeOffset.UtcNow;
        task.Start(now);
        var step = task.Steps.Single();
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
            queueRepository);
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
            queueRepository);
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
        var approval = new ApprovalRequest(task.Id, AgentApprovalType.ToolCall, task.Steps.Single().Id.Value.ToString(), task.UserId, now);
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
            queueRepository);
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
        var step = task.Steps.Single();
        task.WaitForToolApproval(now);
        var approval = new ApprovalRequest(task.Id, AgentApprovalType.ToolCall, step.Id.Value.ToString(), task.UserId, now);
        var queueRepository = new InMemoryAgentTaskRunQueueStore();
        var handler = new ApproveAgentApprovalCommandHandler(
            new AgentApprovalDecisionCoordinator(
                new InMemoryRepository<ApprovalRequest>(approval),
                new InMemoryRepository<AgentTask>(task),
                new InMemoryRepository<ArtifactWorkspace>(workspace),
                new AgentAuditRecorder(new CapturingAuditLogWriter()),
                new AgentTaskRunQueue(queueRepository),
                new TestCurrentUser(UserId),
                new StubIdentityAccessService([AgentApprovalPermissions.ApproveAgentToolCall]),
                new AgentPlanDraftConfirmationService(
                    CreatePlanToolGuard(CreateAgentRuntimeGuardWithCloudEnabled()),
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
            CreateGuard(CreateTool("generate_chart_data", isEnabled: false)));

        var result = await runtime.RunAsync(task, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        task.Status.Should().Be(AgentTaskStatus.Failed);
        task.Steps.Single().Status.Should().Be(AgentStepStatus.Failed);
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
            CreateGuard(CreateTool("generate_chart_data", requiresApproval: true)));

        var result = await runtime.RunAsync(task, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        task.Status.Should().Be(AgentTaskStatus.WaitingToolApproval);
        task.Steps.Single().Status.Should().Be(AgentStepStatus.WaitingApproval);
        executionRepository.Items.Should().BeEmpty();
        var approval = approvalRepository.Items.Should().ContainSingle().Which;
        approval.ApprovalType.Should().Be(AgentApprovalType.ToolCall);
        approval.TargetId.Should().Be(task.Steps.Single().Id.Value.ToString());
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
            new ThrowingWorkspaceService(throwOnWrite: true));

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
            task.Steps.Single().Id,
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
            task.Steps.Single().Id,
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
            foreignTask.Steps.Single().Id,
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
        var step = task.Steps.Single();
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
                CreateTool("generate_markdown_report", ToolProviderType.Artifact)),
            workspaceService,
            CreateRealCloudReadonlyExecutor(new FixedSemanticQueryPlanner(semanticPlan), cloudClient));

        var result = await runtime.RunAsync(task, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        task.Steps.Should().OnlyContain(step => step.Status == AgentStepStatus.Completed);
        cloudClient.RequestedPlans.Should().ContainSingle()
            .Which.Intent.Should().Be("Analysis.Device.List");
        workspaceService.TextArtifacts.Should().ContainKey("draft/report.md");
        workspaceService.TextArtifacts["draft/report.md"].Should().Contain("DEV-001");
        executionRepository.Items.Should().HaveCount(2);
        executionRepository.Items.First().Status.Should().Be(ToolExecutionStatus.Succeeded);
        executionRepository.Items.First().OutputSummary.Should().Contain("Cloud AiRead");
        executionRepository.Items.First().OutputSummary!.ToLowerInvariant().Should().NotContain("select ");
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
                    @"apiKey: sk-test C:\cloud\secret.txt Host=db;Password=super-secret; missing device/time"))));

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
    public async Task AgentTaskRuntime_ShouldExecuteApprovedMcpTool_AndWriteRedactedExecutionRecord()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var loader = new AgentPluginLoader([], provider);
        var toolCode = AiToolIdentity.CreateRuntimeName(AiToolTargetType.McpServer, "runtime-mcp", "query_status");
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
                    InvokeAsync = (_, _) => ValueTask.FromResult<object?>(new
                    {
                        status = "ok",
                        token = "sk-test",
                        path = @"C:\server\secret.txt"
                    })
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
                riskLevel: AiToolRiskLevel.RequiresApproval)),
            toolExecutors: [new McpAgentToolExecutor(loader, provider)]);

        var result = await runtime.RunAsync(task, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var record = executionRepository.Items.Should().ContainSingle().Which;
        record.Status.Should().Be(ToolExecutionStatus.Succeeded);
        record.OutputSummary.Should().Contain("runtime-mcp");
        record.OutputSummary.Should().Contain("token=******");
        record.OutputSummary.Should().Contain("[redacted-path]");
        record.OutputSummary.Should().NotContain("sk-test");
        record.OutputSummary.Should().NotContain("C:\\");
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
            var (task, workspace) = CreateApprovedTask(toolCode, requiresApproval: true);
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
                toolExecutors: [new McpAgentToolExecutor(loader, provider)]);

            var result = await runtime.RunAsync(task, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            invoked.Should().BeFalse("runtime safety must be re-evaluated before every invocation delegate call");
            task.Status.Should().Be(AgentTaskStatus.Failed);
            var record = executionRepository.Items.Should().ContainSingle().Which;
            record.Status.Should().Be(ToolExecutionStatus.Failed);
            record.ErrorCode.Should().Be(AppProblemCodes.ToolBlocked);
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
            toolExecutors: [new McpAgentToolExecutor(loader, provider)]);

        var result = await runtime.RunAsync(task, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        invoked.Should().BeFalse();
        task.Status.Should().Be(AgentTaskStatus.Failed);
        var record = executionRepository.Items.Should().ContainSingle().Which;
        record.Status.Should().Be(ToolExecutionStatus.Failed);
        record.ErrorCode.Should().Be(AppProblemCodes.AgentPlanSchemaInvalid);
    }
}
