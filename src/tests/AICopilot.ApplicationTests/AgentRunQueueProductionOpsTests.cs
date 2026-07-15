using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Domain;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using AICopilot.SharedKernel.Specification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AICopilot.ApplicationTests;

public sealed class AgentRunQueueProductionOpsTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Fact]
    public void AgentRunQueueOptions_ShouldExposeDefaultBackoffPolicy()
    {
        var options = new AgentRunQueueOptions();

        options.LeaseDuration.Should().Be(TimeSpan.FromMinutes(5));
        options.HeartbeatActiveWindow.Should().Be(TimeSpan.FromSeconds(30));
        options.EffectiveMaxRetryAttempts.Should().Be(3);
        options.GetRetryBackoff(1).Should().Be(TimeSpan.FromSeconds(30));
        options.GetRetryBackoff(2).Should().Be(TimeSpan.FromSeconds(60));
        options.GetRetryBackoff(3).Should().Be(TimeSpan.FromSeconds(120));
    }

    [Fact]
    public void AgentRunQueueSummary_ShouldCalculateProductionMetrics()
    {
        var task = CreateFailedTask();
        var now = DateTimeOffset.UtcNow;
        var oldest = now.AddMinutes(-10);
        var queued = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Manual, task.UserId, now, oldest);
        var succeeded = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Manual, task.UserId, now.AddMinutes(-6), now.AddMinutes(-5));
        succeeded.AcquireLease(Guid.NewGuid(), "worker-1", now.AddMinutes(-4), TimeSpan.FromMinutes(5));
        succeeded.MarkStarted(null, now.AddMinutes(-4));
        succeeded.MarkSucceeded(now.AddMinutes(-2), "done");
        var leased = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Retry, task.UserId, now);
        leased.AcquireLease(Guid.NewGuid(), "worker-1", now, TimeSpan.FromMinutes(5));
        var stale = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Retry, task.UserId, now.AddMinutes(-10));
        stale.AcquireLease(Guid.NewGuid(), "worker-2", now.AddMinutes(-10), TimeSpan.FromSeconds(1));
        var apiWorker = new AgentWorkerHeartbeat("worker-api", "data-worker", now, "api-hash", "1.0.0");
        var mismatchWorker = new AgentWorkerHeartbeat("worker-mismatch", "data-worker", now, "worker-hash", "1.0.0");

        var summary = GetAgentRunQueueSummaryQueryHandler.BuildSummary(
            [queued, succeeded, leased, stale],
            [apiWorker, mismatchWorker],
            now,
            "api-hash",
            TimeSpan.FromSeconds(30));

        summary.QueuedCount.Should().Be(1);
        summary.LeasedCount.Should().Be(2);
        summary.SucceededCount.Should().Be(1);
        summary.StaleLeasedCount.Should().Be(1);
        summary.OldestQueuedAt.Should().Be(oldest);
        summary.OldestQueuedWaitMs.Should().Be(600000);
        summary.AverageWaitMs.Should().Be(60000);
        summary.AverageRunMs.Should().Be(120000);
        summary.ActiveWorkerCount.Should().Be(2);
        summary.WorkspaceMismatchCount.Should().Be(1);
    }

    [Fact]
    public void AgentWorkerStatus_ShouldTreatExpiredHeartbeatAsUnavailable()
    {
        var task = CreateFailedTask();
        var now = DateTimeOffset.UtcNow;
        var heartbeat = new AgentWorkerHeartbeat("worker-1", "data-worker", now.AddMinutes(-5), "api-hash", "1.0.0");

        var status = AgentWorkerStatusCalculator.Build(
            [new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Manual, task.UserId, now)],
            [heartbeat],
            "api-hash",
            now,
            TimeSpan.FromSeconds(30));

        status.HasActiveWorkers.Should().BeFalse();
        status.StatusCode.Should().Be(AppProblemCodes.AgentWorkerUnavailable);
    }

    [Fact]
    public async Task RetryAgentTaskCommand_ShouldApplyBackoffLimitAndAudit()
    {
        var task = CreateFailedTask();
        var previousRetry1 = FailedRetryItem(task, DateTimeOffset.UtcNow.AddMinutes(-6));
        var previousRetry2 = FailedRetryItem(task, DateTimeOffset.UtcNow.AddMinutes(-4));
        var queueRepository = new InMemoryAgentTaskRunQueueStore(previousRetry1, previousRetry2);
        var taskRepository = new InMemoryRepository<AgentTask>(task);
        var workspaceRepository = new InMemoryRepository<ArtifactWorkspace>();
        var approvalRepository = new InMemoryRepository<ApprovalRequest>();
        var audit = new CapturingAuditLogWriter();
        var handler = new RetryAgentTaskCommandHandler(
            taskRepository,
            CreateAgentTaskDtoQueryService(workspaceRepository, approvalRepository, queueRepository),
            CreateLifecycleCoordinator(
                taskRepository,
                workspaceRepository,
                approvalRepository,
                queueRepository,
                options: Options.Create(new AgentRunQueueOptions()),
                auditRecorder: new AgentAuditRecorder(audit)),
            new TestCurrentUser(UserId));

        var before = DateTimeOffset.UtcNow;
        var result = await handler.Handle(new RetryAgentTaskCommand(task.Id.Value), CancellationToken.None);
        var after = DateTimeOffset.UtcNow;

        result.IsSuccess.Should().BeTrue();
        var queued = queueRepository.Items.Single(item => item.Status == AgentTaskRunQueueStatus.Queued);
        queued.AvailableAt.Should().BeOnOrAfter(before.AddSeconds(120));
        queued.AvailableAt.Should().BeOnOrBefore(after.AddSeconds(121));
        audit.Requests.Should().ContainSingle(request => request.ActionCode == "Agent.RunQueueRetry")
            .Which.Metadata!["retryAttemptNo"].Should().Be("3");

        var maxedTask = CreateFailedTask();
        var maxedQueue = new InMemoryAgentTaskRunQueueStore(
            FailedRetryItem(maxedTask, DateTimeOffset.UtcNow.AddMinutes(-9)),
            FailedRetryItem(maxedTask, DateTimeOffset.UtcNow.AddMinutes(-6)),
            FailedRetryItem(maxedTask, DateTimeOffset.UtcNow.AddMinutes(-3)));
        var maxedTaskRepository = new InMemoryRepository<AgentTask>(maxedTask);
        var maxedWorkspaceRepository = new InMemoryRepository<ArtifactWorkspace>();
        var maxedApprovalRepository = new InMemoryRepository<ApprovalRequest>();
        var maxedHandler = new RetryAgentTaskCommandHandler(
            maxedTaskRepository,
            CreateAgentTaskDtoQueryService(maxedWorkspaceRepository, maxedApprovalRepository, maxedQueue),
            CreateLifecycleCoordinator(
                maxedTaskRepository,
                maxedWorkspaceRepository,
                maxedApprovalRepository,
                maxedQueue,
                options: Options.Create(new AgentRunQueueOptions())),
            new TestCurrentUser(UserId));

        var maxed = await maxedHandler.Handle(new RetryAgentTaskCommand(maxedTask.Id.Value), CancellationToken.None);

        maxed.IsSuccess.Should().BeFalse();
        maxed.Errors!.OfType<ApiProblemDescriptor>().Single().Code
            .Should().Be(AppProblemCodes.AgentTaskRetryNotAllowed);
        maxed.Errors!.OfType<ApiProblemDescriptor>().Single().Detail
            .Should().Contain("retry limit exceeded");
    }

    [Fact]
    public async Task CancelAgentTaskCommand_ShouldKeepTerminalTaskUnchanged()
    {
        var task = CreateFailedTask();
        var queueItem = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Manual, task.UserId, DateTimeOffset.UtcNow);
        queueItem.MarkFailed("failed", "failed", DateTimeOffset.UtcNow);
        var queueRepository = new InMemoryAgentTaskRunQueueStore(queueItem);
        var taskRepository = new InMemoryRepository<AgentTask>(task);
        var workspaceRepository = new InMemoryRepository<ArtifactWorkspace>();
        var approvalRepository = new InMemoryRepository<ApprovalRequest>();
        var attemptRepository = new InMemoryAgentTaskRunAttemptStore();
        var handler = new CancelAgentTaskCommandHandler(
            taskRepository,
            CreateAgentTaskDtoQueryService(workspaceRepository, approvalRepository, queueRepository),
            CreateLifecycleCoordinator(
                taskRepository,
                workspaceRepository,
                approvalRepository,
                queueRepository,
                attemptRepository),
            new TestCurrentUser(UserId));

        var result = await handler.Handle(new CancelAgentTaskCommand(task.Id.Value), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        task.Status.Should().Be(AgentTaskStatus.Failed);
        queueItem.Status.Should().Be(AgentTaskRunQueueStatus.Failed);
    }

    [Fact]
    public void AgentTaskRunAttempt_ShouldRejectRepeatedTerminalCompletion()
    {
        var task = CreateFailedTask();
        var attempt = new AgentTaskRunAttempt(
            task.Id,
            1,
            AgentTaskRunTriggerType.Manual,
            "worker",
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(5));
        attempt.MarkSucceeded(DateTimeOffset.UtcNow, "done");

        var act = () => attempt.MarkFailed("failed", "failed again", DateTimeOffset.UtcNow);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot be completed again*");
    }

    [Fact]
    public async Task DataWorker_ShouldFailStaleStartedLeaseAndAudit()
    {
        var task = CreateFailedTask();
        task.PrepareRetry(DateTimeOffset.UtcNow);
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
        var queueRepository = new InMemoryAgentTaskRunQueueStore(queueItem);
        var audit = new CapturingAuditLogWriter();
        using var provider = CreateQueueWorkerProvider(
            new InMemoryRepository<AgentTask>(task),
            queueRepository,
            new InMemoryAgentTaskRunAttemptStore(attempt),
            new ThrowingRuntime(),
            audit);
        var worker = new AgentTaskRunQueueWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentTaskRunQueueWorker>.Instance,
            Options.Create(new AgentRunQueueOptions()));

        var processed = await worker.ProcessOnceAsync(CancellationToken.None);

        processed.Should().BeFalse();
        queueItem.Status.Should().Be(AgentTaskRunQueueStatus.Failed);
        queueItem.FailureCode.Should().Be(AppProblemCodes.AgentTaskRunQueueLeaseExpired);
        audit.Requests.Should().ContainSingle(request => request.ActionCode == "Agent.RunQueueStaleLeaseFailed")
            .Which.Metadata!["oldStatus"].Should().Be(AgentTaskRunQueueStatus.Leased.ToString());
    }

    private static AgentTask CreateFailedTask()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new AgentTask(
            new SessionId(Guid.NewGuid()),
            UserId,
            "Run queue test",
            "Generate run queue test artifact",
            AgentTaskType.ReportGeneration,
            AgentTaskRiskLevel.Low,
            null,
            """{"steps":[]}""",
            now);
        var step = task.AddStep(
            "Generate",
            "Generate output",
            AgentStepType.ArtifactGeneration,
            "generate_chart_data",
            requiresApproval: false,
            now);
        task.ConfirmExecutablePlan(task.PlanJson, Array.Empty<int>(), now);
        task.ApprovePlan(now);
        task.Start(now);
        step.Start(now);
        step.Fail("failed", now);
        task.Fail("failed", now);
        return task;
    }

    private static AgentTaskRunQueueItem FailedRetryItem(AgentTask task, DateTimeOffset now)
    {
        var item = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Retry, task.UserId, now);
        item.MarkFailed("failed", "failed", now.AddSeconds(1));
        return item;
    }

    private static ServiceProvider CreateQueueWorkerProvider(
        InMemoryRepository<AgentTask> taskRepository,
        InMemoryAgentTaskRunQueueStore queueRepository,
        InMemoryAgentTaskRunAttemptStore attemptRepository,
        IAgentTaskRuntime runtime,
        IAuditLogWriter auditLogWriter)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRepository<AgentTask>>(taskRepository);
        services.AddSingleton<IAgentTaskRunQueueStore>(queueRepository);
        services.AddSingleton<IAgentTaskRunAttemptStore>(attemptRepository);
        services.AddSingleton<IAgentTaskRunQueue>(new AgentTaskRunQueue(queueRepository));
        services.AddSingleton(runtime);
        services.AddSingleton(auditLogWriter);
        services.AddSingleton<AgentAuditRecorder>();
        services.AddSingleton<AgentTaskRunQueueWorkerCoordinator>();
        return services.BuildServiceProvider();
    }

    private static AgentTaskLifecycleCoordinator CreateLifecycleCoordinator(
        InMemoryRepository<AgentTask> taskRepository,
        InMemoryRepository<ArtifactWorkspace> workspaceRepository,
        InMemoryRepository<ApprovalRequest> approvalRepository,
        InMemoryAgentTaskRunQueueStore queueRepository,
        InMemoryAgentTaskRunAttemptStore? attemptRepository = null,
        IOptions<AgentRunQueueOptions>? options = null,
        AgentAuditRecorder? auditRecorder = null)
    {
        return new AgentTaskLifecycleCoordinator(
            taskRepository,
            approvalRepository,
            workspaceRepository,
            queueRepository,
            attemptRepository ?? new InMemoryAgentTaskRunAttemptStore(),
            new AgentTaskRunQueue(queueRepository),
            options,
            auditRecorder);
    }

    private static AgentTaskDtoQueryService CreateAgentTaskDtoQueryService(
        IReadRepository<ArtifactWorkspace> workspaceRepository,
        IReadRepository<ApprovalRequest> approvalRepository,
        IAgentTaskRunQueueStore queueRepository)
    {
        return new AgentTaskDtoQueryService(
            workspaceRepository,
            approvalRepository,
            queueRepository);
    }

    private sealed class ThrowingRuntime : IAgentTaskRuntime
    {
        public Task<Result<AgentTask>> RunAsync(AgentTask task, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Runtime should not be called by this test.");
        }

        public Task<Result<AgentTask>> RunAsync(
            AgentTask task,
            AgentTaskRunTriggerType triggerType = AgentTaskRunTriggerType.Manual,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Runtime should not be called by this test.");
        }
    }

    private sealed class CapturingAuditLogWriter : IAuditLogWriter
    {
        public List<AuditLogWriteRequest> Requests { get; } = [];

        public Task WriteAsync(AuditLogWriteRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1);
        }
    }

    private sealed class InMemoryAgentTaskRunQueueStore(params AgentTaskRunQueueItem[] initialItems)
        : IAgentTaskRunQueueStore
    {
        public List<AgentTaskRunQueueItem> Items { get; } = [..initialItems];

        public Task<AgentTaskRunQueueItem?> FirstActiveByTaskAsync(
            AgentTaskId taskId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items
                .Where(item => item.TaskId == taskId && IsActive(item))
                .OrderByDescending(item => item.CreatedAt)
                .FirstOrDefault());
        }

        public Task<AgentTaskRunQueueItem?> FirstByIdAsync(
            AgentTaskRunQueueItemId id,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items.FirstOrDefault(item => item.Id == id));
        }

        public Task<List<AgentTaskRunQueueItem>> ListActiveByTaskAsync(
            AgentTaskId taskId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items
                .Where(item => item.TaskId == taskId && IsActive(item))
                .OrderByDescending(item => item.CreatedAt)
                .ToList());
        }

        public Task<List<AgentTaskRunQueueItem>> ListActiveAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items
                .Where(IsActive)
                .OrderBy(item => item.AvailableAt)
                .ToList());
        }

        public Task<List<AgentTaskRunQueueItem>> ListAllAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items.OrderByDescending(item => item.CreatedAt).ToList());
        }

        public Task<List<AgentTaskRunQueueItem>> ListByTaskAsync(
            AgentTaskId taskId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items
                .Where(item => item.TaskId == taskId)
                .OrderByDescending(item => item.CreatedAt)
                .ToList());
        }

        public AgentTaskRunQueueItem Add(AgentTaskRunQueueItem item)
        {
            Items.Add(item);
            return item;
        }

        public void Update(AgentTaskRunQueueItem item)
        {
            if (!Items.Contains(item))
            {
                Items.Add(item);
            }
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1);
        }

        private static bool IsActive(AgentTaskRunQueueItem item)
        {
            return item.Status is AgentTaskRunQueueStatus.Queued or AgentTaskRunQueueStatus.Leased;
        }
    }

    private sealed class InMemoryAgentTaskRunAttemptStore(params AgentTaskRunAttempt[] initialItems)
        : IAgentTaskRunAttemptStore
    {
        public List<AgentTaskRunAttempt> Items { get; } = [..initialItems];

        public Task<AgentTaskRunAttempt?> FirstByIdAsync(
            AgentTaskRunAttemptId id,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items.FirstOrDefault(attempt => attempt.Id == id));
        }

        public Task<List<AgentTaskRunAttempt>> ListByTaskAsync(
            AgentTaskId taskId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items
                .Where(attempt => attempt.TaskId == taskId)
                .OrderByDescending(attempt => attempt.StartedAt)
                .ToList());
        }

        public AgentTaskRunAttempt Add(AgentTaskRunAttempt attempt)
        {
            Items.Add(attempt);
            return attempt;
        }

        public void Update(AgentTaskRunAttempt attempt)
        {
            if (!Items.Contains(attempt))
            {
                Items.Add(attempt);
            }
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1);
        }
    }

    private sealed class InMemoryRepository<T>(params T[] initialItems) : IRepository<T>
        where T : class, IEntity, IAggregateRoot
    {
        public List<T> Items { get; } = [..initialItems];

        public T Add(T entity)
        {
            Items.Add(entity);
            return entity;
        }

        public void Update(T entity)
        {
        }

        public void Delete(T entity)
        {
            Items.Remove(entity);
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1);
        }

        public Task<List<T>> ListAsync(ISpecification<T>? specification = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(TestSpecificationEvaluator.Apply(Items.AsQueryable(), specification).ToList());
        }

        public Task<T?> FirstOrDefaultAsync(ISpecification<T>? specification = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(TestSpecificationEvaluator.Apply(Items.AsQueryable(), specification).FirstOrDefault());
        }

        public Task<int> CountAsync(ISpecification<T>? specification = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(TestSpecificationEvaluator.Apply(Items.AsQueryable(), specification).Count());
        }

        public Task<bool> AnyAsync(ISpecification<T>? specification = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(TestSpecificationEvaluator.Apply(Items.AsQueryable(), specification).Any());
        }

        public Task<T?> GetByIdAsync<TKey>(TKey id, CancellationToken cancellationToken = default)
            where TKey : notnull
        {
            return Task.FromResult(Items.FirstOrDefault(item => Equals(GetId(item), id)));
        }

        public Task<List<T>> GetListAsync(Expression<Func<T, bool>> expression, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items.AsQueryable().Where(expression).ToList());
        }

        public Task<int> GetCountAsync(Expression<Func<T, bool>> expression, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items.AsQueryable().Count(expression));
        }

        public Task<T?> GetAsync(
            Expression<Func<T, bool>> expression,
            Expression<Func<T, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items.AsQueryable().FirstOrDefault(expression));
        }

        public Task<List<T>> GetListAsync(
            Expression<Func<T, bool>> expression,
            Expression<Func<T, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items.AsQueryable().Where(expression).ToList());
        }

        private static object? GetId(T item)
        {
            return typeof(T).GetProperty("Id")?.GetValue(item);
        }
    }
}
