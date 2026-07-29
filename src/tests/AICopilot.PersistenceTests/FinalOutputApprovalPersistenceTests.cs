using System.Text;
using AICopilot.AiGatewayService;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Sessions;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.AuditLogs;
using AICopilot.EntityFrameworkCore.Persistence;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AICopilot.PersistenceTests;

[Collection(PostgresPersistenceTestCollection.Name)]
public sealed class FinalOutputApprovalPersistenceTests(PostgresPersistenceFixture fixture)
{
    [Fact]
    public async Task AgentExecutionRetry_ShouldKeepScopedRuntimeAggregateTrackingIsolated()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fileStore = new ToolRegistryGovernanceTestBase.InMemoryArtifactWorkspaceFileStore();
        var seeded = await SeedAndPrepareAsync(database.ConnectionString, fileStore);
        using var host = CreateStoreHost(database.ConnectionString, fileStore);
        using (var approvalScope = host.Services.CreateScope())
        {
            var approved = await approvalScope.ServiceProvider
                .GetRequiredService<IFinalOutputApprovalStore>()
                .DecideAsync(new FinalOutputApprovalDecision(
                    seeded.ApprovalId,
                    Guid.NewGuid(),
                    IsApproved: true,
                    "approve before execution retry isolation test",
                    seeded.Proof,
                    DateTimeOffset.UtcNow));
            approved.Status.Should().Be(FinalOutputApprovalCommandStatus.Approved);
        }

        DurableTaskClaim claim;
        using (var claimScope = host.Services.CreateScope())
        {
            claim = (await claimScope.ServiceProvider
                .GetRequiredService<IAgentDurableTaskClaimStore>()
                .TryClaimNextAsync(
                    "final-output-retry-isolation-test",
                    TimeSpan.FromMinutes(5)))!;
            claim.Should().NotBeNull();
            (await claimScope.ServiceProvider
                    .GetRequiredService<IAgentDurableTaskClaimStore>()
                    .TryMarkStartedAsync(
                        claim,
                        DateTimeOffset.UtcNow))
                .Should()
                .Be(AgentFencedWriteResult.Succeeded);
        }

        await InstallFailFirstFinalizationAuditTriggerAsync(database.ConnectionString);
        using var scope = host.Services.CreateScope();
        var scopedContext = scope.ServiceProvider.GetRequiredService<AiGatewayDbContext>();
        var runtimeTask = await scopedContext.AgentTasks
            .Include(item => item.Steps)
            .SingleAsync(item => item.Id == seeded.TaskId);

        var result = await scope.ServiceProvider
            .GetRequiredService<IAgentDurableTaskClaimStore>()
            .TryRequireFinalizationReconciliationAsync(
                claim,
                "Transient audit retry must not replace scoped runtime aggregate tracking.",
                DateTimeOffset.UtcNow);

        result.Should().Be(AgentFencedWriteResult.Succeeded);
        scopedContext.ChangeTracker
            .Entries<AgentTask>()
            .Should()
            .ContainSingle(entry => ReferenceEquals(entry.Entity, runtimeTask));
        var update = () => scopedContext.Update(runtimeTask);
        update.Should().NotThrow();
    }

    [Fact]
    public async Task FinalizationLeaseRenewal_ShouldRejectEveryExpiredAuthorityLeaseWithoutRevival()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fileStore = new ToolRegistryGovernanceTestBase.InMemoryArtifactWorkspaceFileStore();
        var seeded = await SeedAndPrepareAsync(database.ConnectionString, fileStore);
        using var host = CreateStoreHost(database.ConnectionString, fileStore);
        using (var approvalScope = host.Services.CreateScope())
        {
            var approved = await approvalScope.ServiceProvider
                .GetRequiredService<IFinalOutputApprovalStore>()
                .DecideAsync(new FinalOutputApprovalDecision(
                    seeded.ApprovalId,
                    Guid.NewGuid(),
                    IsApproved: true,
                    "approve before expired lease renewal test",
                    seeded.Proof,
                    DateTimeOffset.UtcNow));
            approved.Status.Should().Be(FinalOutputApprovalCommandStatus.Approved);
        }

        DurableTaskClaim taskClaim;
        using (var taskClaimScope = host.Services.CreateScope())
        {
            var taskClaimStore = taskClaimScope.ServiceProvider
                .GetRequiredService<IAgentDurableTaskClaimStore>();
            taskClaim = (await taskClaimStore.TryClaimNextAsync(
                "final-output-expired-renewal-test",
                TimeSpan.FromMinutes(5)))!;
            taskClaim.Should().NotBeNull();
            (await taskClaimStore.TryMarkStartedAsync(
                    taskClaim,
                    DateTimeOffset.UtcNow))
                .Should()
                .Be(AgentFencedWriteResult.Succeeded);
        }

        var workerNow = taskClaim.RunAttempt.StartedAt.AddSeconds(2);
        await using (var authorityContext = CreateAiGatewayContext(database.ConnectionString))
        {
            var attempt = await authorityContext.AgentTaskRunAttempts.SingleAsync(item =>
                item.Id == taskClaim.RunAttempt.Id);
            attempt.InitializeBudget(new AgentRunBudgetLimits(
                "final-output-expired-renewal-test:v1",
                MaxNodes: 2,
                MaxToolCalls: 2,
                MaxModelCalls: 0,
                MaxInputTokens: 0,
                MaxOutputTokens: 0,
                MaxElapsedSeconds: 600,
                MaxCostAmount: 0,
                CostCurrency: "CNY",
                MaxRetries: 0,
                MaxArtifactCount: 1,
                MaxArtifactBytes: 1_048_576));
            var finalNode = await authorityContext.Set<AgentNodeRun>().SingleAsync(node =>
                node.Id == seeded.FinalNodeRunId);
            finalNode.BindTaskClaim(
                taskClaim.QueueItem.Id,
                taskClaim.TaskFencingToken,
                workerNow);
            await authorityContext.SaveChangesAsync();
        }

        AgentNodeRunClaim nodeClaim;
        using (var nodeClaimScope = host.Services.CreateScope())
        {
            (await nodeClaimScope.ServiceProvider
                    .GetRequiredService<IAgentNodeRunStore>()
                    .TryReleaseApprovalAsync(
                        seeded.FinalNodeRunId,
                        taskClaim.RunAttempt.Id,
                        taskClaim.TaskFencingToken,
                        workerNow))
                .Should()
                .Be(AgentFencedWriteResult.Succeeded);
            var claimStore = nodeClaimScope.ServiceProvider
                .GetRequiredService<IAgentNodeRunClaimStore>();
            var outcome = await claimStore.TryClaimAsync(
                seeded.FinalNodeRunId,
                taskClaim.RunAttempt.Id,
                taskClaim.TaskFencingToken,
                "final-output-expired-renewal-test",
                TimeSpan.FromMinutes(5),
                workerNow);
            outcome.Code.Should().Be(AgentNodeRunClaimOutcomeCode.Claimed);
            nodeClaim = outcome.Claim!;
            (await claimStore.TryMarkRunningAsync(
                    nodeClaim,
                    workerNow.AddMilliseconds(1)))
                .Should()
                .Be(AgentFencedWriteResult.Succeeded);
        }

        var renewalAt = workerNow.AddSeconds(1);
        foreach (var expiredAuthority in new[] { "task", "attempt", "queue", "node" })
        {
            var expiredAt = renewalAt.AddMilliseconds(-1);
            var validUntil = renewalAt.AddMinutes(5);
            await using (var mutation = CreateAiGatewayContext(database.ConnectionString))
            {
                (await mutation.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     UPDATE aigateway.agent_tasks
                     SET run_lease_expires_at = {(expiredAuthority == "task" ? expiredAt : validUntil)}
                     WHERE id = {taskClaim.Task.Id.Value}
                     """)).Should().Be(1);
                (await mutation.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     UPDATE aigateway.agent_task_run_attempts
                     SET lease_expires_at = {(expiredAuthority == "attempt" ? expiredAt : validUntil)}
                     WHERE id = {taskClaim.RunAttempt.Id.Value}
                     """)).Should().Be(1);
                (await mutation.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     UPDATE aigateway.agent_task_run_queue_items
                     SET lease_expires_at = {(expiredAuthority == "queue" ? expiredAt : validUntil)}
                     WHERE id = {taskClaim.QueueItem.Id.Value}
                     """)).Should().Be(1);
                (await mutation.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     UPDATE aigateway.agent_node_runs
                     SET lease_expires_at = {(expiredAuthority == "node" ? expiredAt : validUntil)}
                     WHERE id = {nodeClaim.NodeRun.Id.Value}
                     """)).Should().Be(1);
            }

            var before = await ReadLeaseAuthorityAsync();
            using (var renewalScope = host.Services.CreateScope())
            {
                (await renewalScope.ServiceProvider
                        .GetRequiredService<IAgentNodeRunClaimStore>()
                        .TryRenewTaskAndNodeLeaseAsync(
                            nodeClaim,
                            TimeSpan.FromMinutes(5),
                            TimeSpan.FromMinutes(5),
                            renewalAt))
                    .Should()
                    .Be(AgentFencedWriteResult.StaleFence);
            }

            (await ReadLeaseAuthorityAsync()).Should().Be(before);
        }

        async Task<(
            DateTimeOffset? Task,
            DateTimeOffset? Attempt,
            DateTimeOffset? Queue,
            DateTimeOffset? Node)> ReadLeaseAuthorityAsync()
        {
            await using var context = CreateAiGatewayContext(database.ConnectionString);
            return (
                await context.AgentTasks
                    .AsNoTracking()
                    .Where(item => item.Id == taskClaim.Task.Id)
                    .Select(item => item.RunLeaseExpiresAt)
                    .SingleAsync(),
                await context.AgentTaskRunAttempts
                    .AsNoTracking()
                    .Where(item => item.Id == taskClaim.RunAttempt.Id)
                    .Select(item => item.LeaseExpiresAt)
                    .SingleAsync(),
                await context.AgentTaskRunQueueItems
                    .AsNoTracking()
                    .Where(item => item.Id == taskClaim.QueueItem.Id)
                    .Select(item => item.LeaseExpiresAt)
                    .SingleAsync(),
                await context.Set<AgentNodeRun>()
                    .AsNoTracking()
                    .Where(item => item.Id == nodeClaim.NodeRun.Id)
                    .Select(item => item.LeaseExpiresAt)
                    .SingleAsync());
        }
    }

    [Fact]
    public async Task TimelineSequence_ShouldSerializeConcurrentRepositoryWriters()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var session = new Session(Guid.NewGuid(), ConversationTemplateId.New());
        await using (var seedContext = CreateAiGatewayContext(database.ConnectionString))
        {
            seedContext.Sessions.Add(session);
            await seedContext.SaveChangesAsync();
        }

        var fileStore = new ToolRegistryGovernanceTestBase.InMemoryArtifactWorkspaceFileStore();
        using var host = CreateStoreHost(database.ConnectionString, fileStore);
        using var firstScope = host.Services.CreateScope();
        using var secondScope = host.Services.CreateScope();
        var createdAt = DateTimeOffset.UtcNow;
        firstScope.ServiceProvider
            .GetRequiredService<IMessageTimelineProjectionStore>()
            .Add(MessageEvent.FromProjection(
                session.Id,
                sequence: 1,
                MessageEventType.AgentTaskPlanCreated,
                createdAt));
        secondScope.ServiceProvider
            .GetRequiredService<IMessageTimelineProjectionStore>()
            .Add(MessageEvent.FromProjection(
                session.Id,
                sequence: 1,
                MessageEventType.FinalOutputReady,
                createdAt.AddMilliseconds(1)));

        var affectedRows = await Task.WhenAll(
            firstScope.ServiceProvider
                .GetRequiredService<IRepository<Session>>()
                .SaveChangesAsync(),
            secondScope.ServiceProvider
                .GetRequiredService<IRepository<Session>>()
                .SaveChangesAsync());

        affectedRows.Should().OnlyContain(count => count > 0);
        using (var messageScope = host.Services.CreateScope())
        {
            await messageScope.ServiceProvider
                .GetRequiredService<SessionMessagePersistenceService>()
                .AppendAsync(
                    session.Id.Value,
                    "message after concurrent projections",
                    MessageType.Assistant);
        }

        await using var verification = CreateAiGatewayContext(database.ConnectionString);
        var timeline = await verification.MessageEvents
                .AsNoTracking()
                .Where(messageEvent => messageEvent.SessionId == session.Id)
                .OrderBy(messageEvent => messageEvent.Sequence)
                .ToArrayAsync();
        timeline
            .Select(messageEvent => messageEvent.Sequence)
            .Should()
            .Equal(1, 2, 3);
        var message = await verification.Messages
            .AsNoTracking()
            .SingleAsync(candidate => candidate.SessionId == session.Id);
        message.Sequence.Should().Be(3);
        timeline.Single(messageEvent => messageEvent.MessageId == message.Id)
            .Sequence.Should().Be(message.Sequence);
    }

    [Fact]
    public async Task Prepare_ShouldSerializeConcurrentCreatorsIntoOneProofBoundApproval()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fileStore = new ToolRegistryGovernanceTestBase.InMemoryArtifactWorkspaceFileStore();
        var seeded = await SeedPreApprovalAsync(database.ConnectionString, fileStore);
        using var host = CreateStoreHost(database.ConnectionString, fileStore);

        using var firstScope = host.Services.CreateScope();
        using var secondScope = host.Services.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<IFinalOutputApprovalStore>();
        var second = secondScope.ServiceProvider.GetRequiredService<IFinalOutputApprovalStore>();
        var preparation = new FinalOutputApprovalPreparation(
            seeded.TaskId,
            seeded.UserId,
            seeded.Proof,
            DateTimeOffset.UtcNow);

        var results = await Task.WhenAll(
            first.PrepareAsync(preparation),
            second.PrepareAsync(preparation));

        results.Select(result => result.Status).Should().BeEquivalentTo(
            [
                FinalOutputApprovalCommandStatus.Created,
                FinalOutputApprovalCommandStatus.ExistingPending
            ]);
        await using var verification = CreateAiGatewayContext(database.ConnectionString);
        var approval = await verification.ApprovalRequests.AsNoTracking().SingleAsync();
        approval.HasValidFinalOutputProof().Should().BeTrue();
        approval.Status.Should().Be(AgentApprovalStatus.Pending);
        var requestedProjection = await verification.MessageEvents
            .AsNoTracking()
            .SingleAsync(messageEvent =>
                messageEvent.ApprovalRequestId == approval.Id &&
                messageEvent.EventType == MessageEventType.ApprovalRequested);
        requestedProjection.AgentTaskId.Should().Be(seeded.TaskId);
        requestedProjection.ArtifactWorkspaceId.Should().Be(seeded.WorkspaceId);
        (await verification.AgentTaskRunQueueItems.CountAsync(item =>
            item.TriggerType == AgentTaskRunTriggerType.ApprovalResume)).Should().Be(0);
        var task = await verification.AgentTasks
            .Include(item => item.Steps)
            .AsNoTracking()
            .SingleAsync();
        task.Status.Should().Be(AgentTaskStatus.WaitingFinalApproval);
        task.Steps.Single(step => step.Id == seeded.FinalStepId)
            .Status.Should().Be(AgentStepStatus.WaitingApproval);
        var attempt = await verification.AgentTaskRunAttempts.AsNoTracking().SingleAsync();
        attempt.Status.Should().Be(AgentTaskRunAttemptStatus.WaitingApproval);
        await using var audit = new AuditDbContext(
            PostgresPersistenceTestOptions.CreateAudit(database.ConnectionString));
        (await audit.AuditLogs.AsNoTracking().CountAsync(entry =>
                entry.ActionCode == "Agent.FinalReviewSubmitted" &&
                entry.TargetId == approval.Id.Value.ToString()))
            .Should().Be(1);
        var originatingQueue = await verification.AgentTaskRunQueueItems
            .AsNoTracking()
            .SingleAsync(item => item.SourceApprovalRequestId == null);
        originatingQueue.Status.Should().Be(AgentTaskRunQueueStatus.Succeeded);
        originatingQueue.CompletedAt.Should().NotBeNull();
        originatingQueue.LeaseId.Should().BeNull();
    }

    [Fact]
    public async Task Prepare_ShouldRetireOriginatingQueueBeforeWorkerCompletionOrLeaseRecovery()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fileStore = new ToolRegistryGovernanceTestBase.InMemoryArtifactWorkspaceFileStore();
        var seeded = await SeedPreApprovalAsync(database.ConnectionString, fileStore);
        using var host = CreateStoreHost(database.ConnectionString, fileStore);
        DurableTaskClaim originalClaim;
        using (var claimSnapshotScope = host.Services.CreateScope())
        {
            var context = claimSnapshotScope.ServiceProvider
                .GetRequiredService<AiGatewayDbContext>();
            var task = await context.AgentTasks
                .Include(item => item.Steps)
                .AsNoTracking()
                .SingleAsync();
            var attempt = await context.AgentTaskRunAttempts
                .AsNoTracking()
                .SingleAsync();
            var queue = await context.AgentTaskRunQueueItems
                .AsNoTracking()
                .SingleAsync();
            originalClaim = new DurableTaskClaim(
                queue,
                task,
                attempt,
                task.RunFencingToken,
                task.RunLeaseId!.Value,
                task.RunLeaseExpiresAt!.Value);
        }

        using (var prepareScope = host.Services.CreateScope())
        {
            var prepared = await prepareScope.ServiceProvider
                .GetRequiredService<IFinalOutputApprovalStore>()
                .PrepareAsync(new FinalOutputApprovalPreparation(
                    seeded.TaskId,
                    seeded.UserId,
                    seeded.Proof,
                    DateTimeOffset.UtcNow));
            prepared.Status.Should().Be(FinalOutputApprovalCommandStatus.Created);
        }

        using (var lateCompletionScope = host.Services.CreateScope())
        {
            var completed = await lateCompletionScope.ServiceProvider
                .GetRequiredService<IAgentDurableTaskClaimStore>()
                .TryCompleteAsync(
                    originalClaim,
                    AgentTaskRunQueueStatus.Succeeded,
                    failureCode: null,
                    "late worker completion after atomic approval pause",
                    DateTimeOffset.UtcNow);
            completed.Should().Be(AgentFencedWriteResult.Succeeded);
        }

        using (var recoveryScope = host.Services.CreateScope())
        {
            var recovered = await recoveryScope.ServiceProvider
                .GetRequiredService<IAgentDurableTaskClaimStore>()
                .RecoverExpiredStartedAsync(
                    DateTimeOffset.UtcNow.AddHours(1),
                    maxItems: 32);
            recovered.Should().Be(0);
        }

        using (var decisionScope = host.Services.CreateScope())
        {
            var approval = await decisionScope.ServiceProvider
                .GetRequiredService<AiGatewayDbContext>()
                .ApprovalRequests
                .AsNoTracking()
                .SingleAsync();
            var decided = await decisionScope.ServiceProvider
                .GetRequiredService<IFinalOutputApprovalStore>()
                .DecideAsync(new FinalOutputApprovalDecision(
                    approval.Id,
                    Guid.NewGuid(),
                    IsApproved: true,
                    "approve after simulated worker exit",
                    seeded.Proof,
                    DateTimeOffset.UtcNow));
            decided.Status.Should().Be(FinalOutputApprovalCommandStatus.Approved);
        }

        await using var verification = CreateAiGatewayContext(database.ConnectionString);
        var queues = await verification.AgentTaskRunQueueItems
            .AsNoTracking()
            .ToArrayAsync();
        queues.Should().HaveCount(2);
        var originatingQueue = queues.Single(item => item.SourceApprovalRequestId is null);
        originatingQueue.Status.Should().Be(AgentTaskRunQueueStatus.Succeeded);
        originatingQueue.TaskFencingToken.Should().Be(seeded.Proof.TaskFencingToken);
        var resumeQueue = queues.Single(item => item.SourceApprovalRequestId is not null);
        resumeQueue.Status.Should().Be(AgentTaskRunQueueStatus.Queued);
        (await verification.AgentTasks.AsNoTracking().SingleAsync())
            .RunFencingToken.Should().Be(seeded.Proof.TaskFencingToken);
    }

    [Fact]
    public async Task Prepare_ShouldRejectApprovalTimestampAfterRunLeaseExpiry()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fileStore = new ToolRegistryGovernanceTestBase.InMemoryArtifactWorkspaceFileStore();
        var seeded = await SeedPreApprovalAsync(database.ConnectionString, fileStore);
        using var host = CreateStoreHost(database.ConnectionString, fileStore);
        using var scope = host.Services.CreateScope();

        var result = await scope.ServiceProvider
            .GetRequiredService<IFinalOutputApprovalStore>()
            .PrepareAsync(new FinalOutputApprovalPreparation(
                seeded.TaskId,
                seeded.UserId,
                seeded.Proof,
                DateTimeOffset.UtcNow.AddHours(1)));

        result.Status.Should().Be(FinalOutputApprovalCommandStatus.FinalizationConflict);
        await using var verification = CreateAiGatewayContext(database.ConnectionString);
        (await verification.ApprovalRequests.CountAsync()).Should().Be(0);
        (await verification.AgentTaskRunQueueItems.AsNoTracking().SingleAsync())
            .Status.Should().Be(AgentTaskRunQueueStatus.Started);
    }

    [Fact]
    public async Task DraftVersionCommit_ShouldFailClosedWhenFinalReviewSealsAfterPrevalidation()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fileStore = new ToolRegistryGovernanceTestBase.InMemoryArtifactWorkspaceFileStore();
        var seeded = await SeedPreApprovalAsync(database.ConnectionString, fileStore);
        using var host = CreateStoreHost(database.ConnectionString, fileStore);
        using var editScope = host.Services.CreateScope();
        var editContext = editScope.ServiceProvider.GetRequiredService<AiGatewayDbContext>();
        var workspaceRepository = editScope.ServiceProvider
            .GetRequiredService<IRepository<ArtifactWorkspace>>();
        var workspace = await editContext.ArtifactWorkspaces
            .Include(candidate => candidate.Artifacts)
            .SingleAsync(candidate => candidate.Id == seeded.WorkspaceId);
        var artifact = workspace.Artifacts.Single(candidate => candidate.Id == seeded.ArtifactId);
        (await editContext.ApprovalRequests
                .AsNoTracking()
                .AnyAsync(candidate =>
                    candidate.TaskId == seeded.TaskId &&
                    candidate.ApprovalType == AgentApprovalType.FinalOutput))
            .Should().BeFalse("the stale editor passed the pre-commit review-window check");

        artifact.AddVersion(
            $"draft/{Guid.NewGuid():N}/report.md",
            artifact.FileSize + 1,
            DateTimeOffset.UtcNow);
        workspaceRepository.Update(workspace);

        using (var approvalScope = host.Services.CreateScope())
        {
            var prepared = await approvalScope.ServiceProvider
                .GetRequiredService<IFinalOutputApprovalStore>()
                .PrepareAsync(new FinalOutputApprovalPreparation(
                    seeded.TaskId,
                    seeded.UserId,
                    seeded.Proof,
                    DateTimeOffset.UtcNow));
            prepared.Status.Should().Be(FinalOutputApprovalCommandStatus.Created);
        }

        var commit = () => workspaceRepository.SaveChangesAsync();
        var failure = await commit.Should()
            .ThrowAsync<ArtifactFinalReviewMutationConflictException>();
        failure.Which.TaskId.Should().Be(seeded.TaskId.Value);
        editContext.ChangeTracker.HasChanges().Should().BeFalse();

        await using var verification = CreateAiGatewayContext(database.ConnectionString);
        var persisted = await verification.Set<Artifact>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == seeded.ArtifactId);
        persisted.Version.Should().Be(1);
        persisted.RelativePath.Should().Be(seeded.ArtifactRelativePath);
        (await verification.ApprovalRequests.CountAsync(candidate =>
                candidate.TaskId == seeded.TaskId &&
                candidate.ApprovalType == AgentApprovalType.FinalOutput))
            .Should().Be(1);
    }

    [Fact]
    public async Task DraftVersionCommitAndFinalReviewSeal_ShouldHaveExactlyOneWinner()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fileStore = new ToolRegistryGovernanceTestBase.InMemoryArtifactWorkspaceFileStore();
        var seeded = await SeedPreApprovalAsync(database.ConnectionString, fileStore);
        using var host = CreateStoreHost(database.ConnectionString, fileStore);
        using var editScope = host.Services.CreateScope();
        using var approvalScope = host.Services.CreateScope();
        var editContext = editScope.ServiceProvider.GetRequiredService<AiGatewayDbContext>();
        var workspaceRepository = editScope.ServiceProvider
            .GetRequiredService<IRepository<ArtifactWorkspace>>();
        var workspace = await editContext.ArtifactWorkspaces
            .Include(candidate => candidate.Artifacts)
            .SingleAsync(candidate => candidate.Id == seeded.WorkspaceId);
        var artifact = workspace.Artifacts.Single(candidate => candidate.Id == seeded.ArtifactId);
        artifact.AddVersion(
            $"draft/{Guid.NewGuid():N}/report.md",
            artifact.FileSize + 1,
            DateTimeOffset.UtcNow);
        workspaceRepository.Update(workspace);

        var editTask = Record.ExceptionAsync(
            () => workspaceRepository.SaveChangesAsync());
        var approvalTask = approvalScope.ServiceProvider
            .GetRequiredService<IFinalOutputApprovalStore>()
            .PrepareAsync(new FinalOutputApprovalPreparation(
                seeded.TaskId,
                seeded.UserId,
                seeded.Proof,
                DateTimeOffset.UtcNow));
        await Task.WhenAll(editTask, approvalTask);

        var editFailure = await editTask;
        var approval = await approvalTask;
        if (approval.Status == FinalOutputApprovalCommandStatus.Created)
        {
            editFailure.Should().BeOfType<ArtifactFinalReviewMutationConflictException>();
        }
        else
        {
            approval.Status.Should().Be(FinalOutputApprovalCommandStatus.FinalizationConflict);
            editFailure.Should().BeNull();
        }

        await using var verification = CreateAiGatewayContext(database.ConnectionString);
        var persisted = await verification.Set<Artifact>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == seeded.ArtifactId);
        var approvalCount = await verification.ApprovalRequests.CountAsync(candidate =>
            candidate.TaskId == seeded.TaskId &&
            candidate.ApprovalType == AgentApprovalType.FinalOutput);
        if (approval.Status == FinalOutputApprovalCommandStatus.Created)
        {
            persisted.Version.Should().Be(1);
            approvalCount.Should().Be(1);
        }
        else
        {
            persisted.Version.Should().Be(2);
            approvalCount.Should().Be(0);
        }
    }

    [Fact]
    public async Task Decide_ShouldMakeConcurrentApproveApproveIdempotentWithOneResumeQueue()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fileStore = new ToolRegistryGovernanceTestBase.InMemoryArtifactWorkspaceFileStore();
        var seeded = await SeedAndPrepareAsync(database.ConnectionString, fileStore);
        using var host = CreateStoreHost(database.ConnectionString, fileStore);
        using var firstScope = host.Services.CreateScope();
        using var secondScope = host.Services.CreateScope();
        var decidedAt = DateTimeOffset.UtcNow;

        var results = await Task.WhenAll(
            firstScope.ServiceProvider.GetRequiredService<IFinalOutputApprovalStore>()
                .DecideAsync(new FinalOutputApprovalDecision(
                    seeded.ApprovalId,
                    Guid.NewGuid(),
                    IsApproved: true,
                    "approve-a",
                    seeded.Proof,
                    decidedAt)),
            secondScope.ServiceProvider.GetRequiredService<IFinalOutputApprovalStore>()
                .DecideAsync(new FinalOutputApprovalDecision(
                    seeded.ApprovalId,
                    Guid.NewGuid(),
                    IsApproved: true,
                    "approve-b",
                    seeded.Proof,
                    decidedAt)));

        results.Select(result => result.Status).Should().BeEquivalentTo(
            [
                FinalOutputApprovalCommandStatus.Approved,
                FinalOutputApprovalCommandStatus.DuplicateDecision
            ]);
        await using var verification = CreateAiGatewayContext(database.ConnectionString);
        var approval = await verification.ApprovalRequests.AsNoTracking().SingleAsync();
        approval.Status.Should().Be(AgentApprovalStatus.Approved);
        approval.HasValidFinalOutputDecisionProof().Should().BeTrue();
        (await verification.MessageEvents.AsNoTracking().CountAsync(messageEvent =>
                messageEvent.ApprovalRequestId == approval.Id &&
                messageEvent.EventType == MessageEventType.ApprovalDecided))
            .Should().Be(1);
        var resume = await verification.AgentTaskRunQueueItems
            .AsNoTracking()
            .Where(item => item.TriggerType == AgentTaskRunTriggerType.ApprovalResume)
            .SingleAsync();
        resume.SourceApprovalRequestId.Should().Be(approval.Id);
        resume.RunAttemptId.Should().BeNull(
            "approval only enqueues; the durable worker owns the later claim");

        var task = await verification.AgentTasks
            .Include(item => item.Steps)
            .AsNoTracking()
            .SingleAsync();
        task.Status.Should().Be(AgentTaskStatus.WaitingFinalApproval);
        task.Steps.Single(step => step.Id == seeded.FinalStepId)
            .Status.Should().Be(AgentStepStatus.Approved);
        var finalNode = await verification.Set<AgentNodeRun>()
            .AsNoTracking()
            .SingleAsync(node => node.Id == seeded.FinalNodeRunId);
        finalNode.Status.Should().Be(AgentNodeRunStatus.WaitingApproval);
        (await verification.ArtifactWorkspaces.AsNoTracking().SingleAsync())
            .Status.Should().Be(ArtifactWorkspaceStatus.Active);
        await using var audit = new AuditDbContext(
            PostgresPersistenceTestOptions.CreateAudit(database.ConnectionString));
        (await audit.AuditLogs.AsNoTracking().CountAsync(entry =>
                entry.ActionCode == "Agent.ApprovalDecision" &&
                entry.TargetId == approval.Id.Value.ToString()))
            .Should().Be(1);
    }

    [Theory]
    [InlineData(
        AgentTaskRunQueueStatus.Failed,
        AgentTaskStatus.Failed,
        AgentTaskRunAttemptStatus.Failed)]
    [InlineData(
        AgentTaskRunQueueStatus.Cancelled,
        AgentTaskStatus.Cancelled,
        AgentTaskRunAttemptStatus.Cancelled)]
    public async Task Decide_ShouldKeepApprovedDecisionIdempotentAfterTerminalResumeOutcome(
        AgentTaskRunQueueStatus terminalQueueStatus,
        AgentTaskStatus expectedTaskStatus,
        AgentTaskRunAttemptStatus expectedAttemptStatus)
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fileStore = new ToolRegistryGovernanceTestBase.InMemoryArtifactWorkspaceFileStore();
        var seeded = await SeedAndPrepareAsync(database.ConnectionString, fileStore);
        using var host = CreateStoreHost(database.ConnectionString, fileStore);
        var decidedAt = DateTimeOffset.UtcNow;

        using (var approvalScope = host.Services.CreateScope())
        {
            var approved = await approvalScope.ServiceProvider
                .GetRequiredService<IFinalOutputApprovalStore>()
                .DecideAsync(new FinalOutputApprovalDecision(
                    seeded.ApprovalId,
                    Guid.NewGuid(),
                    IsApproved: true,
                    "approve before downstream terminal outcome",
                    seeded.Proof,
                    decidedAt));
            approved.Status.Should().Be(FinalOutputApprovalCommandStatus.Approved);
        }

        using (var workerScope = host.Services.CreateScope())
        {
            var claimStore = workerScope.ServiceProvider
                .GetRequiredService<IAgentDurableTaskClaimStore>();
            var claim = await claimStore.TryClaimNextAsync(
                "final-output-terminal-decision-test",
                TimeSpan.FromMinutes(5));
            claim.Should().NotBeNull();
            var startedAt = decidedAt.AddSeconds(1);
            (await claimStore.TryMarkStartedAsync(claim!, startedAt))
                .Should().Be(AgentFencedWriteResult.Succeeded);
            (await claimStore.TryCompleteAsync(
                    claim!,
                    terminalQueueStatus,
                    terminalQueueStatus == AgentTaskRunQueueStatus.Failed
                        ? "final_output_downstream_failed"
                        : null,
                    "Final-output resume reached a downstream terminal outcome.",
                    startedAt.AddSeconds(1)))
                .Should().Be(AgentFencedWriteResult.Succeeded);
        }

        using (var duplicateScope = host.Services.CreateScope())
        {
            var duplicate = await duplicateScope.ServiceProvider
                .GetRequiredService<IFinalOutputApprovalStore>()
                .DecideAsync(new FinalOutputApprovalDecision(
                    seeded.ApprovalId,
                    Guid.NewGuid(),
                    IsApproved: true,
                    "late duplicate approve",
                    seeded.Proof,
                    decidedAt.AddSeconds(3)));
            duplicate.Status.Should().Be(FinalOutputApprovalCommandStatus.DuplicateDecision);
            duplicate.Task!.Status.Should().Be(expectedTaskStatus);
            duplicate.QueueItem!.Status.Should().Be(terminalQueueStatus);
        }

        using (var oppositeScope = host.Services.CreateScope())
        {
            var opposite = await oppositeScope.ServiceProvider
                .GetRequiredService<IFinalOutputApprovalStore>()
                .DecideAsync(new FinalOutputApprovalDecision(
                    seeded.ApprovalId,
                    Guid.NewGuid(),
                    IsApproved: false,
                    "late opposite reject",
                    seeded.Proof,
                    decidedAt.AddSeconds(4)));
            opposite.Status.Should().Be(FinalOutputApprovalCommandStatus.DecisionConflict);
        }

        await using var verification = CreateAiGatewayContext(database.ConnectionString);
        (await verification.ApprovalRequests.AsNoTracking().SingleAsync())
            .Status.Should().Be(AgentApprovalStatus.Approved);
        (await verification.AgentTasks.AsNoTracking().SingleAsync())
            .Status.Should().Be(expectedTaskStatus);
        (await verification.AgentTaskRunAttempts.AsNoTracking().SingleAsync())
            .Status.Should().Be(expectedAttemptStatus);
        (await verification.AgentTaskRunQueueItems
                .AsNoTracking()
                .SingleAsync(item => item.SourceApprovalRequestId == seeded.ApprovalId))
            .Status.Should().Be(terminalQueueStatus);
        (await verification.AgentTaskRunQueueItems.CountAsync(item =>
            item.TriggerType == AgentTaskRunTriggerType.ApprovalResume)).Should().Be(1);
    }

    [Theory]
    [InlineData("requested-by")]
    [InlineData("created-at")]
    [InlineData("available-at")]
    public async Task ApprovalResumeClaim_ShouldFailClosedWhenDecisionQueueTupleDrifts(
        string drift)
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fileStore = new ToolRegistryGovernanceTestBase.InMemoryArtifactWorkspaceFileStore();
        var seeded = await SeedAndPrepareAsync(database.ConnectionString, fileStore);
        using var host = CreateStoreHost(database.ConnectionString, fileStore);
        using (var approvalScope = host.Services.CreateScope())
        {
            var approved = await approvalScope.ServiceProvider
                .GetRequiredService<IFinalOutputApprovalStore>()
                .DecideAsync(new FinalOutputApprovalDecision(
                    seeded.ApprovalId,
                    Guid.NewGuid(),
                    IsApproved: true,
                    "approve before queue drift",
                    seeded.Proof,
                    DateTimeOffset.UtcNow));
            approved.Status.Should().Be(FinalOutputApprovalCommandStatus.Approved);
        }

        await using (var mutation = CreateAiGatewayContext(database.ConnectionString))
        {
            var affected = drift switch
            {
                "requested-by" => await mutation.Database.ExecuteSqlInterpolatedAsync($$"""
                    UPDATE aigateway.agent_task_run_queue_items
                    SET requested_by = {{Guid.NewGuid()}}
                    WHERE source_approval_request_id = {{seeded.ApprovalId.Value}}
                    """),
                "created-at" => await mutation.Database.ExecuteSqlRawAsync(
                    """
                    UPDATE aigateway.agent_task_run_queue_items
                    SET created_at = created_at + INTERVAL '1 second'
                    WHERE source_approval_request_id IS NOT NULL
                    """),
                "available-at" => await mutation.Database.ExecuteSqlRawAsync(
                    """
                    UPDATE aigateway.agent_task_run_queue_items
                    SET available_at = available_at - INTERVAL '1 second'
                    WHERE source_approval_request_id IS NOT NULL
                    """),
                _ => throw new ArgumentOutOfRangeException(nameof(drift), drift, null)
            };
            affected.Should().Be(1);
        }

        var laterTaskId = await SeedQueuedTaskAsync(
            database.ConnectionString,
            seeded.UserId);
        using (var claimScope = host.Services.CreateScope())
        {
            var claim = await claimScope.ServiceProvider
                .GetRequiredService<IAgentDurableTaskClaimStore>()
                .TryClaimNextAsync(
                    "final-output-queue-drift-test",
                    TimeSpan.FromMinutes(5));
            claim.Should().BeNull();
        }

        await using var verification = CreateAiGatewayContext(database.ConnectionString);
        (await verification.AgentTasks.AsNoTracking().SingleAsync(item =>
                item.Id == seeded.TaskId))
            .Status.Should().Be(AgentTaskStatus.ReconciliationRequired);
        (await verification.AgentTaskRunAttempts.AsNoTracking().SingleAsync(item =>
                item.Id == seeded.RunAttemptId))
            .Status.Should().Be(AgentTaskRunAttemptStatus.ReconciliationRequired);
        var poison = await verification.AgentTaskRunQueueItems
            .AsNoTracking()
            .SingleAsync(item => item.SourceApprovalRequestId == seeded.ApprovalId);
        poison.Status.Should().Be(AgentTaskRunQueueStatus.DeadLetter);
        poison.FailureCode.Should().Be(AppProblemCodes.AgentFinalizationStateConflict);
        await using (var audit = new AuditDbContext(
                         PostgresPersistenceTestOptions.CreateAudit(database.ConnectionString)))
        {
            (await audit.AuditLogs
                    .AsNoTracking()
                    .CountAsync(entry =>
                        entry.ActionCode == "Agent.FinalizationReconciliationRequired" &&
                        entry.TargetType == "AgentTaskRunQueueItem" &&
                        entry.TargetId == poison.Id.Value.ToString()))
                .Should().Be(1);
        }

        using var nextClaimScope = host.Services.CreateScope();
        var nextClaim = await nextClaimScope.ServiceProvider
            .GetRequiredService<IAgentDurableTaskClaimStore>()
            .TryClaimNextAsync(
                "final-output-next-queue-test",
                TimeSpan.FromMinutes(5));
        nextClaim.Should().NotBeNull();
        nextClaim!.Task.Id.Should().Be(laterTaskId);
    }

    [Fact]
    public async Task ApprovalResumeRuntime_ShouldReconcileSourceDriftBeforeFinalNodeClaim()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fileStore = new ToolRegistryGovernanceTestBase.InMemoryArtifactWorkspaceFileStore();
        var seeded = await SeedAndPrepareAsync(database.ConnectionString, fileStore);
        using var host = CreateStoreHost(database.ConnectionString, fileStore);
        using (var approvalScope = host.Services.CreateScope())
        {
            var approved = await approvalScope.ServiceProvider
                .GetRequiredService<IFinalOutputApprovalStore>()
                .DecideAsync(new FinalOutputApprovalDecision(
                    seeded.ApprovalId,
                    Guid.NewGuid(),
                    IsApproved: true,
                    "approve before source drift",
                    seeded.Proof,
                    DateTimeOffset.UtcNow));
            approved.Status.Should().Be(FinalOutputApprovalCommandStatus.Approved);
        }

        DurableTaskClaim claim;
        using (var claimScope = host.Services.CreateScope())
        {
            claim = (await claimScope.ServiceProvider
                .GetRequiredService<IAgentDurableTaskClaimStore>()
                .TryClaimNextAsync(
                    "final-output-pre-claim-drift-test",
                    TimeSpan.FromMinutes(5)))!;
            claim.Should().NotBeNull();
        }

        await ApplyDriftAsync(
            database.ConnectionString,
            fileStore,
            seeded,
            "source-file",
            apply: true);
        using (var verificationScope = host.Services.CreateScope())
        {
            var context = verificationScope.ServiceProvider
                .GetRequiredService<AiGatewayDbContext>();
            var task = await context.AgentTasks
                .Include(item => item.Steps)
                .SingleAsync(item => item.Id == seeded.TaskId);
            var workspace = await context.ArtifactWorkspaces
                .Include(item => item.Artifacts)
                .SingleAsync(item => item.Id == seeded.WorkspaceId);
            var approval = await context.ApprovalRequests
                .SingleAsync(item => item.Id == seeded.ApprovalId);
            var verified = await verificationScope.ServiceProvider
                .GetRequiredService<FinalOutputApprovalCoordinator>()
                .VerifyCheckpointAsync(
                    task,
                    workspace,
                    approval,
                    allowApprovedCheckpoint: true,
                    CancellationToken.None);
            verified.IsSuccess.Should().BeFalse();
            verified.Errors!
                .OfType<ApiProblemDescriptor>()
                .Should()
                .ContainSingle(problem =>
                    problem.Code == AppProblemCodes.AgentFinalizationStateConflict);
        }

        using (var workerScope = host.Services.CreateScope())
        {
            await CreateFinalizationConflictWorker(workerScope.ServiceProvider)
                .ExecuteClaimAsync(claim, CancellationToken.None);
        }

        await using var verification = CreateAiGatewayContext(database.ConnectionString);
        (await verification.AgentTasks.AsNoTracking().SingleAsync(item =>
                item.Id == seeded.TaskId))
            .Should()
            .Match<AgentTask>(task =>
                task.Status == AgentTaskStatus.ReconciliationRequired &&
                task.ActiveRunAttemptId == seeded.RunAttemptId);
        (await verification.AgentTaskRunAttempts.AsNoTracking().SingleAsync(item =>
                item.Id == seeded.RunAttemptId))
            .Status.Should().Be(AgentTaskRunAttemptStatus.ReconciliationRequired);
        var queue = await verification.AgentTaskRunQueueItems
            .AsNoTracking()
            .SingleAsync(item => item.SourceApprovalRequestId == seeded.ApprovalId);
        queue.Status.Should().Be(AgentTaskRunQueueStatus.Started);
        queue.CompletedAt.Should().BeNull();
        queue.FailureCode.Should().BeNull();
        var finalNode = await verification.Set<AgentNodeRun>()
            .AsNoTracking()
            .SingleAsync(node => node.Id == seeded.FinalNodeRunId);
        finalNode.Status.Should().Be(AgentNodeRunStatus.WaitingApproval);
        (await verification.ArtifactFileSetOperations.CountAsync()).Should().Be(0);
        (await verification.ArtifactWorkspaces.AsNoTracking().SingleAsync(item =>
                item.Id == seeded.WorkspaceId))
            .Status.Should().Be(ArtifactWorkspaceStatus.Active);

        using (var duplicateScope = host.Services.CreateScope())
        {
            (await duplicateScope.ServiceProvider
                    .GetRequiredService<IAgentDurableTaskClaimStore>()
                    .TryRequireFinalizationReconciliationAsync(
                        claim,
                        "Approved final-output source bytes drifted before NodeRun claim.",
                        DateTimeOffset.UtcNow))
                .Should()
                .Be(AgentFencedWriteResult.Duplicate);
        }

        using (var recoveryScope = host.Services.CreateScope())
        {
            (await recoveryScope.ServiceProvider
                    .GetRequiredService<IAgentDurableTaskClaimStore>()
                    .RecoverExpiredStartedAsync(
                        DateTimeOffset.UtcNow.AddHours(1),
                        maxItems: 32))
                .Should()
                .Be(0);
        }

        await using var audit = new AuditDbContext(
            PostgresPersistenceTestOptions.CreateAudit(database.ConnectionString));
        (await audit.AuditLogs.AsNoTracking().CountAsync(entry =>
                entry.ActionCode == "Agent.FinalizationReconciliationRequired" &&
                entry.TargetType == "ApprovalRequest" &&
                entry.TargetId == seeded.ApprovalId.Value.ToString()))
            .Should().Be(1);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("retargeted")]
    [InlineData("non-final-output")]
    public async Task ApprovalResumeRuntime_ShouldReconcileCorruptedSourceApprovalAuthority(
        string corruption)
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fileStore = new ToolRegistryGovernanceTestBase.InMemoryArtifactWorkspaceFileStore();
        var seeded = await SeedAndPrepareAsync(database.ConnectionString, fileStore);
        using var host = CreateStoreHost(database.ConnectionString, fileStore);
        using (var approvalScope = host.Services.CreateScope())
        {
            var approved = await approvalScope.ServiceProvider
                .GetRequiredService<IFinalOutputApprovalStore>()
                .DecideAsync(new FinalOutputApprovalDecision(
                    seeded.ApprovalId,
                    Guid.NewGuid(),
                    IsApproved: true,
                    "approve before source authority corruption",
                    seeded.Proof,
                    DateTimeOffset.UtcNow));
            approved.Status.Should().Be(FinalOutputApprovalCommandStatus.Approved);
        }

        DurableTaskClaim claim;
        using (var claimScope = host.Services.CreateScope())
        {
            claim = (await claimScope.ServiceProvider
                .GetRequiredService<IAgentDurableTaskClaimStore>()
                .TryClaimNextAsync(
                    $"final-output-source-authority-{corruption}",
                    TimeSpan.FromMinutes(5)))!;
            claim.Should().NotBeNull();
        }

        await CorruptClaimedSourceApprovalAsync(
            database.ConnectionString,
            seeded.ApprovalId,
            corruption);
        using (var workerScope = host.Services.CreateScope())
        {
            await CreateFinalizationConflictWorker(workerScope.ServiceProvider)
                .ExecuteClaimAsync(claim, CancellationToken.None);
        }

        await using var verification = CreateAiGatewayContext(database.ConnectionString);
        (await verification.AgentTasks.AsNoTracking().SingleAsync(item =>
                item.Id == seeded.TaskId))
            .Status.Should().Be(AgentTaskStatus.ReconciliationRequired);
        (await verification.AgentTaskRunAttempts.AsNoTracking().SingleAsync(item =>
                item.Id == seeded.RunAttemptId))
            .Status.Should().Be(AgentTaskRunAttemptStatus.ReconciliationRequired);
        var queue = await verification.AgentTaskRunQueueItems
            .AsNoTracking()
            .SingleAsync(item => item.SourceApprovalRequestId == seeded.ApprovalId);
        queue.Status.Should().Be(AgentTaskRunQueueStatus.Started);
        queue.CompletedAt.Should().BeNull();
        queue.FailureCode.Should().BeNull();

        using (var recoveryScope = host.Services.CreateScope())
        {
            (await recoveryScope.ServiceProvider
                    .GetRequiredService<IAgentDurableTaskClaimStore>()
                    .RecoverExpiredStartedAsync(
                        DateTimeOffset.UtcNow.AddHours(1),
                        maxItems: 32))
                .Should()
                .Be(0);
        }

        await using var audit = new AuditDbContext(
            PostgresPersistenceTestOptions.CreateAudit(database.ConnectionString));
        (await audit.AuditLogs.AsNoTracking().CountAsync(entry =>
                entry.ActionCode == "Agent.FinalizationReconciliationRequired" &&
                entry.TargetType == "ApprovalRequest" &&
                entry.TargetId == seeded.ApprovalId.Value.ToString()))
            .Should().Be(1);
    }

    [Fact]
    public async Task ApprovalResumeRuntime_ShouldNotFailClaimWhenReconciliationAuditRollsBack()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fileStore = new ToolRegistryGovernanceTestBase.InMemoryArtifactWorkspaceFileStore();
        var seeded = await SeedAndPrepareAsync(database.ConnectionString, fileStore);
        using var host = CreateStoreHost(database.ConnectionString, fileStore);
        using (var approvalScope = host.Services.CreateScope())
        {
            var approved = await approvalScope.ServiceProvider
                .GetRequiredService<IFinalOutputApprovalStore>()
                .DecideAsync(new FinalOutputApprovalDecision(
                    seeded.ApprovalId,
                    Guid.NewGuid(),
                    IsApproved: true,
                    "approve before reconciliation audit rollback",
                    seeded.Proof,
                    DateTimeOffset.UtcNow));
            approved.Status.Should().Be(FinalOutputApprovalCommandStatus.Approved);
        }

        DurableTaskClaim claim;
        using (var claimScope = host.Services.CreateScope())
        {
            claim = (await claimScope.ServiceProvider
                .GetRequiredService<IAgentDurableTaskClaimStore>()
                .TryClaimNextAsync(
                    "final-output-pre-claim-audit-test",
                    TimeSpan.FromMinutes(5)))!;
            claim.Should().NotBeNull();
        }

        await ApplyDriftAsync(
            database.ConnectionString,
            fileStore,
            seeded,
            "source-file",
            apply: true);
        await InstallRejectFinalizationAuditTriggerAsync(database.ConnectionString);
        using (var workerScope = host.Services.CreateScope())
        {
            await CreateFinalizationConflictWorker(workerScope.ServiceProvider)
                .ExecuteClaimAsync(claim, CancellationToken.None);
        }

        await using var verification = CreateAiGatewayContext(database.ConnectionString);
        (await verification.AgentTasks.AsNoTracking().SingleAsync(item =>
                item.Id == seeded.TaskId))
            .Status.Should().Be(AgentTaskStatus.WaitingFinalApproval);
        (await verification.AgentTaskRunAttempts.AsNoTracking().SingleAsync(item =>
                item.Id == seeded.RunAttemptId))
            .Status.Should().Be(AgentTaskRunAttemptStatus.Running);
        var queue = await verification.AgentTaskRunQueueItems
            .AsNoTracking()
            .SingleAsync(item => item.SourceApprovalRequestId == seeded.ApprovalId);
        queue.Status.Should().Be(AgentTaskRunQueueStatus.Started);
        queue.CompletedAt.Should().BeNull();
        queue.FailureCode.Should().BeNull();
        await using var audit = new AuditDbContext(
            PostgresPersistenceTestOptions.CreateAudit(database.ConnectionString));
        (await audit.AuditLogs.AsNoTracking().CountAsync(entry =>
                entry.ActionCode == "Agent.FinalizationReconciliationRequired"))
            .Should().Be(0);
    }

    [Fact]
    public async Task ApprovalResumeClaim_ShouldRollbackRetirementWhenReconciliationAuditFails()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fileStore = new ToolRegistryGovernanceTestBase.InMemoryArtifactWorkspaceFileStore();
        var seeded = await SeedAndPrepareAsync(database.ConnectionString, fileStore);
        using var host = CreateStoreHost(database.ConnectionString, fileStore);
        using (var approvalScope = host.Services.CreateScope())
        {
            var approved = await approvalScope.ServiceProvider
                .GetRequiredService<IFinalOutputApprovalStore>()
                .DecideAsync(new FinalOutputApprovalDecision(
                    seeded.ApprovalId,
                    Guid.NewGuid(),
                    IsApproved: true,
                    "approve before atomic audit failure",
                    seeded.Proof,
                    DateTimeOffset.UtcNow));
            approved.Status.Should().Be(FinalOutputApprovalCommandStatus.Approved);
        }

        await using (var mutation = CreateAiGatewayContext(database.ConnectionString))
        {
            (await mutation.Database.ExecuteSqlInterpolatedAsync($$"""
                UPDATE aigateway.agent_task_run_queue_items
                SET requested_by = {{Guid.NewGuid()}}
                WHERE source_approval_request_id = {{seeded.ApprovalId.Value}}
                """)).Should().Be(1);
            await mutation.Database.ExecuteSqlRawAsync(
                """
                CREATE FUNCTION public.reject_finalization_reconciliation_audit()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    RAISE EXCEPTION 'simulated non-transient reconciliation audit failure'
                        USING ERRCODE = '23514';
                END;
                $function$;

                CREATE TRIGGER reject_finalization_reconciliation_audit
                BEFORE INSERT ON public.audit_logs
                FOR EACH ROW
                WHEN (NEW.action_code = 'Agent.FinalizationReconciliationRequired')
                EXECUTE FUNCTION public.reject_finalization_reconciliation_audit();
                """);
        }

        using (var claimScope = host.Services.CreateScope())
        {
            var claim = () => claimScope.ServiceProvider
                .GetRequiredService<IAgentDurableTaskClaimStore>()
                .TryClaimNextAsync(
                    "final-output-atomic-audit-test",
                    TimeSpan.FromMinutes(5));
            await claim.Should().ThrowAsync<DbUpdateException>();
        }

        await using var verification = CreateAiGatewayContext(database.ConnectionString);
        (await verification.AgentTasks
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Id == seeded.TaskId))
            .Status.Should().Be(AgentTaskStatus.WaitingFinalApproval);
        (await verification.AgentTaskRunAttempts
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Id == seeded.RunAttemptId))
            .Status.Should().Be(AgentTaskRunAttemptStatus.WaitingApproval);
        (await verification.AgentTaskRunQueueItems
                .AsNoTracking()
                .SingleAsync(candidate => candidate.SourceApprovalRequestId == seeded.ApprovalId))
            .Status.Should().Be(AgentTaskRunQueueStatus.Queued);
        await using var audit = new AuditDbContext(
            PostgresPersistenceTestOptions.CreateAudit(database.ConnectionString));
        (await audit.AuditLogs.CountAsync(entry =>
                entry.ActionCode == "Agent.FinalizationReconciliationRequired"))
            .Should().Be(0);
    }

    [Fact]
    public async Task Decide_ShouldGiveExactlyOneWinnerToConcurrentApproveReject()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fileStore = new ToolRegistryGovernanceTestBase.InMemoryArtifactWorkspaceFileStore();
        var seeded = await SeedAndPrepareAsync(database.ConnectionString, fileStore);
        using var host = CreateStoreHost(database.ConnectionString, fileStore);
        using var approveScope = host.Services.CreateScope();
        using var rejectScope = host.Services.CreateScope();
        var decidedAt = DateTimeOffset.UtcNow;

        var results = await Task.WhenAll(
            approveScope.ServiceProvider.GetRequiredService<IFinalOutputApprovalStore>()
                .DecideAsync(new FinalOutputApprovalDecision(
                    seeded.ApprovalId,
                    Guid.NewGuid(),
                    IsApproved: true,
                    "approve",
                    seeded.Proof,
                    decidedAt)),
            rejectScope.ServiceProvider.GetRequiredService<IFinalOutputApprovalStore>()
                .DecideAsync(new FinalOutputApprovalDecision(
                    seeded.ApprovalId,
                    Guid.NewGuid(),
                    IsApproved: false,
                    "reject",
                    seeded.Proof,
                    decidedAt)));

        results.Should().ContainSingle(result =>
            result.Status == FinalOutputApprovalCommandStatus.Approved ||
            result.Status == FinalOutputApprovalCommandStatus.Rejected);
        results.Should().ContainSingle(result =>
            result.Status == FinalOutputApprovalCommandStatus.DecisionConflict);

        await using var verification = CreateAiGatewayContext(database.ConnectionString);
        var approval = await verification.ApprovalRequests.AsNoTracking().SingleAsync();
        approval.HasValidFinalOutputDecisionProof().Should().BeTrue();
        var task = await verification.AgentTasks
            .Include(item => item.Steps)
            .AsNoTracking()
            .SingleAsync();
        var attempt = await verification.AgentTaskRunAttempts.AsNoTracking().SingleAsync();
        var finalNode = await verification.Set<AgentNodeRun>()
            .AsNoTracking()
            .SingleAsync(node => node.Id == seeded.FinalNodeRunId);
        var resumeCount = await verification.AgentTaskRunQueueItems.CountAsync(item =>
            item.TriggerType == AgentTaskRunTriggerType.ApprovalResume);
        if (approval.Status == AgentApprovalStatus.Approved)
        {
            task.Status.Should().Be(AgentTaskStatus.WaitingFinalApproval);
            attempt.Status.Should().Be(AgentTaskRunAttemptStatus.WaitingApproval);
            finalNode.Status.Should().Be(AgentNodeRunStatus.WaitingApproval);
            resumeCount.Should().Be(1);
        }
        else
        {
            approval.Status.Should().Be(AgentApprovalStatus.Rejected);
            task.Status.Should().Be(AgentTaskStatus.Rejected);
            task.Steps.Single(step => step.Id == seeded.FinalStepId)
                .Status.Should().Be(AgentStepStatus.Failed);
            attempt.Status.Should().Be(AgentTaskRunAttemptStatus.Failed);
            finalNode.Status.Should().Be(AgentNodeRunStatus.Cancelled);
            resumeCount.Should().Be(0);
        }
    }

    [Fact]
    public async Task Decide_ShouldMakeRepeatedRejectTerminalAndRejectLateApprovalWithoutResume()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fileStore = new ToolRegistryGovernanceTestBase.InMemoryArtifactWorkspaceFileStore();
        var seeded = await SeedAndPrepareAsync(database.ConnectionString, fileStore);
        using var host = CreateStoreHost(database.ConnectionString, fileStore);
        var decidedAt = DateTimeOffset.UtcNow;

        FinalOutputApprovalCommandResult rejected;
        using (var scope = host.Services.CreateScope())
        {
            rejected = await scope.ServiceProvider
                .GetRequiredService<IFinalOutputApprovalStore>()
                .DecideAsync(new FinalOutputApprovalDecision(
                    seeded.ApprovalId,
                    Guid.NewGuid(),
                    IsApproved: false,
                    "reject",
                    seeded.Proof,
                    decidedAt));
        }

        FinalOutputApprovalCommandResult duplicate;
        using (var scope = host.Services.CreateScope())
        {
            duplicate = await scope.ServiceProvider
                .GetRequiredService<IFinalOutputApprovalStore>()
                .DecideAsync(new FinalOutputApprovalDecision(
                    seeded.ApprovalId,
                    Guid.NewGuid(),
                    IsApproved: false,
                    "late duplicate reject",
                    seeded.Proof,
                    decidedAt.AddSeconds(1)));
        }

        FinalOutputApprovalCommandResult opposite;
        using (var scope = host.Services.CreateScope())
        {
            opposite = await scope.ServiceProvider
                .GetRequiredService<IFinalOutputApprovalStore>()
                .DecideAsync(new FinalOutputApprovalDecision(
                    seeded.ApprovalId,
                    Guid.NewGuid(),
                    IsApproved: true,
                    "late approve",
                    seeded.Proof,
                    decidedAt.AddSeconds(2)));
        }

        rejected.Status.Should().Be(FinalOutputApprovalCommandStatus.Rejected);
        duplicate.Status.Should().Be(FinalOutputApprovalCommandStatus.DuplicateDecision);
        opposite.Status.Should().Be(FinalOutputApprovalCommandStatus.DecisionConflict);

        await using var verification = CreateAiGatewayContext(database.ConnectionString);
        var approval = await verification.ApprovalRequests.AsNoTracking().SingleAsync();
        approval.Status.Should().Be(AgentApprovalStatus.Rejected);
        approval.HasValidFinalOutputDecisionProof().Should().BeTrue();
        var task = await verification.AgentTasks
            .Include(item => item.Steps)
            .AsNoTracking()
            .SingleAsync();
        task.Status.Should().Be(AgentTaskStatus.Rejected);
        task.Steps.Single(step => step.Id == seeded.FinalStepId)
            .Status.Should().Be(AgentStepStatus.Failed);
        (await verification.AgentTaskRunAttempts.AsNoTracking().SingleAsync())
            .Status.Should().Be(AgentTaskRunAttemptStatus.Failed);
        (await verification.Set<AgentNodeRun>()
                .AsNoTracking()
                .SingleAsync(node => node.Id == seeded.FinalNodeRunId))
            .Status.Should().Be(AgentNodeRunStatus.Cancelled);
        (await verification.AgentTaskRunQueueItems.CountAsync(item =>
            item.TriggerType == AgentTaskRunTriggerType.ApprovalResume)).Should().Be(0);
    }

    [Fact]
    public async Task Decide_ShouldFailClosedForEveryPersistedAuthorityOrSourceByteDrift()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fileStore = new ToolRegistryGovernanceTestBase.InMemoryArtifactWorkspaceFileStore();
        var seeded = await SeedAndPrepareAsync(database.ConnectionString, fileStore);
        using var host = CreateStoreHost(database.ConnectionString, fileStore);
        var drifts = new[]
        {
            "workspace",
            "final-step",
            "attempt",
            "final-node",
            "task-fence",
            "node-fence",
            "evidence",
            "evidence-expiry",
            "artifact-metadata",
            "source-file"
        };

        foreach (var drift in drifts)
        {
            await ApplyDriftAsync(
                database.ConnectionString,
                fileStore,
                seeded,
                drift,
                apply: true);
            using (var scope = host.Services.CreateScope())
            {
                var result = await scope.ServiceProvider
                    .GetRequiredService<IFinalOutputApprovalStore>()
                    .DecideAsync(new FinalOutputApprovalDecision(
                        seeded.ApprovalId,
                        Guid.NewGuid(),
                        IsApproved: true,
                        $"drift-{drift}",
                        seeded.Proof,
                        DateTimeOffset.UtcNow));
                result.Status.Should().Be(
                    FinalOutputApprovalCommandStatus.FinalizationConflict,
                    $"'{drift}' drift must never be treated as an approval conflict or infrastructure success");
            }

            await ApplyDriftAsync(
                database.ConnectionString,
                fileStore,
                seeded,
                drift,
                apply: false);
        }

        using (var finalScope = host.Services.CreateScope())
        {
            var result = await finalScope.ServiceProvider
                .GetRequiredService<IFinalOutputApprovalStore>()
                .DecideAsync(new FinalOutputApprovalDecision(
                    seeded.ApprovalId,
                    Guid.NewGuid(),
                    IsApproved: true,
                    "authority restored",
                    seeded.Proof,
                    DateTimeOffset.UtcNow));
            result.Status.Should().Be(FinalOutputApprovalCommandStatus.Approved);
        }

        await using var verification = CreateAiGatewayContext(database.ConnectionString);
        (await verification.AgentTaskRunQueueItems.CountAsync(item =>
            item.TriggerType == AgentTaskRunTriggerType.ApprovalResume)).Should().Be(1);
    }

    [Fact]
    public async Task PrepareAndDecide_ShouldReloadTrackedAuthorityBeforeProofValidation()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fileStore = new ToolRegistryGovernanceTestBase.InMemoryArtifactWorkspaceFileStore();
        var seeded = await SeedPreApprovalAsync(database.ConnectionString, fileStore);
        using var host = CreateStoreHost(database.ConnectionString, fileStore);

        using (var stalePrepareScope = host.Services.CreateScope())
        {
            var scopedContext = stalePrepareScope.ServiceProvider
                .GetRequiredService<AiGatewayDbContext>();
            (await scopedContext.Set<Artifact>().SingleAsync(item =>
                    item.Id == seeded.ArtifactId))
                .Version.Should().Be(1);
            await ApplyDriftAsync(
                database.ConnectionString,
                fileStore,
                seeded,
                "artifact-metadata",
                apply: true);

            var result = await stalePrepareScope.ServiceProvider
                .GetRequiredService<IFinalOutputApprovalStore>()
                .PrepareAsync(new FinalOutputApprovalPreparation(
                    seeded.TaskId,
                    seeded.UserId,
                    seeded.Proof,
                    DateTimeOffset.UtcNow));

            result.Status.Should().Be(FinalOutputApprovalCommandStatus.FinalizationConflict);
        }

        await ApplyDriftAsync(
            database.ConnectionString,
            fileStore,
            seeded,
            "artifact-metadata",
            apply: false);
        FinalOutputApprovalCommandResult prepared;
        using (var prepareScope = host.Services.CreateScope())
        {
            prepared = await prepareScope.ServiceProvider
                .GetRequiredService<IFinalOutputApprovalStore>()
                .PrepareAsync(new FinalOutputApprovalPreparation(
                    seeded.TaskId,
                    seeded.UserId,
                    seeded.Proof,
                    DateTimeOffset.UtcNow));
        }

        prepared.Status.Should().Be(FinalOutputApprovalCommandStatus.Created);
        using (var staleDecisionScope = host.Services.CreateScope())
        {
            var scopedContext = staleDecisionScope.ServiceProvider
                .GetRequiredService<AiGatewayDbContext>();
            (await scopedContext.Set<Artifact>().SingleAsync(item =>
                    item.Id == seeded.ArtifactId))
                .Version.Should().Be(1);
            await ApplyDriftAsync(
                database.ConnectionString,
                fileStore,
                seeded,
                "artifact-metadata",
                apply: true);

            var result = await staleDecisionScope.ServiceProvider
                .GetRequiredService<IFinalOutputApprovalStore>()
                .DecideAsync(new FinalOutputApprovalDecision(
                    prepared.Approval!.Id,
                    Guid.NewGuid(),
                    IsApproved: true,
                    "must reject stale tracked authority",
                    seeded.Proof,
                    DateTimeOffset.UtcNow));

            result.Status.Should().Be(FinalOutputApprovalCommandStatus.FinalizationConflict);
        }

        await using var verification = CreateAiGatewayContext(database.ConnectionString);
        (await verification.ApprovalRequests.AsNoTracking().SingleAsync())
            .Status.Should().Be(AgentApprovalStatus.Pending);
        (await verification.AgentTaskRunQueueItems.CountAsync(item =>
            item.TriggerType == AgentTaskRunTriggerType.ApprovalResume)).Should().Be(0);
    }

    [Fact]
    public Task FinalCheckpoint_ShouldFreshReadAndRejectEvidenceExpiredAfterStaging() =>
        AssertFinalCheckpointAsync(expireEvidenceAfterStaging: true);

    [Fact]
    public Task FinalCheckpoint_ShouldAtomicallyCompleteAuthoritiesProjectionsAuditsAndResumeQueue() =>
        AssertFinalCheckpointAsync(expireEvidenceAfterStaging: false);

    private async Task AssertFinalCheckpointAsync(bool expireEvidenceAfterStaging)
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fileStore = new ToolRegistryGovernanceTestBase.InMemoryArtifactWorkspaceFileStore();
        var seeded = await SeedAndPrepareAsync(database.ConnectionString, fileStore);
        var fileSetStore = new ExpiringArtifactFileSetStore(
            database.ConnectionString,
            seeded.EvidenceId,
            expireEvidenceAfterStaging);
        using var host = CreateStoreHost(
            database.ConnectionString,
            fileStore,
            fileSetStore);

        using (var approvalScope = host.Services.CreateScope())
        {
            var approved = await approvalScope.ServiceProvider
                .GetRequiredService<IFinalOutputApprovalStore>()
                .DecideAsync(new FinalOutputApprovalDecision(
                    seeded.ApprovalId,
                    Guid.NewGuid(),
                    IsApproved: true,
                    "approve before worker stage",
                    seeded.Proof,
                    DateTimeOffset.UtcNow));
            approved.Status.Should().Be(FinalOutputApprovalCommandStatus.Approved);
        }

        DurableTaskClaim taskClaim;
        using (var taskClaimScope = host.Services.CreateScope())
        {
            var taskClaimStore = taskClaimScope.ServiceProvider
                .GetRequiredService<IAgentDurableTaskClaimStore>();
            taskClaim = (await taskClaimStore.TryClaimNextAsync(
                "final-output-expiry-test",
                TimeSpan.FromMinutes(5)))!;
            taskClaim.Should().NotBeNull();
            var taskStartedAt = taskClaim.RunAttempt.StartedAt.AddSeconds(1);
            (await taskClaimStore.TryMarkStartedAsync(
                    taskClaim,
                    taskStartedAt))
                .Should()
                .Be(AgentFencedWriteResult.Succeeded);
        }

        var workerNow = taskClaim.RunAttempt.StartedAt.AddSeconds(2);
        await using (var authorityContext = CreateAiGatewayContext(database.ConnectionString))
        {
            var attempt = await authorityContext.AgentTaskRunAttempts.SingleAsync(item =>
                item.Id == taskClaim.RunAttempt.Id);
            attempt.InitializeBudget(new AgentRunBudgetLimits(
                "final-output-expiry-test:v1",
                MaxNodes: 2,
                MaxToolCalls: 2,
                MaxModelCalls: 0,
                MaxInputTokens: 0,
                MaxOutputTokens: 0,
                MaxElapsedSeconds: 600,
                MaxCostAmount: 0,
                CostCurrency: "CNY",
                MaxRetries: 0,
                MaxArtifactCount: 1,
                MaxArtifactBytes: 1_048_576));
            var finalNode = await authorityContext.Set<AgentNodeRun>().SingleAsync(node =>
                node.Id == seeded.FinalNodeRunId);
            finalNode.BindTaskClaim(
                taskClaim.QueueItem.Id,
                taskClaim.TaskFencingToken,
                workerNow);
            await authorityContext.SaveChangesAsync();
        }

        AgentNodeRunClaim nodeClaim;
        using (var nodeClaimScope = host.Services.CreateScope())
        {
            var nodeRunStore = nodeClaimScope.ServiceProvider
                .GetRequiredService<IAgentNodeRunStore>();
            (await nodeRunStore.TryReleaseApprovalAsync(
                    seeded.FinalNodeRunId,
                    taskClaim.RunAttempt.Id,
                    taskClaim.TaskFencingToken,
                    workerNow))
                .Should()
                .Be(AgentFencedWriteResult.Succeeded);

            var claimStore = nodeClaimScope.ServiceProvider
                .GetRequiredService<IAgentNodeRunClaimStore>();
            var outcome = await claimStore.TryClaimAsync(
                seeded.FinalNodeRunId,
                taskClaim.RunAttempt.Id,
                taskClaim.TaskFencingToken,
                "final-output-expiry-test",
                TimeSpan.FromMinutes(5),
                workerNow);
            outcome.Code.Should().Be(AgentNodeRunClaimOutcomeCode.Claimed);
            nodeClaim = outcome.Claim!;
            (await claimStore.TryMarkRunningAsync(
                    nodeClaim,
                    workerNow.AddSeconds(1)))
                .Should()
                .Be(AgentFencedWriteResult.Succeeded);
        }

        using (var executionScope = host.Services.CreateScope())
        {
            var context = executionScope.ServiceProvider
                .GetRequiredService<AiGatewayDbContext>();
            var task = await context.AgentTasks
                .Include(item => item.Steps)
                .AsNoTracking()
                .SingleAsync(item => item.Id == seeded.TaskId);
            var workspace = await context.ArtifactWorkspaces
                .Include(item => item.Artifacts)
                .AsNoTracking()
                .SingleAsync(item => item.Id == seeded.WorkspaceId);
            var approval = await context.ApprovalRequests
                .AsNoTracking()
                .SingleAsync(item => item.Id == seeded.ApprovalId);
            var parentEvidence = await context.AgentEvidenceRecords
                .AsNoTracking()
                .Where(item =>
                    item.RunAttemptId == taskClaim.RunAttempt.Id &&
                    !item.IsRevoked)
                .OrderBy(item => item.NodeId)
                .ToArrayAsync();
            var finalStep = task.Steps.Single(step => step.Id == seeded.FinalStepId);
            var nodeContract = CreateFinalizationNodeContract(
                nodeClaim.NodeRun,
                parentEvidence);

            var result = await executionScope.ServiceProvider
                .GetRequiredService<AgentFinalizationNodeExecutor>()
                .ExecuteAsync(
                    taskClaim,
                    nodeClaim,
                    nodeContract,
                    workspace,
                    finalStep,
                    approval,
                    parentEvidence,
                    workerNow.AddSeconds(2),
                    CancellationToken.None);

            if (expireEvidenceAfterStaging)
            {
                result.IsSuccess.Should().BeFalse();
                result.Errors!
                    .OfType<ApiProblemDescriptor>()
                    .Should()
                    .ContainSingle(problem =>
                        problem.Code == AppProblemCodes.AgentNodeRunStateConflict);
            }
            else
            {
                result.IsSuccess.Should().BeTrue();
            }
        }

        if (expireEvidenceAfterStaging)
        {
            await InstallFailFirstFinalizationAuditTriggerAsync(database.ConnectionString);
            using var reconciliationScope = host.Services.CreateScope();
            var reconciliation = await reconciliationScope.ServiceProvider
                .GetRequiredService<NodeCheckpointCoordinator>()
                .CommitFinalizationConflictAsync(
                    taskClaim,
                    nodeClaim,
                    "Approved final-output Evidence expired before the authoritative checkpoint.",
                    DateTimeOffset.UtcNow,
                    CancellationToken.None);
            reconciliation.IsSuccess.Should().BeTrue();
        }

        if (!expireEvidenceAfterStaging)
        {
            using var completionScope = host.Services.CreateScope();
            var completed = await completionScope.ServiceProvider
                .GetRequiredService<IAgentDurableTaskClaimStore>()
                .TryCompleteAsync(
                    taskClaim,
                    AgentTaskRunQueueStatus.Succeeded,
                    failureCode: null,
                    "stale pre-checkpoint task status",
                    DateTimeOffset.UtcNow);
            completed.Should().Be(AgentFencedWriteResult.Succeeded);
        }

        await using var verification = CreateAiGatewayContext(database.ConnectionString);
        if (expireEvidenceAfterStaging)
        {
            fileSetStore.RollbackCalled.Should().BeTrue();
            fileSetStore.ConfirmCalled.Should().BeFalse();
            (await verification.AgentEvidenceRecords.AsNoTracking().SingleAsync(item =>
                    item.Id == seeded.EvidenceId))
                .ExpiresAt.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
            (await verification.AgentEvidenceRecords.CountAsync()).Should().Be(1);
            (await verification.ArtifactFileSetOperations.CountAsync()).Should().Be(0);
            (await verification.AgentTasks.AsNoTracking().SingleAsync(item =>
                    item.Id == seeded.TaskId))
                .Should()
                .Match<AgentTask>(task =>
                    task.Status == AgentTaskStatus.ReconciliationRequired &&
                    task.ActiveRunAttemptId == seeded.RunAttemptId);
            (await verification.ArtifactWorkspaces.AsNoTracking().SingleAsync(item =>
                    item.Id == seeded.WorkspaceId))
                .Status.Should().Be(ArtifactWorkspaceStatus.Active);
            var reconciliationNode = await verification.Set<AgentNodeRun>()
                .AsNoTracking()
                .SingleAsync(item => item.Id == seeded.FinalNodeRunId);
            reconciliationNode.Status.Should().Be(AgentNodeRunStatus.OutcomeUnknown);
            reconciliationNode.ReconciliationPolicy.Should()
                .Be("manual-final-output-authority-conflict-v1");
            (await verification.AgentTaskRunAttempts.AsNoTracking().SingleAsync(item =>
                    item.Id == seeded.RunAttemptId))
                .Status.Should().Be(AgentTaskRunAttemptStatus.ReconciliationRequired);
            (await verification.AgentTasks
                    .Include(item => item.Steps)
                    .AsNoTracking()
                    .SingleAsync(item => item.Id == seeded.TaskId))
                .Steps.Single(item => item.Id == seeded.FinalStepId)
                .Status.Should().Be(AgentStepStatus.Approved);
            (await verification.MessageEvents.CountAsync(messageEvent =>
                    messageEvent.AgentTaskId == seeded.TaskId &&
                    (messageEvent.EventType == MessageEventType.AgentTaskStepCompleted ||
                     messageEvent.EventType == MessageEventType.ArtifactReady ||
                     messageEvent.EventType == MessageEventType.FinalOutputReady)))
                .Should().Be(0);
            await using var reconciliationAudit = new AuditDbContext(
                PostgresPersistenceTestOptions.CreateAudit(database.ConnectionString));
            (await reconciliationAudit.AuditLogs.AsNoTracking().CountAsync(entry =>
                    entry.ActionCode == "Agent.FinalizationReconciliationRequired" &&
                    entry.TargetId == seeded.FinalNodeRunId.Value.ToString()))
                .Should().Be(1);
            return;
        }

        fileSetStore.ConfirmCalled.Should().BeTrue();
        fileSetStore.RollbackCalled.Should().BeFalse();
        (await verification.AgentEvidenceRecords.CountAsync()).Should().Be(2);
        (await verification.ArtifactFileSetOperations.CountAsync()).Should().Be(1);
        var completedTask = await verification.AgentTasks
            .Include(item => item.Steps)
            .AsNoTracking()
            .SingleAsync(item => item.Id == seeded.TaskId);
        completedTask.Status.Should().Be(AgentTaskStatus.Completed);
        completedTask.CompletedAt.Should().NotBeNull();
        completedTask.ActiveRunAttemptId.Should().BeNull();
        completedTask.Steps.Single(item => item.Id == seeded.FinalStepId)
            .Status.Should().Be(AgentStepStatus.Completed);
        (await verification.ArtifactWorkspaces
                .Include(item => item.Artifacts)
                .AsNoTracking()
                .SingleAsync(item => item.Id == seeded.WorkspaceId))
            .Should()
            .Match<ArtifactWorkspace>(workspace =>
                workspace.Status == ArtifactWorkspaceStatus.Finalized &&
                workspace.Artifacts.Count == 1 &&
                workspace.Artifacts.Single().Status == ArtifactStatus.Final &&
                workspace.Artifacts.Single().RelativePath.StartsWith(
                    "final/.committed/",
                    StringComparison.Ordinal));
        (await verification.AgentTaskRunAttempts.AsNoTracking().SingleAsync(item =>
                item.Id == seeded.RunAttemptId))
            .Status.Should().Be(AgentTaskRunAttemptStatus.Succeeded);
        (await verification.Set<AgentNodeRun>().AsNoTracking().SingleAsync(item =>
                item.Id == seeded.FinalNodeRunId))
            .Status.Should().Be(AgentNodeRunStatus.Succeeded);
        var persistedApproval = await verification.ApprovalRequests.AsNoTracking().SingleAsync(item =>
            item.Id == seeded.ApprovalId);
        persistedApproval.HasValidFinalOutputProof().Should().BeTrue();
        persistedApproval.HasValidFinalOutputDecisionProof().Should().BeTrue();
        var queue = await verification.AgentTaskRunQueueItems.AsNoTracking().SingleAsync(item =>
            item.SourceApprovalRequestId == seeded.ApprovalId);
        queue.Status.Should().Be(AgentTaskRunQueueStatus.Succeeded);
        queue.SafeMessage.Should().Be("Agent task run reached Completed.");
        var finalizationEvents = await verification.MessageEvents
            .AsNoTracking()
            .Where(messageEvent =>
                messageEvent.AgentTaskId == seeded.TaskId &&
                (messageEvent.EventType == MessageEventType.AgentTaskStepCompleted ||
                 messageEvent.EventType == MessageEventType.ArtifactReady ||
                 messageEvent.EventType == MessageEventType.FinalOutputReady))
            .OrderBy(messageEvent => messageEvent.Sequence)
            .ToArrayAsync();
        finalizationEvents.Select(messageEvent => messageEvent.EventType).Should().Equal(
            MessageEventType.AgentTaskStepCompleted,
            MessageEventType.ArtifactReady,
            MessageEventType.FinalOutputReady);
        finalizationEvents[0].AgentStepId.Should().Be(seeded.FinalStepId);
        finalizationEvents[1].ArtifactId.Should().Be(seeded.ArtifactId);
        finalizationEvents.Should().OnlyContain(messageEvent =>
            messageEvent.ArtifactWorkspaceId == seeded.WorkspaceId);

        await using var audit = new AuditDbContext(
            PostgresPersistenceTestOptions.CreateAudit(database.ConnectionString));
        (await audit.AuditLogs.AsNoTracking().CountAsync(entry =>
                entry.ActionCode == "Agent.ToolExecution" &&
                entry.TargetId == seeded.FinalStepId.Value.ToString()))
            .Should().Be(1);
        (await audit.AuditLogs.AsNoTracking().CountAsync(entry =>
                entry.ActionCode == "Agent.WorkspaceFinalize" &&
                entry.TargetId == seeded.WorkspaceId.Value.ToString()))
            .Should().Be(1);
    }

    private async Task<PostgresScratchDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_final_output_approval");
        try
        {
            await using var root = new AiCopilotDbContext(
                PostgresPersistenceTestOptions.Create<AiCopilotDbContext>(
                    database.ConnectionString,
                    MigrationHistoryTables.AiCopilot));
            await root.Database.MigrateAsync();
            await using var aiGateway = CreateAiGatewayContext(database.ConnectionString);
            await aiGateway.Database.MigrateAsync();
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    private static async Task InstallFailFirstFinalizationAuditTriggerAsync(
        string connectionString)
    {
        await using var context = CreateAiGatewayContext(connectionString);
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE SEQUENCE public.finalization_reconciliation_audit_retry_sequence;

            CREATE FUNCTION public.fail_first_finalization_reconciliation_audit()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $function$
            BEGIN
                IF nextval('public.finalization_reconciliation_audit_retry_sequence') = 1 THEN
                    RAISE EXCEPTION 'simulated transient finalization audit failure'
                        USING ERRCODE = '40001';
                END IF;
                RETURN NEW;
            END;
            $function$;

            CREATE TRIGGER fail_first_finalization_reconciliation_audit
            BEFORE INSERT ON public.audit_logs
            FOR EACH ROW
            WHEN (NEW.action_code = 'Agent.FinalizationReconciliationRequired')
            EXECUTE FUNCTION public.fail_first_finalization_reconciliation_audit();
            """);
    }

    private static async Task InstallRejectFinalizationAuditTriggerAsync(
        string connectionString)
    {
        await using var context = CreateAiGatewayContext(connectionString);
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE FUNCTION public.reject_runtime_finalization_reconciliation_audit()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $function$
            BEGIN
                RAISE EXCEPTION 'simulated non-transient runtime reconciliation audit failure'
                    USING ERRCODE = '23514';
            END;
            $function$;

            CREATE TRIGGER reject_runtime_finalization_reconciliation_audit
            BEFORE INSERT ON public.audit_logs
            FOR EACH ROW
            WHEN (NEW.action_code = 'Agent.FinalizationReconciliationRequired')
            EXECUTE FUNCTION public.reject_runtime_finalization_reconciliation_audit();
            """);
    }

    private static async Task<SeededFinalOutput> SeedAndPrepareAsync(
        string connectionString,
        ToolRegistryGovernanceTestBase.InMemoryArtifactWorkspaceFileStore fileStore)
    {
        var seeded = await SeedPreApprovalAsync(connectionString, fileStore);
        using var host = CreateStoreHost(connectionString, fileStore);
        using var scope = host.Services.CreateScope();
        var prepared = await scope.ServiceProvider
            .GetRequiredService<IFinalOutputApprovalStore>()
            .PrepareAsync(new FinalOutputApprovalPreparation(
                seeded.TaskId,
                seeded.UserId,
                seeded.Proof,
                DateTimeOffset.UtcNow));
        prepared.Status.Should().Be(FinalOutputApprovalCommandStatus.Created);
        return seeded with { ApprovalId = prepared.Approval!.Id };
    }

    private static async Task<SeededFinalOutput> SeedPreApprovalAsync(
        string connectionString,
        ToolRegistryGovernanceTestBase.InMemoryArtifactWorkspaceFileStore fileStore)
    {
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        var session = new Session(userId, ConversationTemplateId.New());
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
            session.Id,
            userId,
            "Final-output transaction",
            "Final-output transaction",
            AgentTaskType.ReportGeneration,
            AgentTaskRiskLevel.Low,
            null,
            planJson,
            now);
        var steps = AgentPlanV2TestData.AddTrackedPlanSteps(task, planJson, now);
        var producerStep = steps.Single(step =>
            string.Equals(step.ToolCode, "generate_markdown_report", StringComparison.Ordinal));
        var finalStep = steps.Single(step =>
            string.Equals(step.ToolCode, "finalize_artifacts", StringComparison.Ordinal));
        var workspace = new ArtifactWorkspace(
            task.Id,
            $"ws_{Guid.NewGuid():N}",
            "/tmp/final-output-persistence",
            "/workspaces/final-output-persistence",
            now);
        task.AttachWorkspace(workspace.Id, now);
        task.ConfirmExecutablePlan(
            task.PlanJson,
            steps.Where(step => step.RequiresApproval).Select(step => step.StepIndex).ToArray(),
            now);
        task.ApprovePlan(now);

        var sourceBytes = Encoding.UTF8.GetBytes("# final output");
        producerStep.Start(now.AddSeconds(1));
        var artifact = workspace.AddDraftArtifact(
            ArtifactType.Markdown,
            "report.md",
            "draft/report.md",
            sourceBytes.LongLength,
            "text/markdown",
            producerStep.Id,
            now.AddSeconds(2));
        producerStep.Complete(
            $$"""{"artifactId":"{{artifact.Id.Value:D}}","artifactType":"markdown","resultType":"artifact","status":"completed"}""",
            now.AddSeconds(3));
        var authority = await FinalOutputApprovalTestData.CreatePreApprovalAuthorityAsync(
            task,
            workspace,
            new Dictionary<Guid, byte[]>
            {
                [artifact.Id.Value] = sourceBytes
            },
            now.AddSeconds(4),
            completeOriginalQueue: false);
        fileStore.AddFile(
            workspace.WorkspaceCode,
            artifact.RelativePath,
            sourceBytes,
            artifact.MimeType);

        await using var dbContext = CreateAiGatewayContext(connectionString);
        dbContext.Sessions.Add(session);
        dbContext.AgentTasks.Add(task);
        dbContext.ArtifactWorkspaces.Add(workspace);
        dbContext.AgentTaskRunAttempts.Add(authority.RunAttempt);
        dbContext.AgentTaskRunQueueItems.Add(authority.OriginalQueueItem);
        dbContext.Set<AgentNodeRun>().AddRange(authority.NodeRuns);
        dbContext.Set<AgentEvidenceRecord>().AddRange(authority.Evidence);
        await dbContext.SaveChangesAsync();
        return new SeededFinalOutput(
            task.Id,
            userId,
            workspace.Id,
            workspace.WorkspaceCode,
            finalStep.Id,
            authority.RunAttempt.Id,
            authority.NodeRuns.Single(node => node.RequiresApproval).Id,
            authority.Evidence.Single().Id,
            authority.Evidence.Single().EnvelopeDigest,
            artifact.Id,
            artifact.RelativePath,
            artifact.MimeType,
            sourceBytes,
            authority.Proof,
            ApprovalId: default);
    }

    private static async Task<AgentTaskId> SeedQueuedTaskAsync(
        string connectionString,
        Guid userId)
    {
        var now = DateTimeOffset.UtcNow;
        var session = new Session(userId, ConversationTemplateId.New());
        var planJson = AgentPlanV2TestData.Create(
            [
                new AgentPlanV2TestStep(
                    "Generate governed report",
                    "Generate a governed report artifact.",
                    AgentStepType.ArtifactGeneration,
                    "generate_markdown_report")
            ],
            executable: true,
            taskType: AgentTaskType.ReportGeneration,
            knowledgeBaseIds: null);
        var task = new AgentTask(
            session.Id,
            userId,
            "Later durable task",
            "Prove a poison approval queue cannot block later tasks.",
            AgentTaskType.ReportGeneration,
            AgentTaskRiskLevel.Low,
            null,
            planJson,
            now);
        var steps = AgentPlanV2TestData.AddTrackedPlanSteps(task, planJson, now);
        task.ConfirmExecutablePlan(
            task.PlanJson,
            steps.Where(step => step.RequiresApproval)
                .Select(step => step.StepIndex)
                .ToArray(),
            now);
        task.ApprovePlan(now);
        task.MarkQueued(now);
        var queue = new AgentTaskRunQueueItem(
            task.Id,
            AgentTaskRunTriggerType.Manual,
            userId,
            now);

        await using var context = CreateAiGatewayContext(connectionString);
        context.Sessions.Add(session);
        context.AgentTasks.Add(task);
        context.AgentTaskRunQueueItems.Add(queue);
        await context.SaveChangesAsync();
        return task.Id;
    }

    private static IHost CreateStoreHost(
        string connectionString,
        ToolRegistryGovernanceTestBase.InMemoryArtifactWorkspaceFileStore fileStore,
        IArtifactWorkspaceFileSetStore? fileSetStore = null)
    {
        Environment.SetEnvironmentVariable(
            "AICopilotSecurity__ApiKeyEncryptionKey",
            Environment.GetEnvironmentVariable("AICopilotSecurity__ApiKeyEncryptionKey")
            ?? "aicopilot-persistence-final-output-test-key");
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:ai-copilot"] = connectionString;
        builder.AddEfCore();
        builder.AddAiGatewayService();
        builder.Services.AddSingleton<IArtifactWorkspaceFileStore>(fileStore);
        builder.Services.AddFinalOutputApprovalStore();
        if (fileSetStore is not null)
        {
            builder.Services.AddSingleton(fileSetStore);
        }

        return builder.Build();
    }

    private static AgentTaskRunQueueWorkerCoordinator CreateFinalizationConflictWorker(
        IServiceProvider services)
    {
        return new AgentTaskRunQueueWorkerCoordinator(
            services.GetRequiredService<IAgentTaskRunQueueStore>(),
            services.GetRequiredService<IRepository<AgentTask>>(),
            services.GetRequiredService<IAgentTaskRunAttemptStore>(),
            services.GetRequiredService<IAgentTaskRunQueue>(),
            new FinalizationConflictRuntime(),
            durableTaskClaimCoordinator:
                services.GetRequiredService<DurableTaskClaimCoordinator>());
    }

    private static AgentPlanNodeDocument CreateFinalizationNodeContract(
        AgentNodeRun finalNode,
        IReadOnlyCollection<AgentEvidenceRecord> parentEvidence)
    {
        var dependencies = parentEvidence
            .Select(item => item.NodeId)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return new AgentPlanNodeDocument(
            AgentPlanContractVersions.NodeV1,
            finalNode.NodeId,
            finalNode.NodeKind,
            dependencies,
            Required: true,
            "node-input:v1",
            finalNode.OutputSchemaRef,
            [BuiltInToolRegistrations.FinalizationCheckpointToolCode],
            ["General.Chat"],
            [],
            [],
            dependencies,
            Input: null,
            ModelPolicy: null,
            new AgentPlanTimeoutPolicyDocument(
                "timeout-policy:v1",
                finalNode.TimeoutSeconds),
            new AgentPlanRetryPolicyDocument(
                "retry-policy:v1",
                finalNode.MaxAttempts,
                "None"),
            new AgentPlanNodeBudgetDocument(
                finalNode.MaxToolCalls,
                finalNode.MaxModelCalls,
                finalNode.MaxInputTokens,
                finalNode.MaxOutputTokens,
                MaxRows: 0,
                finalNode.MaxCostAmount,
                finalNode.MaxArtifactCount,
                finalNode.MaxArtifactBytes),
            new AgentPlanApprovalPolicyDocument(
                Required: true,
                "FinalOutput"),
            new AgentPlanIdempotencyPolicyDocument(
                "idempotency-policy:v1",
                "Fenced"),
            "ArtifactDraftOnly",
            finalNode.JoinPolicy);
    }

    private static AiGatewayDbContext CreateAiGatewayContext(string connectionString) =>
        new(PostgresPersistenceTestOptions.Create<AiGatewayDbContext>(
            connectionString,
            MigrationHistoryTables.AiGateway));

    private static async Task ApplyDriftAsync(
        string connectionString,
        ToolRegistryGovernanceTestBase.InMemoryArtifactWorkspaceFileStore fileStore,
        SeededFinalOutput seeded,
        string drift,
        bool apply)
    {
        if (drift == "source-file")
        {
            fileStore.AddFile(
                seeded.WorkspaceCode,
                seeded.ArtifactRelativePath,
                apply ? Encoding.UTF8.GetBytes("# FINAL OUTPUT") : seeded.SourceBytes,
                seeded.ArtifactMimeType);
            return;
        }

        await using var dbContext = CreateAiGatewayContext(connectionString);
        switch (drift)
        {
            case "workspace":
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     UPDATE aigateway.artifact_workspaces
                     SET workspace_code = {(apply ? "ws_drifted" : seeded.WorkspaceCode)}
                     WHERE id = {seeded.WorkspaceId.Value}
                     """);
                break;
            case "final-step":
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     UPDATE aigateway.agent_steps
                     SET status = {(apply ? "Approved" : "WaitingApproval")}
                     WHERE id = {seeded.FinalStepId.Value}
                     """);
                break;
            case "attempt":
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     UPDATE aigateway.agent_task_run_attempts
                     SET status = {(apply ? "Running" : "WaitingApproval")}
                     WHERE id = {seeded.RunAttemptId.Value}
                     """);
                break;
            case "final-node":
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     UPDATE aigateway.agent_node_runs
                     SET status = {(apply ? "Cancelled" : "WaitingApproval")}
                     WHERE id = {seeded.FinalNodeRunId.Value}
                     """);
                break;
            case "task-fence":
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     UPDATE aigateway.agent_tasks
                     SET run_fencing_token = {(apply
                         ? seeded.Proof.TaskFencingToken + 1
                         : seeded.Proof.TaskFencingToken)}
                     WHERE id = {seeded.TaskId.Value}
                     """);
                break;
            case "node-fence":
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     UPDATE aigateway.agent_node_runs
                     SET node_fencing_token = {(apply ? 1L : 0L)}
                     WHERE id = {seeded.FinalNodeRunId.Value}
                     """);
                break;
            case "evidence":
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     UPDATE aigateway.agent_evidence_records
                     SET envelope_digest = {(apply
                         ? new string('c', 64)
                         : seeded.EvidenceEnvelopeDigest)}
                     WHERE id = {seeded.EvidenceId.Value}
                     """);
                break;
            case "evidence-expiry":
            {
                DateTimeOffset? expiresAt = apply
                    ? DateTimeOffset.UtcNow.AddMinutes(-1)
                    : null;
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     UPDATE aigateway.agent_evidence_records
                     SET expires_at = {expiresAt}
                     WHERE id = {seeded.EvidenceId.Value}
                     """);
                break;
            }
            case "artifact-metadata":
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     UPDATE aigateway.artifacts
                     SET version = {(apply ? 2 : 1)}
                     WHERE id = {seeded.ArtifactId.Value}
                     """);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(drift));
        }
    }

    private static async Task CorruptClaimedSourceApprovalAsync(
        string connectionString,
        ApprovalRequestId approvalRequestId,
        string corruption)
    {
        await using var context = CreateAiGatewayContext(connectionString);
        switch (corruption)
        {
            case "missing":
                await context.Database.ExecuteSqlRawAsync(
                    """
                    ALTER TABLE aigateway.agent_task_run_queue_items
                    DROP CONSTRAINT fk_agent_task_run_queue_items_source_approval
                    """);
                (await context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     DELETE FROM aigateway.approval_requests
                     WHERE id = {approvalRequestId.Value}
                     """))
                    .Should()
                    .Be(1);
                break;
            case "retargeted":
                (await context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     UPDATE aigateway.approval_requests
                     SET task_id = {Guid.NewGuid()}
                     WHERE id = {approvalRequestId.Value}
                     """))
                    .Should()
                    .Be(1);
                break;
            case "non-final-output":
                await context.Database.ExecuteSqlRawAsync(
                    """
                    ALTER TABLE aigateway.approval_requests
                    DROP CONSTRAINT ck_approval_requests_final_output_proof_shape
                    """);
                (await context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     UPDATE aigateway.approval_requests
                     SET approval_type = 'Artifact'
                     WHERE id = {approvalRequestId.Value}
                     """))
                    .Should()
                    .Be(1);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(corruption));
        }
    }

    private sealed record SeededFinalOutput(
        AgentTaskId TaskId,
        Guid UserId,
        ArtifactWorkspaceId WorkspaceId,
        string WorkspaceCode,
        AgentStepId FinalStepId,
        AgentTaskRunAttemptId RunAttemptId,
        AgentNodeRunId FinalNodeRunId,
        AgentEvidenceRecordId EvidenceId,
        string EvidenceEnvelopeDigest,
        ArtifactId ArtifactId,
        string ArtifactRelativePath,
        string ArtifactMimeType,
        byte[] SourceBytes,
        FinalOutputApprovalProof Proof,
        ApprovalRequestId ApprovalId);

    private sealed class FinalizationConflictRuntime : IAgentTaskRuntime
    {
        public Task<Result<AgentTask>> RunAsync(
            AgentTask task,
            CancellationToken cancellationToken = default) =>
            RunAsync(task, AgentTaskRunTriggerType.ApprovalResume, cancellationToken);

        public Task<Result<AgentTask>> RunAsync(
            AgentTask task,
            AgentTaskRunTriggerType triggerType = AgentTaskRunTriggerType.Manual,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Result<AgentTask>>(Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentFinalizationStateConflict,
                "Approved final-output source bytes drifted before NodeRun claim.")));
    }

    private sealed class ExpiringArtifactFileSetStore(
        string connectionString,
        AgentEvidenceRecordId evidenceId,
        bool expireEvidenceOnStage)
        : IArtifactWorkspaceFileSetStore
    {
        public bool ConfirmCalled { get; private set; }

        public bool RollbackCalled { get; private set; }

        public async Task<ArtifactFileSetStage> StageAsync(
            string workspaceCode,
            string operationKind,
            string publishArea,
            IReadOnlyCollection<ArtifactFileSetWriteRequest> files,
            CancellationToken cancellationToken = default,
            ArtifactFileSetAuthority? authority = null)
        {
            authority.Should().NotBeNull();
            var commitId = Guid.NewGuid();
            var publishedReference = $"final/.committed/{commitId:N}";
            var published = files
                .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
                .Select(file => new ArtifactFileSetPublishedFile(
                    $"{publishedReference}/{ArtifactPathGuard.NormalizeRelativePath(file.RelativePath)}",
                    file.Content.LongLength,
                    file.MimeType,
                    Convert.ToHexString(
                            System.Security.Cryptography.SHA256.HashData(file.Content))
                        .ToLowerInvariant()))
                .ToArray();
            var manifestJson = CanonicalJson.Serialize(new
            {
                version = "final-output-expiry-test:v1",
                commitId,
                workspaceCode,
                operationKind,
                publishedReference,
                files = published
            });

            if (expireEvidenceOnStage)
            {
                await using var context = CreateAiGatewayContext(connectionString);
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     UPDATE aigateway.agent_evidence_records
                     SET expires_at = {DateTimeOffset.UtcNow.AddMinutes(-1)}
                     WHERE id = {evidenceId.Value}
                     """,
                    cancellationToken);
            }

            return new ArtifactFileSetStage(
                commitId,
                workspaceCode,
                operationKind,
                $"staging:{commitId:N}",
                publishedReference,
                manifestJson,
                CanonicalJson.ComputeSha256(manifestJson),
                published,
                DateTimeOffset.UtcNow,
                authority!);
        }

        public Task ConfirmBestEffortAsync(
            ArtifactFileSetStage stage,
            CancellationToken cancellationToken = default)
        {
            ConfirmCalled = true;
            return Task.CompletedTask;
        }

        public Task RollbackBestEffortAsync(
            ArtifactFileSetStage stage,
            CancellationToken cancellationToken = default)
        {
            RollbackCalled = true;
            return Task.CompletedTask;
        }

        public Task LeavePendingAsync(
            ArtifactFileSetStage stage,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> VerifyPublishedAsync(
            ArtifactFileSetStage stage,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<ArtifactFileSetPendingSnapshot> GetPendingAsync(
            int maximumEntries,
            DateTimeOffset createdBeforeUtc,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ArtifactFileSetPendingSnapshot([], false));

        public Task ConfirmPendingAsync(
            ArtifactFileSetStage stage,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RollbackPendingAsync(
            ArtifactFileSetStage stage,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task MarkPendingAttemptedAsync(
            Guid commitId,
            DateTimeOffset attemptedAtUtc,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> ExistsPendingAsync(
            Guid commitId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
