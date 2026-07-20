using System.Text.Json;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.AuditLogs;
using AICopilot.EntityFrameworkCore.Outbox;
using AICopilot.EntityFrameworkCore.Persistence;
using AICopilot.EntityFrameworkCore.Repository;
using AICopilot.EntityFrameworkCore.Transactions;
using AICopilot.SharedKernel.Result;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.PersistenceTests;

[Collection(PostgresPersistenceTestCollection.Name)]
public sealed class AgentTaskPlanPersistenceAtomicityTests(PostgresPersistenceFixture fixture)
{
    [Fact]
    public async Task Save_ShouldRoundTripExactCanonicalPlanThroughFreshContextInSameTransaction()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var planJson = AgentPlanV2TestData.CreateSingleStep("generate_chart_data", executable: false);
        await using var scope = CreateScope(
            database.ConnectionString,
            new AgentTaskPlanPersistencePolicy(new AgentPlanCanonicalizer()));
        var task = scope.StageTask(planJson);

        await scope.Repository.SaveChangesAsync();

        await using var fresh = CreateAiGatewayContext(database.ConnectionString);
        var persisted = await fresh.AgentTasks.AsNoTracking().SingleAsync(item => item.Id == task.Id);
        persisted.PlanJson.Should().Be(planJson);
        new AgentPlanCanonicalizer().ValidatePersisted(persisted.PlanJson).IsSuccess.Should().BeTrue();
        await AssertCountsAsync(database.ConnectionString, tasks: 1, markers: 1);
    }

    [Fact]
    public async Task Save_ShouldRollbackBusinessRowAndMarker_WhenCanonicalPolicyRejectsPlan()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        await using var scope = CreateScope(
            database.ConnectionString,
            new AgentTaskPlanPersistencePolicy(new AgentPlanCanonicalizer()));
        scope.StageTask("{\"version\":1}");

        Func<Task> action = () => scope.Repository.SaveChangesAsync();

        var failure = await action.Should().ThrowAsync<AgentTaskPlanPersistenceIntegrityException>();
        failure.Which.ErrorCode.Should().Be(AppProblemCodes.AgentPlanInvalid);
        await AssertCountsAsync(database.ConnectionString, tasks: 0, markers: 0);
    }

    [Fact]
    public async Task Save_ShouldRollbackBusinessRowAndMarker_WhenAttemptPolicyFailsAfterDatabaseWrite()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        await using var scope = CreateScope(database.ConnectionString, new RejectingPolicy());
        scope.StageTask(AgentPlanV2TestData.CreateSingleStep("generate_chart_data", executable: false));

        Func<Task> action = () => scope.Repository.SaveChangesAsync();

        var failure = await action.Should().ThrowAsync<AgentTaskPlanPersistenceIntegrityException>();
        failure.Which.ErrorCode.Should().Be("injected_plan_policy_failure");
        await AssertCountsAsync(database.ConnectionString, tasks: 0, markers: 0);
    }

    [Fact]
    public async Task Save_ShouldRejectOversizePlanAtCanonicalPolicyWithoutDomainTruncation()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        await using var scope = CreateScope(
            database.ConnectionString,
            new AgentTaskPlanPersistencePolicy(new AgentPlanCanonicalizer()));
        var oversize = new string('x', 262_145);
        var task = scope.StageTask(oversize);
        task.PlanJson.Should().Be(oversize, "the domain must not silently truncate structured Plan JSON");

        Func<Task> action = () => scope.Repository.SaveChangesAsync();

        var failure = await action.Should().ThrowAsync<AgentTaskPlanPersistenceIntegrityException>();
        failure.Which.ErrorCode.Should().Be(AppProblemCodes.PlanPayloadTooLarge);
        await AssertCountsAsync(database.ConnectionString, tasks: 0, markers: 0);
    }

    [Fact]
    public async Task HistoricalCompletedV1_ShouldRemainReadableButAnyWriteMustRollback()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var seeded = CreateCompletedLegacyTask();
        await using (var seedContext = CreateAiGatewayContext(database.ConnectionString))
        {
            seedContext.Sessions.Add(seeded.Session);
            seedContext.AgentTasks.Add(seeded.Task);
            await seedContext.SaveChangesAsync();
        }

        await using var scope = CreateScope(
            database.ConnectionString,
            new AgentTaskPlanPersistencePolicy(new AgentPlanCanonicalizer()));
        var historical = await scope.Business.AgentTasks.SingleAsync(task => task.Id == seeded.Task.Id);
        var metadata = AgentTaskPlanMetadataResolver.Resolve(historical);
        metadata.IntegrityStatus.Should().Be(AgentTaskPlanMetadataResolver.LegacyCompletedReadOnly);
        metadata.IsExecutable.Should().BeFalse();

        historical.Cancel(DateTimeOffset.UtcNow);
        scope.Repository.Update(historical);
        Func<Task> action = () => scope.Repository.SaveChangesAsync();

        var failure = await action.Should().ThrowAsync<AgentTaskPlanPersistenceIntegrityException>();
        failure.Which.ErrorCode.Should().Be(AppProblemCodes.AgentPlanInvalid);
        await using var fresh = CreateAiGatewayContext(database.ConnectionString);
        var unchanged = await fresh.AgentTasks.AsNoTracking().SingleAsync(task => task.Id == seeded.Task.Id);
        unchanged.Status.Should().Be(AgentTaskStatus.Completed);
        unchanged.PlanJson.Should().Be("{\"version\":1}");
        await AssertCountsAsync(database.ConnectionString, tasks: 1, markers: 0);
    }

    [Fact]
    public async Task Enqueue_ShouldRejectValidDetachedPlan_WhenIndependentDatabaseBytesHaveDrifted()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var originalPlan = AgentPlanV2TestData.CreateSingleStep(
            "generate_chart_data",
            executable: false);
        var driftedPlan = AgentPlanV2TestData.CreateSingleStep(
            "generate_markdown_report",
            executable: false);
        var seeded = CreateApprovedTask(originalPlan);
        await using (var seedContext = CreateAiGatewayContext(database.ConnectionString))
        {
            seedContext.Sessions.Add(seeded.Session);
            seedContext.AgentTasks.Add(seeded.Task);
            await seedContext.SaveChangesAsync();
        }

        AgentTask detached;
        await using (var callerContext = CreateAiGatewayContext(database.ConnectionString))
        {
            detached = await callerContext.AgentTasks
                .Include(task => task.Steps)
                .AsNoTracking()
                .SingleAsync(task => task.Id == seeded.Task.Id);
        }

        new AgentPlanCanonicalizer()
            .ValidatePersisted(detached.PlanJson, requireExecutable: false)
            .IsSuccess.Should().BeTrue("the caller holds a locally valid P0 PlanDraft");

        await using (var driftContext = CreateAiGatewayContext(database.ConnectionString))
        {
            await driftContext.AgentTasks
                .Where(task => task.Id == seeded.Task.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(task => task.PlanJson, driftedPlan));
        }

        var options = PostgresPersistenceTestOptions.Create<AiGatewayDbContext>(
            database.ConnectionString,
            MigrationHistoryTables.AiGateway);
        var gate = AgentPlanV2TestData.CreateDownstreamRuntimeHarnessFreshReadGate(
            new AgentTaskPlanFreshReadVerifier(options));
        var queueStore = new ToolRegistryGovernanceTestBase.InMemoryAgentTaskRunQueueStore();
        var result = await new AgentTaskRunQueue(queueStore, gate).EnqueueAsync(
            detached,
            AgentTaskRunTriggerType.Manual,
            detached.UserId,
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ApiProblemDescriptor(
                AppProblemCodes.AgentPlanInvalid,
                "Persisted agent task plan changed; reload and re-confirm before continuing."));
        queueStore.Items.Should().BeEmpty();
    }

    private async Task<PostgresScratchDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_plan_atomicity");
        await using var aiCopilot = new AiCopilotDbContext(
            PostgresPersistenceTestOptions.Create<AiCopilotDbContext>(
                database.ConnectionString,
                MigrationHistoryTables.AiCopilot));
        await aiCopilot.Database.MigrateAsync();
        await using var aiGateway = CreateAiGatewayContext(database.ConnectionString);
        await aiGateway.Database.MigrateAsync();
        return database;
    }

    private static PersistenceScope CreateScope(
        string connectionString,
        IAgentTaskPlanPersistencePolicy policy)
    {
        var business = CreateAiGatewayContext(connectionString);
        var audit = new AuditDbContext(PostgresPersistenceTestOptions.CreateAudit(connectionString));
        var committer = new RepositoryPersistenceCommitter(
            audit,
            new PersistenceCommitEngine(PostgresPersistenceTestOptions.CreateMarker(connectionString)),
            [new AiGatewayDomainEventOutboxSource()],
            new PersistenceCommitScope(),
            [new AgentTaskPlanPersistenceAttemptValidator([policy])]);
        return new PersistenceScope(
            business,
            audit,
            new AiGatewayRepository<AgentTask>(business, committer));
    }

    private static AiGatewayDbContext CreateAiGatewayContext(string connectionString)
    {
        return new AiGatewayDbContext(
            PostgresPersistenceTestOptions.Create<AiGatewayDbContext>(
                connectionString,
                MigrationHistoryTables.AiGateway));
    }

    private static async Task AssertCountsAsync(
        string connectionString,
        int tasks,
        int markers)
    {
        await using var fresh = CreateAiGatewayContext(connectionString);
        (await fresh.AgentTasks.CountAsync()).Should().Be(tasks);
        (await fresh.Sessions.CountAsync()).Should().Be(tasks);
        await using var marker = new PersistenceCommitMarkerDbContext(
            PostgresPersistenceTestOptions.CreateMarker(connectionString));
        (await marker.CommitMarkers.CountAsync()).Should().Be(markers);
    }

    private static (Session Session, AgentTask Task) CreateCompletedLegacyTask()
    {
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        var session = new Session(userId, ConversationTemplateId.New());
        var task = new AgentTask(
            session.Id,
            userId,
            "Historical legacy plan",
            "Historical legacy plan",
            AgentTaskType.ReportGeneration,
            AgentTaskRiskLevel.Low,
            null,
            "{\"version\":1}",
            now);
        task.ConfirmExecutablePlan(task.PlanJson, [], now);
        task.ApprovePlan(now);
        task.AttachWorkspace(ArtifactWorkspaceId.New(), now);
        task.Start(now);
        task.MarkWorkspaceReady(now);
        task.WaitForFinalApproval(now);
        task.Complete("historical completed output", now);
        return (session, task);
    }

    private static (Session Session, AgentTask Task) CreateApprovedTask(string planJson)
    {
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        var session = new Session(userId, ConversationTemplateId.New());
        var task = new AgentTask(
            session.Id,
            userId,
            "Fresh read executable plan",
            "Reject stale executable plan bytes.",
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
        task.ConfirmExecutablePlan(
            planJson,
            trackedSteps.Where(step => step.RequiresApproval).Select(step => step.StepIndex).ToArray(),
            now);
        task.ApprovePlan(now);
        return (session, task);
    }

    private sealed class PersistenceScope(
        AiGatewayDbContext business,
        AuditDbContext audit,
        AiGatewayRepository<AgentTask> repository)
        : IAsyncDisposable
    {
        public AiGatewayDbContext Business { get; } = business;

        public AiGatewayRepository<AgentTask> Repository { get; } = repository;

        public AgentTask StageTask(string planJson)
        {
            var now = DateTimeOffset.UtcNow;
            var userId = Guid.NewGuid();
            var session = new Session(userId, ConversationTemplateId.New());
            var task = new AgentTask(
                session.Id,
                userId,
                "Plan persistence",
                "Verify canonical plan persistence.",
                AgentTaskType.ReportGeneration,
                AgentTaskRiskLevel.Low,
                null,
                planJson,
                now);
            if (new AgentPlanCanonicalizer()
                .ValidatePersisted(planJson, requireExecutable: false)
                .IsSuccess)
            {
                var plan = JsonSerializer.Deserialize<AgentTaskPlanDocument>(
                    planJson,
                    AgentRuntimeJson.Options)!;
                foreach (var step in plan.Steps)
                {
                    task.AddStep(
                        step.Title,
                        step.Description,
                        step.StepType,
                        step.ToolCode,
                        step.RequiresApproval,
                        now,
                        step.InputJson);
                }
            }

            Business.Sessions.Add(session);
            Repository.Add(task);
            return task;
        }

        public async ValueTask DisposeAsync()
        {
            await audit.DisposeAsync();
            await Business.DisposeAsync();
        }
    }

    private sealed class RejectingPolicy : IAgentTaskPlanPersistencePolicy
    {
        public AgentTaskPlanPersistenceValidationDecision Validate(
            AgentTaskPlanPersistenceValidationRequest request)
        {
            return AgentTaskPlanPersistenceValidationDecision.Invalid(
                "injected_plan_policy_failure",
                "Injected test policy rejected the fresh-context plan.");
        }
    }
}
