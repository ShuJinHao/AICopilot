using System.Reflection;
using System.Text.Json;
using AICopilot.AiGatewayService.CloudReadiness;
using AICopilot.AiGatewayService.TrialOperations;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.ProductionOperations;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Domain;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Specification;

namespace AICopilot.BackendTests;

[Trait("Suite", "EnterpriseProductionPilotHardeningP16_0")]
public sealed class EnterpriseProductionPilotHardeningP16Tests
{
    [Fact]
    public void RepositoryStores_ShouldPersistP12WindowRunAndP13IntentRunAcrossStoreInstances()
    {
        var repositories = new ProductionPilotRepositories();
        var p12 = repositories.CreateP12Store();
        var p13 = repositories.CreateP13Store();
        var now = DateTimeOffset.UtcNow;
        var window = new CloudReadonlyProductionPilotWindowDto(
            "p12win_test",
            "P12 test window",
            CloudReadonlyProductionPilotWindowStatuses.Approved,
            now.AddMinutes(-5),
            now.AddHours(2),
            ["devices"],
            7,
            50,
            5000,
            "PilotOps",
            "ToolApproval",
            "EmergencyStop");
        var p12Result = CreateP12Result(window.WindowId, now);
        var intent = new CloudProductionGoalIntentDto(
            "intent-test-001",
            "sha256:intent-goal",
            ["devices"],
            null,
            null,
            new CloudProductionGoalTimeRangeDto(now.AddDays(-1), now),
            50,
            ["Markdown"],
            "DeviceList",
            [],
            [],
            true,
            true);
        var p13Result = CreateP13Result(window.WindowId, intent.IntentId, now.AddMinutes(1));

        p12.SaveWindow(window);
        p12.SaveRun(p12Result);
        p13.SaveIntent(intent);
        p13.SaveRun(p13Result);

        var reloadedP12 = repositories.CreateP12Store();
        var reloadedP13 = repositories.CreateP13Store();

        reloadedP12.GetWindow(window.WindowId).Should().BeEquivalentTo(window);
        reloadedP12.ListRuns().Should().ContainSingle(item =>
            item.QueryResult.QueryHash == p12Result.QueryResult.QueryHash &&
            item.QueryResult.Rows.Count == 0);
        reloadedP13.GetIntent(intent.IntentId).Should().BeEquivalentTo(intent);
        reloadedP13.ListRuns().Should().ContainSingle(item =>
            item.QueryResult.ResultHash == p13Result.QueryResult.ResultHash &&
            item.QueryResult.Rows.Count == 0);
    }

    [Fact]
    public void BackfillFinalArtifactRefs_ShouldUpdateP12AndP13LedgersWithoutRawRows()
    {
        var operationsStore = new InMemoryProductionPilotOperationsStore();
        var operations = new CloudReadonlyProductionOperationsService(
            operationsStore,
            new InMemoryCloudReadonlyProductionPilotStore(),
            new InMemoryCloudReadonlyProductionControlledPilotStore());
        var taskId = AgentTaskId.New();
        var workspace = new ArtifactWorkspace(
            taskId,
            "ws_p16_artifact_refs",
            "workspace-root",
            "/workspace/ws_p16_artifact_refs",
            DateTimeOffset.UtcNow);
        var p12 = CreateP12Result("p12win_test", DateTimeOffset.UtcNow);
        var p13 = CreateP13Result("p12win_test", "intent-test-001", DateTimeOffset.UtcNow.AddMinutes(1));
        operations.UpsertRunLedger(CloudReadonlyProductionOperationsService.CreateRunLedger(p12));
        operations.UpsertRunLedger(CloudReadonlyProductionOperationsService.CreateRunLedger(p13));

        var p12Artifact = AddFinalArtifact(workspace, "p12-report.md", p12.QueryResult.SourceMode, p12.QueryResult.Boundary, p12.QueryResult.QueryHash, p12.QueryResult.ResultHash);
        var p13Artifact = AddFinalArtifact(workspace, "p13-report.md", p13.QueryResult.SourceMode, p13.QueryResult.Boundary, p13.QueryResult.QueryHash, p13.QueryResult.ResultHash);

        var warnings = operations.BackfillFinalArtifactRefs(taskId.Value, workspace.Artifacts.ToArray());
        var ledger = operations.BuildLedger();
        var json = JsonSerializer.Serialize(new { ledger, CloudReadonlyProductionOperationsService.RowsRetentionPolicy }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        warnings.Should().BeEmpty();
        ledger.Should().Contain(item => item.SourceMode == CloudReadonlyProductionPilotMarkers.SourceMode && item.ArtifactIds.Contains(p12Artifact.Id.Value) && item.TaskId == taskId.Value);
        ledger.Should().Contain(item => item.SourceMode == CloudReadonlyProductionControlledPilotMarkers.SourceMode && item.ArtifactIds.Contains(p13Artifact.Id.Value) && item.TaskId == taskId.Value);
        ledger.Should().OnlyContain(item => item.ApprovalStatus == "Finalized");
        json.Contains("\"rows\"", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        json.Contains("\"rawPayload\"", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        json.Contains("token", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
    }

    [Fact]
    public void RowsRetentionPolicy_ShouldExposeHashOnlyEvidence()
    {
        var status = new CloudReadonlyProductionOperationsService(
                new InMemoryProductionPilotOperationsStore(),
                new InMemoryCloudReadonlyProductionPilotStore(),
                new InMemoryCloudReadonlyProductionControlledPilotStore())
            .BuildStatus(
                new CloudReadonlyProductionPilotStatusDto(
                    CloudReadonlyProductionPilotStatuses.Disabled,
                    false,
                    null,
                    null,
                    [],
                    "NotRequired",
                    false,
                    false,
                    null,
                    [],
                    []),
                new CloudReadonlyProductionControlledPilotStatusDto(
                    CloudReadonlyProductionControlledPilotStatuses.Disabled,
                    false,
                    CloudReadonlyProductionPilotStatuses.Disabled,
                    null,
                    null,
                    false,
                    [],
                    false,
                    false,
                    null,
                    [],
                    []));

        status.RowsRetentionPolicy.PersistenceMode.Should().Be("HashOnly");
        status.RowsRetentionPolicy.LedgerStoresRows.Should().BeFalse();
        status.RowsRetentionPolicy.LedgerStoresRawPayload.Should().BeFalse();
        status.RowsRetentionPolicy.ReportsReturnRows.Should().BeFalse();
    }

    [Fact]
    public void ProductionOperationsCommands_ShouldKeepManageAndAuditPermissions()
    {
        PermissionOf<ActivateProductionPilotEmergencyStopCommand>().Should().Be(TrialOperationsPermissions.Manage);
        PermissionOf<ClearProductionPilotEmergencyStopCommand>().Should().Be(TrialOperationsPermissions.Manage);
        PermissionOf<UpsertProductionPilotIncidentCommand>().Should().Be(TrialOperationsPermissions.Manage);
        PermissionOf<RunProductionPilotGaReadinessEvaluationCommand>().Should().Be(TrialOperationsPermissions.AuditView);
        PermissionOf<GetCloudReadonlyProductionOperationsStatusQuery>().Should().Be(TrialOperationsPermissions.Read);
        PermissionOf<GetProductionPilotRunLedgerQuery>().Should().Be(TrialOperationsPermissions.Read);
    }

    [Fact]
    public async Task EmergencyStop_ShouldRemainConsistentUnderConcurrentActivateAndClear()
    {
        var store = new InMemoryProductionPilotOperationsStore();
        var tasks = Enumerable.Range(0, 20)
            .Select(index => Task.Run(() =>
            {
                if (index % 2 == 0)
                {
                    store.ActivateEmergencyStop($"activate-{index}", "tester", DateTimeOffset.UtcNow);
                }
                else
                {
                    store.ClearEmergencyStop($"clear-{index}", "tester", DateTimeOffset.UtcNow);
                }
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        var state = store.GetEmergencyStop();
        state.Reason.Should().NotBeNullOrWhiteSpace();
        (state.ActivatedAt is not null || state.ClearedAt is not null).Should().BeTrue();
    }

    private static Artifact AddFinalArtifact(
        ArtifactWorkspace workspace,
        string name,
        string sourceMode,
        string boundary,
        string queryHash,
        string resultHash)
    {
        var artifact = workspace.AddDraftArtifact(
            ArtifactType.Markdown,
            name,
            $"draft/{name}",
            128,
            "text/markdown",
            null,
            DateTimeOffset.UtcNow);
        artifact.ApplySourceMetadata(new ArtifactSourceMetadata(
            sourceMode,
            boundary,
            IsSimulation: false,
            IsSandbox: false,
            "Cloud production readonly Pilot",
            queryHash,
            resultHash,
            2,
            IsTruncated: false));
        artifact.Approve(DateTimeOffset.UtcNow);
        artifact.MarkFinal($"final/{name}", DateTimeOffset.UtcNow);
        return artifact;
    }

    private static CloudReadonlyProductionPilotScenarioResultDto CreateP12Result(string windowId, DateTimeOffset executedAt) =>
        new(
            "cloud-production-pilot-devices",
            "Device list",
            CloudReadonlyProductionPilotStatuses.Completed,
            new CloudProductionPilotQueryResultDto(
                "devices",
                CloudReadonlyProductionPilotMarkers.SourceType,
                CloudReadonlyProductionPilotMarkers.SourceMode,
                true,
                false,
                false,
                CloudReadonlyProductionPilotMarkers.SourceLabel,
                CloudReadonlyProductionPilotMarkers.Boundary,
                windowId,
                "sha256:p12-query",
                "sha256:p12-result",
                2,
                false,
                [new Dictionary<string, object?> { ["deviceCode"] = "D-001" }],
                executedAt,
                15,
                "ToolApprovalRequired"),
            ["Markdown"]);

    private static CloudReadonlyProductionControlledPilotResultDto CreateP13Result(string windowId, string intentId, DateTimeOffset executedAt) =>
        new(
            intentId,
            "DeviceList",
            CloudReadonlyProductionControlledPilotStatuses.Completed,
            new CloudProductionControlledQueryResultDto(
                "devices",
                CloudReadonlyProductionControlledPilotMarkers.SourceType,
                CloudReadonlyProductionControlledPilotMarkers.SourceMode,
                true,
                false,
                false,
                CloudReadonlyProductionControlledPilotMarkers.SourceLabel,
                CloudReadonlyProductionControlledPilotMarkers.Boundary,
                windowId,
                intentId,
                "sha256:p13-query",
                "sha256:p13-result",
                2,
                false,
                [new Dictionary<string, object?> { ["deviceCode"] = "D-002" }],
                executedAt,
                17,
                "ToolApprovalRequired"),
            ["Markdown"]);

    private static string PermissionOf<T>() =>
        typeof(T).GetCustomAttribute<AuthorizeRequirementAttribute>()?.Permission
        ?? throw new InvalidOperationException($"{typeof(T).Name} does not declare AuthorizeRequirement.");

    private sealed class ProductionPilotRepositories
    {
        private readonly MemoryRepository<ProductionPilotWindow> windows = new();
        private readonly MemoryRepository<ProductionPilotRun> p12Runs = new();
        private readonly MemoryRepository<ProductionControlledPilotIntent> intents = new();
        private readonly MemoryRepository<ProductionControlledPilotRun> p13Runs = new();

        public RepositoryCloudReadonlyProductionPilotStore CreateP12Store() => new(windows, p12Runs);

        public RepositoryCloudReadonlyProductionControlledPilotStore CreateP13Store() => new(intents, p13Runs);
    }

    private sealed class MemoryRepository<T> : IRepository<T>
        where T : class, IEntity, IAggregateRoot
    {
        private readonly object sync = new();
        private readonly List<T> items = [];

        public T Add(T entity)
        {
            lock (sync)
            {
                items.Add(entity);
                return entity;
            }
        }

        public void Update(T entity)
        {
        }

        public void Delete(T entity)
        {
            lock (sync)
            {
                items.Remove(entity);
            }
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);

        public Task<List<T>> ListAsync(ISpecification<T>? specification = null, CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                return Task.FromResult(items.ToList());
            }
        }

        public Task<T?> FirstOrDefaultAsync(ISpecification<T>? specification = null, CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                return Task.FromResult(items.FirstOrDefault());
            }
        }

        public Task<int> CountAsync(ISpecification<T>? specification = null, CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                return Task.FromResult(items.Count);
            }
        }

        public Task<bool> AnyAsync(ISpecification<T>? specification = null, CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                return Task.FromResult(items.Count > 0);
            }
        }

        public Task<T?> GetByIdAsync<TKey>(TKey id, CancellationToken cancellationToken = default)
            where TKey : notnull
        {
            lock (sync)
            {
                return Task.FromResult(items.FirstOrDefault(item => Equals(GetId(item), id)));
            }
        }

        public Task<List<T>> GetListAsync(System.Linq.Expressions.Expression<Func<T, bool>> expression, CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                return Task.FromResult(items.AsQueryable().Where(expression).ToList());
            }
        }

        public Task<int> GetCountAsync(System.Linq.Expressions.Expression<Func<T, bool>> expression, CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                return Task.FromResult(items.AsQueryable().Count(expression));
            }
        }

        public Task<T?> GetAsync(
            System.Linq.Expressions.Expression<Func<T, bool>> expression,
            System.Linq.Expressions.Expression<Func<T, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                return Task.FromResult(items.AsQueryable().FirstOrDefault(expression));
            }
        }

        public Task<List<T>> GetListAsync(
            System.Linq.Expressions.Expression<Func<T, bool>> expression,
            System.Linq.Expressions.Expression<Func<T, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                return Task.FromResult(items.AsQueryable().Where(expression).ToList());
            }
        }

        private static object? GetId(T item) => typeof(T).GetProperty("Id")?.GetValue(item);
    }
}
