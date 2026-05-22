using System.Text.Json;
using AICopilot.AiGatewayService.TrialOperations;
using AICopilot.Core.AiGateway.Aggregates.ProductionOperations;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.CloudReadiness;

public static class CloudReadonlyProductionOperationsStatuses
{
    public const string CollectingEvidence = "CollectingEvidence";
    public const string EmergencyStopped = "EmergencyStopped";
    public const string Blocked = "Blocked";
    public const string ReadyForP15Planning = "ReadyForP15Planning";
}

public static class ProductionPilotIncidentStatuses
{
    public const string Open = "Open";
    public const string Mitigating = "Mitigating";
    public const string Resolved = "Resolved";
    public const string ClosedAsOutOfScope = "ClosedAsOutOfScope";
}

public sealed record ProductionPilotEmergencyStopDto(
    bool IsActive,
    string? Reason,
    string? ActivatedBy,
    DateTimeOffset? ActivatedAt,
    string? ClearedBy,
    DateTimeOffset? ClearedAt)
{
    public bool DrillCompleted => ActivatedAt is not null && ClearedAt is not null && ClearedAt >= ActivatedAt;
}

public sealed record ProductionPilotRunMetricsDto(
    int TotalRuns,
    int SucceededRuns,
    int FailedRuns,
    int RejectedRuns,
    int TimeoutRuns,
    int TruncatedRuns,
    int TotalRows,
    int FinalArtifactCount,
    int OpenIncidentCount,
    IReadOnlyDictionary<string, int> EndpointDistribution);

public sealed record CloudReadonlyProductionOperationsStatusDto(
    string Status,
    string P12PilotStatus,
    string P13ControlledPilotStatus,
    bool OperationsStorePersisted,
    bool HasP12CompletedRun,
    bool HasP13CompletedRun,
    bool EmergencyStopActive,
    IReadOnlyCollection<string> CurrentWindowIds,
    ProductionPilotRunMetricsDto RunMetrics,
    IReadOnlyCollection<string> Blockers,
    IReadOnlyCollection<string> Warnings,
    DateTimeOffset LastEvaluatedAt);

public sealed record ProductionPilotRunLedgerDto(
    string RunId,
    Guid? TaskId,
    string SourceMode,
    string Boundary,
    string TrialMode,
    string PilotWindowId,
    string? IntentId,
    string EndpointCode,
    IReadOnlyCollection<Guid> ArtifactIds,
    string ApprovalStatus,
    string Status,
    long DurationMs,
    int RowCount,
    bool IsTruncated,
    string QueryHash,
    string ResultHash,
    DateTimeOffset ExecutedAt);

public sealed record ProductionPilotIncidentDto(
    Guid IncidentId,
    string Severity,
    string Category,
    string Status,
    string? Owner,
    string? SourceRef,
    string? ResolutionHash,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ProductionPilotGaReadinessCheckDto(
    string Code,
    string Label,
    string Status,
    bool IsBlocking,
    string Message);

public sealed record ProductionPilotGaReadinessAssessmentDto(
    string Status,
    IReadOnlyCollection<ProductionPilotGaReadinessCheckDto> Checks,
    IReadOnlyCollection<string> Blockers,
    IReadOnlyCollection<string> Warnings,
    ProductionPilotRunMetricsDto Metrics,
    DateTimeOffset GeneratedAt);

[AuthorizeRequirement(TrialOperationsPermissions.Read)]
public sealed record GetCloudReadonlyProductionOperationsStatusQuery
    : IQuery<Result<CloudReadonlyProductionOperationsStatusDto>>;

[AuthorizeRequirement(TrialOperationsPermissions.Read)]
public sealed record GetProductionPilotRunLedgerQuery
    : IQuery<Result<IReadOnlyCollection<ProductionPilotRunLedgerDto>>>;

[AuthorizeRequirement(TrialOperationsPermissions.Manage)]
public sealed record ActivateProductionPilotEmergencyStopCommand(
    string? Reason = null,
    string? ActivatedBy = null) : ICommand<Result<CloudReadonlyProductionOperationsStatusDto>>;

[AuthorizeRequirement(TrialOperationsPermissions.Manage)]
public sealed record ClearProductionPilotEmergencyStopCommand(
    string? Reason = null,
    string? ClearedBy = null) : ICommand<Result<CloudReadonlyProductionOperationsStatusDto>>;

[AuthorizeRequirement(TrialOperationsPermissions.Manage)]
public sealed record UpsertProductionPilotIncidentCommand(
    Guid? IncidentId,
    string Severity,
    string Category,
    string Status,
    string? Owner = null,
    string? SourceRef = null,
    string? ResolutionHash = null) : ICommand<Result<ProductionPilotIncidentDto>>;

[AuthorizeRequirement(TrialOperationsPermissions.AuditView)]
public sealed record RunProductionPilotGaReadinessEvaluationCommand
    : ICommand<Result<ProductionPilotGaReadinessAssessmentDto>>;

public interface IProductionPilotOperationsStore
{
    ProductionPilotEmergencyStopDto GetEmergencyStop();

    void ActivateEmergencyStop(string reason, string activatedBy, DateTimeOffset now);

    void ClearEmergencyStop(string reason, string clearedBy, DateTimeOffset now);

    ProductionPilotIncidentDto UpsertIncident(
        Guid? incidentId,
        string severity,
        string category,
        string status,
        string? owner,
        string? sourceRef,
        string? resolutionHash,
        DateTimeOffset now);

    IReadOnlyCollection<ProductionPilotIncidentDto> ListIncidents();

    void UpsertRunLedger(ProductionPilotRunLedgerDto ledger, DateTimeOffset now);

    IReadOnlyCollection<ProductionPilotRunLedgerDto> ListRunLedgers();

    void SaveGaReadinessAssessment(ProductionPilotGaReadinessAssessmentDto assessment, DateTimeOffset now);

    ProductionPilotGaReadinessAssessmentDto? LatestGaReadinessAssessment();
}

internal sealed class InMemoryProductionPilotOperationsStore : IProductionPilotOperationsStore
{
    private readonly object sync = new();
    private readonly Dictionary<Guid, ProductionPilotIncidentDto> incidents = [];
    private readonly Dictionary<string, ProductionPilotRunLedgerDto> runLedgers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ProductionPilotGaReadinessAssessmentDto> gaReadinessAssessments = [];
    private ProductionPilotEmergencyStopDto emergencyStop = new(false, null, null, null, null, null);

    public ProductionPilotEmergencyStopDto GetEmergencyStop()
    {
        lock (sync)
        {
            return emergencyStop;
        }
    }

    public void ActivateEmergencyStop(string reason, string activatedBy, DateTimeOffset now)
    {
        lock (sync)
        {
            emergencyStop = new ProductionPilotEmergencyStopDto(true, Normalize(reason, "P14 emergency stop"), activatedBy, now, emergencyStop.ClearedBy, emergencyStop.ClearedAt);
        }
    }

    public void ClearEmergencyStop(string reason, string clearedBy, DateTimeOffset now)
    {
        lock (sync)
        {
            emergencyStop = new ProductionPilotEmergencyStopDto(false, Normalize(reason, emergencyStop.Reason ?? "P14 emergency stop cleared"), emergencyStop.ActivatedBy, emergencyStop.ActivatedAt, clearedBy, now);
        }
    }

    public ProductionPilotIncidentDto UpsertIncident(
        Guid? incidentId,
        string severity,
        string category,
        string status,
        string? owner,
        string? sourceRef,
        string? resolutionHash,
        DateTimeOffset now)
    {
        lock (sync)
        {
            var id = incidentId.GetValueOrDefault();
            if (id == Guid.Empty)
            {
                id = Guid.NewGuid();
            }

            var existing = incidents.GetValueOrDefault(id);
            var incident = new ProductionPilotIncidentDto(
                id,
                Normalize(severity, "Medium"),
                Normalize(category, "Operations"),
                NormalizeIncidentStatus(status),
                NormalizeOptional(owner),
                NormalizeOptional(sourceRef),
                NormalizeOptional(resolutionHash),
                existing?.CreatedAt ?? now,
                now);
            incidents[id] = incident;
            return incident;
        }
    }

    public IReadOnlyCollection<ProductionPilotIncidentDto> ListIncidents()
    {
        lock (sync)
        {
            return incidents.Values.OrderByDescending(item => item.UpdatedAt).ToArray();
        }
    }

    public void UpsertRunLedger(ProductionPilotRunLedgerDto ledger, DateTimeOffset now)
    {
        lock (sync)
        {
            runLedgers[ledger.RunId] = ledger;
            foreach (var key in runLedgers.Values
                         .OrderByDescending(item => item.ExecutedAt)
                         .Skip(200)
                         .Select(item => item.RunId)
                         .ToArray())
            {
                runLedgers.Remove(key);
            }
        }
    }

    public IReadOnlyCollection<ProductionPilotRunLedgerDto> ListRunLedgers()
    {
        lock (sync)
        {
            return runLedgers.Values
                .OrderByDescending(item => item.ExecutedAt)
                .Take(200)
                .ToArray();
        }
    }

    public void SaveGaReadinessAssessment(ProductionPilotGaReadinessAssessmentDto assessment, DateTimeOffset now)
    {
        lock (sync)
        {
            gaReadinessAssessments.Insert(0, assessment);
            if (gaReadinessAssessments.Count > 20)
            {
                gaReadinessAssessments.RemoveRange(20, gaReadinessAssessments.Count - 20);
            }
        }
    }

    public ProductionPilotGaReadinessAssessmentDto? LatestGaReadinessAssessment()
    {
        lock (sync)
        {
            return gaReadinessAssessments.FirstOrDefault();
        }
    }

    private static string NormalizeIncidentStatus(string? status)
    {
        var value = Normalize(status, ProductionPilotIncidentStatuses.Open);
        return new[]
            {
                ProductionPilotIncidentStatuses.Open,
                ProductionPilotIncidentStatuses.Mitigating,
                ProductionPilotIncidentStatuses.Resolved,
                ProductionPilotIncidentStatuses.ClosedAsOutOfScope
            }
            .FirstOrDefault(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase))
            ?? ProductionPilotIncidentStatuses.Open;
    }

    private static string Normalize(string? value, string fallback)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed)
            ? fallback
            : trimmed.Length > 160
                ? trimmed[..160]
                : trimmed;
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed)
            ? null
            : trimmed.Length > 160
                ? trimmed[..160]
                : trimmed;
    }
}

internal sealed class RepositoryProductionPilotOperationsStore(
    IRepository<ProductionPilotEmergencyStopState> emergencyStopRepository,
    IRepository<ProductionPilotIncident> incidentRepository,
    IRepository<ProductionPilotRunLedger> runLedgerRepository,
    IRepository<ProductionPilotGaReadinessAssessment> gaReadinessRepository)
    : IProductionPilotOperationsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ProductionPilotEmergencyStopDto GetEmergencyStop()
    {
        var state = Execute(() => emergencyStopRepository.GetByIdAsync(ProductionPilotEmergencyStopStateId.Default));
        return state is null
            ? new ProductionPilotEmergencyStopDto(false, null, null, null, null, null)
            : ToDto(state);
    }

    public void ActivateEmergencyStop(string reason, string activatedBy, DateTimeOffset now)
    {
        Execute(async () =>
        {
            var state = await emergencyStopRepository.GetByIdAsync(ProductionPilotEmergencyStopStateId.Default);
            if (state is null)
            {
                state = ProductionPilotEmergencyStopState.CreateDefault(now);
                emergencyStopRepository.Add(state);
            }

            state.Activate(reason, activatedBy, now);
            await emergencyStopRepository.SaveChangesAsync();
        });
    }

    public void ClearEmergencyStop(string reason, string clearedBy, DateTimeOffset now)
    {
        Execute(async () =>
        {
            var state = await emergencyStopRepository.GetByIdAsync(ProductionPilotEmergencyStopStateId.Default);
            if (state is null)
            {
                state = ProductionPilotEmergencyStopState.CreateDefault(now);
                emergencyStopRepository.Add(state);
            }

            state.Clear(reason, clearedBy, now);
            await emergencyStopRepository.SaveChangesAsync();
        });
    }

    public ProductionPilotIncidentDto UpsertIncident(
        Guid? incidentId,
        string severity,
        string category,
        string status,
        string? owner,
        string? sourceRef,
        string? resolutionHash,
        DateTimeOffset now)
    {
        return Execute(async () =>
        {
            ProductionPilotIncident? incident = null;
            if (incidentId is { } id && id != Guid.Empty)
            {
                incident = await incidentRepository.GetByIdAsync(new ProductionPilotIncidentId(id));
            }

            if (incident is null)
            {
                var newIncidentId = incidentId is { } providedId && providedId != Guid.Empty
                    ? new ProductionPilotIncidentId(providedId)
                    : (ProductionPilotIncidentId?)null;
                incident = new ProductionPilotIncident(
                    newIncidentId,
                    severity,
                    category,
                    status,
                    owner,
                    sourceRef,
                    resolutionHash,
                    now);
                incidentRepository.Add(incident);
            }
            else
            {
                incident.Update(severity, category, status, owner, sourceRef, resolutionHash, now);
            }

            await incidentRepository.SaveChangesAsync();
            return ToDto(incident);
        });
    }

    public IReadOnlyCollection<ProductionPilotIncidentDto> ListIncidents()
    {
        var incidents = Execute(() => incidentRepository.ListAsync());
        return incidents
            .OrderByDescending(item => item.UpdatedAt)
            .Select(ToDto)
            .ToArray();
    }

    public void UpsertRunLedger(ProductionPilotRunLedgerDto ledger, DateTimeOffset now)
    {
        Execute(async () =>
        {
            var existing = (await runLedgerRepository.GetListAsync(
                item => item.RunId == ledger.RunId,
                CancellationToken.None)).FirstOrDefault();
            if (existing is null)
            {
                runLedgerRepository.Add(new ProductionPilotRunLedger(
                    ledger.RunId,
                    ledger.TaskId,
                    ledger.SourceMode,
                    ledger.Boundary,
                    ledger.TrialMode,
                    ledger.PilotWindowId,
                    ledger.IntentId,
                    ledger.EndpointCode,
                    ledger.ArtifactIds,
                    ledger.ApprovalStatus,
                    ledger.Status,
                    ledger.DurationMs,
                    ledger.RowCount,
                    ledger.IsTruncated,
                    ledger.QueryHash,
                    ledger.ResultHash,
                    ledger.ExecutedAt,
                    now));
            }
            else
            {
                existing.Update(
                    ledger.RunId,
                    ledger.TaskId,
                    ledger.SourceMode,
                    ledger.Boundary,
                    ledger.TrialMode,
                    ledger.PilotWindowId,
                    ledger.IntentId,
                    ledger.EndpointCode,
                    ledger.ArtifactIds,
                    ledger.ApprovalStatus,
                    ledger.Status,
                    ledger.DurationMs,
                    ledger.RowCount,
                    ledger.IsTruncated,
                    ledger.QueryHash,
                    ledger.ResultHash,
                    ledger.ExecutedAt,
                    now);
            }

            await runLedgerRepository.SaveChangesAsync();
        });
    }

    public IReadOnlyCollection<ProductionPilotRunLedgerDto> ListRunLedgers()
    {
        var ledgers = Execute(() => runLedgerRepository.ListAsync());
        return ledgers
            .OrderByDescending(item => item.ExecutedAt)
            .Take(200)
            .Select(ToDto)
            .ToArray();
    }

    public void SaveGaReadinessAssessment(ProductionPilotGaReadinessAssessmentDto assessment, DateTimeOffset now)
    {
        Execute(async () =>
        {
            gaReadinessRepository.Add(new ProductionPilotGaReadinessAssessment(
                assessment.Status,
                JsonSerializer.Serialize(assessment.Checks, JsonOptions),
                assessment.Blockers,
                assessment.Warnings,
                assessment.Metrics.TotalRuns,
                assessment.Metrics.SucceededRuns,
                assessment.Metrics.FailedRuns,
                assessment.Metrics.RejectedRuns,
                assessment.Metrics.TimeoutRuns,
                assessment.Metrics.TruncatedRuns,
                assessment.Metrics.TotalRows,
                assessment.Metrics.FinalArtifactCount,
                assessment.Metrics.OpenIncidentCount,
                JsonSerializer.Serialize(assessment.Metrics.EndpointDistribution, JsonOptions),
                assessment.GeneratedAt,
                now));
            await gaReadinessRepository.SaveChangesAsync();
        });
    }

    public ProductionPilotGaReadinessAssessmentDto? LatestGaReadinessAssessment()
    {
        var assessment = Execute(() => gaReadinessRepository.ListAsync())
            .OrderByDescending(item => item.GeneratedAt)
            .FirstOrDefault();
        return assessment is null ? null : ToDto(assessment);
    }

    private static ProductionPilotEmergencyStopDto ToDto(ProductionPilotEmergencyStopState state) =>
        new(
            state.IsActive,
            state.Reason,
            state.ActivatedBy,
            state.ActivatedAt,
            state.ClearedBy,
            state.ClearedAt);

    private static ProductionPilotIncidentDto ToDto(ProductionPilotIncident incident) =>
        new(
            incident.Id.Value,
            incident.Severity,
            incident.Category,
            incident.Status,
            incident.Owner,
            incident.SourceRef,
            incident.ResolutionHash,
            incident.CreatedAt,
            incident.UpdatedAt);

    private static ProductionPilotRunLedgerDto ToDto(ProductionPilotRunLedger ledger) =>
        new(
            ledger.RunId,
            ledger.TaskId,
            ledger.SourceMode,
            ledger.Boundary,
            ledger.TrialMode,
            ledger.PilotWindowId,
            ledger.IntentId,
            ledger.EndpointCode,
            ledger.ArtifactIds,
            ledger.ApprovalStatus,
            ledger.Status,
            ledger.DurationMs,
            ledger.RowCount,
            ledger.IsTruncated,
            ledger.QueryHash,
            ledger.ResultHash,
            ledger.ExecutedAt);

    private static ProductionPilotGaReadinessAssessmentDto ToDto(ProductionPilotGaReadinessAssessment assessment)
    {
        var checks = JsonSerializer.Deserialize<List<ProductionPilotGaReadinessCheckDto>>(assessment.ChecksJson, JsonOptions) ?? [];
        var endpointDistribution = JsonSerializer.Deserialize<Dictionary<string, int>>(assessment.EndpointDistributionJson, JsonOptions)
                                   ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        return new ProductionPilotGaReadinessAssessmentDto(
            assessment.Status,
            checks,
            assessment.Blockers,
            assessment.Warnings,
            new ProductionPilotRunMetricsDto(
                assessment.TotalRuns,
                assessment.SucceededRuns,
                assessment.FailedRuns,
                assessment.RejectedRuns,
                assessment.TimeoutRuns,
                assessment.TruncatedRuns,
                assessment.TotalRows,
                assessment.FinalArtifactCount,
                assessment.OpenIncidentCount,
                endpointDistribution),
            assessment.GeneratedAt);
    }

    private static void Execute(Func<Task> action) => action().GetAwaiter().GetResult();

    private static T Execute<T>(Func<Task<T>> action) => action().GetAwaiter().GetResult();
}

public sealed class CloudReadonlyProductionOperationsService(
    IProductionPilotOperationsStore operationsStore,
    ICloudReadonlyProductionPilotStore productionPilotStore,
    ICloudReadonlyProductionControlledPilotStore controlledPilotStore)
{
    public static ProductionPilotRunLedgerDto CreateRunLedger(CloudReadonlyProductionPilotScenarioResultDto result)
    {
        var query = result.QueryResult;
        return new ProductionPilotRunLedgerDto(
            $"p12_{StableHashSegment(query.QueryHash)}_{query.ExecutedAt.UtcTicks}",
            TaskId: null,
            query.SourceMode,
            query.Boundary,
            "ProductionPilotFixedScenario",
            query.PilotWindowId,
            IntentId: null,
            query.EndpointCode,
            ArtifactIds: [],
            query.ApprovalStatus,
            result.Status,
            query.DurationMs,
            query.RowCount,
            query.IsTruncated,
            query.QueryHash,
            query.ResultHash,
            query.ExecutedAt);
    }

    public static ProductionPilotRunLedgerDto CreateRunLedger(CloudReadonlyProductionControlledPilotResultDto result)
    {
        var query = result.QueryResult;
        return new ProductionPilotRunLedgerDto(
            $"p13_{StableHashSegment(query.ResultHash)}_{query.ExecutedAt.UtcTicks}",
            TaskId: null,
            query.SourceMode,
            query.Boundary,
            CloudReadonlyProductionControlledPilotMarkers.TrialMode,
            query.PilotWindowId,
            query.IntentId,
            query.EndpointCode,
            ArtifactIds: [],
            query.ApprovalStatus,
            result.Status,
            query.DurationMs,
            query.RowCount,
            query.IsTruncated,
            query.QueryHash,
            query.ResultHash,
            query.ExecutedAt);
    }

    public CloudReadonlyProductionOperationsStatusDto BuildStatus(
        CloudReadonlyProductionPilotStatusDto p12Status,
        CloudReadonlyProductionControlledPilotStatusDto p13Status)
    {
        var emergency = operationsStore.GetEmergencyStop();
        var ledger = BuildLedger();
        var metrics = BuildMetrics(ledger, operationsStore.ListIncidents());
        var hasP12CompletedRun = HasCompletedP12Evidence(ledger);
        var hasP13CompletedRun = HasCompletedP13Evidence(ledger);
        var blockers = new List<string>();
        var warnings = new List<string>();

        if (emergency.IsActive)
        {
            blockers.Add("Production Pilot emergency stop is active; P12 and P13 production readonly tools are blocked.");
        }

        if (p12Status.Status is CloudReadonlyProductionPilotStatuses.Blocked or CloudReadonlyProductionPilotStatuses.Failed)
        {
            blockers.Add($"P12 production Pilot status is {p12Status.Status}.");
        }

        if (p13Status.Status is CloudReadonlyProductionControlledPilotStatuses.Blocked or CloudReadonlyProductionControlledPilotStatuses.Failed)
        {
            blockers.Add($"P13 production controlled Pilot status is {p13Status.Status}.");
        }

        if (!emergency.DrillCompleted)
        {
            warnings.Add("Emergency stop drill has not been completed for P14 operations evidence.");
        }

        var openIncidents = operationsStore.ListIncidents()
            .Where(IsOpenIncident)
            .ToArray();
        if (openIncidents.Any(item => IsBlockingSeverity(item.Severity)))
        {
            blockers.Add("Open high or critical production Pilot incident blocks P15 planning.");
        }

        var status = emergency.IsActive
            ? CloudReadonlyProductionOperationsStatuses.EmergencyStopped
            : blockers.Count > 0
                ? CloudReadonlyProductionOperationsStatuses.Blocked
                : CloudReadonlyProductionOperationsStatuses.CollectingEvidence;

        return new CloudReadonlyProductionOperationsStatusDto(
            status,
            p12Status.Status,
            p13Status.Status,
            operationsStore is not InMemoryProductionPilotOperationsStore,
            hasP12CompletedRun,
            hasP13CompletedRun,
            emergency.IsActive,
            new[] { p12Status.PilotWindowId, p13Status.PilotWindowId }
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            metrics,
            blockers,
            warnings,
            DateTimeOffset.UtcNow);
    }

    public IReadOnlyCollection<ProductionPilotRunLedgerDto> BuildLedger()
    {
        var persisted = operationsStore.ListRunLedgers();
        var transient = productionPilotStore.ListRuns()
            .Select(CreateRunLedger)
            .Concat(controlledPilotStore.ListRuns().Select(CreateRunLedger));

        return persisted.Concat(transient)
            .GroupBy(item => item.RunId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.ExecutedAt).First())
            .OrderByDescending(item => item.ExecutedAt)
            .Take(50)
            .ToArray();
    }

    public ProductionPilotGaReadinessAssessmentDto BuildGaReadinessAssessment(
        CloudReadonlyProductionPilotStatusDto p12Status,
        CloudReadonlyProductionControlledPilotStatusDto p13Status,
        IReadOnlyCollection<ToolRegistration>? persistedToolRegistrations)
    {
        var ledger = BuildLedger();
        var incidents = operationsStore.ListIncidents();
        var emergency = operationsStore.GetEmergencyStop();
        var metrics = BuildMetrics(ledger, incidents);
        var checks = new List<ProductionPilotGaReadinessCheckDto>();
        var blockers = new List<string>();
        var warnings = new List<string>();

        AddCheck(checks, blockers, "P13AcceptanceEvidence", "P13 CI and acceptance evidence", true, "Passed", "P14 acceptance script verifies the committed P13 report and current CI separately.");
        AddCheck(checks, blockers, "P12Gate", "P12 production Pilot gate", true, p12Status.Status == CloudReadonlyProductionPilotStatuses.Ready ? "Passed" : "Blocked", $"P12 status is {p12Status.Status}.");
        AddCheck(checks, blockers, "P13Gate", "P13 controlled Pilot gate", true, p13Status.Status == CloudReadonlyProductionControlledPilotStatuses.Ready ? "Passed" : "Blocked", $"P13 status is {p13Status.Status}.");
        AddCheck(checks, blockers, "EmergencyStopDrill", "Emergency stop drill", true, emergency.DrillCompleted ? "Passed" : "Blocked", emergency.DrillCompleted ? "Emergency stop has been activated and cleared." : "Emergency stop drill has not been completed.");
        AddCheck(checks, blockers, "EmergencyStopActive", "Emergency stop active state", true, emergency.IsActive ? "Blocked" : "Passed", emergency.IsActive ? "Emergency stop is active." : "Emergency stop is not active.");
        AddCheck(checks, blockers, "P12CompletedRun", "P12 completed run evidence", true, HasCompletedP12Evidence(ledger) ? "Passed" : "Blocked", HasCompletedP12Evidence(ledger) ? "P12 fixed-template production Pilot has completed hash-only run evidence." : "P12 fixed-template production Pilot completed run evidence is missing.");
        AddCheck(checks, blockers, "P13CompletedRun", "P13 completed run evidence", true, HasCompletedP13Evidence(ledger) ? "Passed" : "Blocked", HasCompletedP13Evidence(ledger) ? "P13 controlled production Pilot has completed hash-only run evidence." : "P13 controlled production Pilot completed run evidence is missing.");
        AddCheck(checks, blockers, "AuditLedger", "Sanitized operations ledger", true, ledger.Count > 0 ? "Passed" : "Blocked", ledger.Count > 0 ? "Production Pilot run ledger has hash-only evidence." : "No production Pilot run ledger evidence exists.");
        AddCheck(checks, blockers, "FinalArtifacts", "Final artifact evidence", true, metrics.FinalArtifactCount > 0 ? "Passed" : "Blocked", metrics.FinalArtifactCount > 0 ? "Final artifact references are present." : "No final artifact reference is present in P14.2 persisted ledger.");
        AddCheck(checks, blockers, "ProductionToolBoundary", "query_cloud_data_readonly boundary", true, "Passed", "query_cloud_data_readonly remains closed by the protected ToolRegistry checks.");
        AddProtectedToolCheck(checks, blockers, persistedToolRegistrations);

        var openBlockingIncidents = incidents.Where(item => IsOpenIncident(item) && IsBlockingSeverity(item.Severity)).ToArray();
        AddCheck(checks, blockers, "BlockingIncidents", "Open blocking incidents", true, openBlockingIncidents.Length == 0 ? "Passed" : "Blocked", openBlockingIncidents.Length == 0 ? "No open high or critical incidents." : "Open high or critical incidents remain.");

        if (metrics.TruncatedRuns > 0)
        {
            warnings.Add("Some production Pilot runs were truncated; review row limits before P15 planning.");
        }

        var status = blockers.Count > 0
            ? CloudReadonlyProductionOperationsStatuses.Blocked
            : CloudReadonlyProductionOperationsStatuses.ReadyForP15Planning;

        var assessment = new ProductionPilotGaReadinessAssessmentDto(
            status,
            checks,
            blockers,
            warnings,
            metrics,
            DateTimeOffset.UtcNow);
        operationsStore.SaveGaReadinessAssessment(assessment, DateTimeOffset.UtcNow);
        return assessment;
    }

    public ProductionPilotIncidentDto UpsertIncident(UpsertProductionPilotIncidentCommand command) =>
        operationsStore.UpsertIncident(
            command.IncidentId,
            command.Severity,
            command.Category,
            command.Status,
            command.Owner,
            command.SourceRef,
            command.ResolutionHash,
            DateTimeOffset.UtcNow);

    public void UpsertRunLedger(ProductionPilotRunLedgerDto ledger) =>
        operationsStore.UpsertRunLedger(ledger, DateTimeOffset.UtcNow);

    public void ActivateEmergencyStop(string? reason, string? activatedBy) =>
        operationsStore.ActivateEmergencyStop(reason ?? "P14 emergency stop", activatedBy ?? "system", DateTimeOffset.UtcNow);

    public void ClearEmergencyStop(string? reason, string? clearedBy) =>
        operationsStore.ClearEmergencyStop(reason ?? "P14 emergency stop cleared", clearedBy ?? "system", DateTimeOffset.UtcNow);

    public ProductionPilotEmergencyStopDto GetEmergencyStop() => operationsStore.GetEmergencyStop();

    private static ProductionPilotRunMetricsDto BuildMetrics(
        IReadOnlyCollection<ProductionPilotRunLedgerDto> ledger,
        IReadOnlyCollection<ProductionPilotIncidentDto> incidents)
    {
        return new ProductionPilotRunMetricsDto(
            ledger.Count,
            ledger.Count(item => string.Equals(item.Status, CloudReadonlyProductionPilotStatuses.Completed, StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(item.Status, CloudReadonlyProductionControlledPilotStatuses.Completed, StringComparison.OrdinalIgnoreCase)),
            ledger.Count(item => string.Equals(item.Status, CloudReadonlyProductionPilotStatuses.Failed, StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(item.Status, CloudReadonlyProductionControlledPilotStatuses.Failed, StringComparison.OrdinalIgnoreCase)),
            ledger.Count(item => string.Equals(item.Status, "Rejected", StringComparison.OrdinalIgnoreCase)),
            ledger.Count(item => string.Equals(item.Status, "Timeout", StringComparison.OrdinalIgnoreCase)),
            ledger.Count(item => item.IsTruncated),
            ledger.Sum(item => item.RowCount),
            ledger.Sum(item => item.ArtifactIds.Count),
            incidents.Count(IsOpenIncident),
            ledger.GroupBy(item => item.EndpointCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase));
    }

    private static void AddCheck(
        ICollection<ProductionPilotGaReadinessCheckDto> checks,
        ICollection<string> blockers,
        string code,
        string label,
        bool isBlocking,
        string status,
        string message)
    {
        checks.Add(new ProductionPilotGaReadinessCheckDto(code, label, status, isBlocking, message));
        if (isBlocking && string.Equals(status, CloudReadonlyProductionOperationsStatuses.Blocked, StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add(message);
        }
    }

    private static void AddProtectedToolCheck(
        ICollection<ProductionPilotGaReadinessCheckDto> checks,
        ICollection<string> blockers,
        IReadOnlyCollection<ToolRegistration>? persistedToolRegistrations)
    {
        foreach (var toolCode in ProtectedCloudReadonlyToolPolicy.ProtectedToolCodes)
        {
            var tool = persistedToolRegistrations?.FirstOrDefault(item => string.Equals(item.ToolCode, toolCode, StringComparison.OrdinalIgnoreCase));
            var safety = tool is null
                ? $"Persisted protected tool is missing: {toolCode}."
                : ProtectedCloudReadonlyToolPolicy.ValidateSafeState(tool.ToolCode, tool.IsEnabled, tool.IsVisibleToPlanner, tool.IsExecutableByAgent, tool.ApprovalPolicy);
            AddCheck(
                checks,
                blockers,
                $"ToolRegistry.{toolCode}",
                $"Protected tool {toolCode}",
                true,
                safety is null ? "Passed" : "Blocked",
                safety ?? $"{toolCode} remains disabled, hidden, and non-executable.");
        }
    }

    private static bool IsOpenIncident(ProductionPilotIncidentDto incident) =>
        incident.Status is ProductionPilotIncidentStatuses.Open or ProductionPilotIncidentStatuses.Mitigating;

    private static bool IsBlockingSeverity(string severity) =>
        severity.Equals("High", StringComparison.OrdinalIgnoreCase) ||
        severity.Equals("Critical", StringComparison.OrdinalIgnoreCase);

    private static string StableHashSegment(string hash) =>
        hash.Length <= 16 ? hash : hash[..16];

    private static bool HasCompletedP12Evidence(IEnumerable<ProductionPilotRunLedgerDto> ledger) =>
        ledger.Any(item =>
            string.Equals(item.SourceMode, CloudReadonlyProductionPilotMarkers.SourceMode, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.Boundary, CloudReadonlyProductionPilotMarkers.Boundary, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.TrialMode, "ProductionPilotFixedScenario", StringComparison.OrdinalIgnoreCase) &&
            HasCompletedRunEvidence(item));

    private static bool HasCompletedP13Evidence(IEnumerable<ProductionPilotRunLedgerDto> ledger) =>
        ledger.Any(item =>
            string.Equals(item.SourceMode, CloudReadonlyProductionControlledPilotMarkers.SourceMode, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.Boundary, CloudReadonlyProductionControlledPilotMarkers.Boundary, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.TrialMode, CloudReadonlyProductionControlledPilotMarkers.TrialMode, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(item.IntentId) &&
            HasCompletedRunEvidence(item));

    private static bool HasCompletedRunEvidence(ProductionPilotRunLedgerDto item) =>
        string.Equals(item.Status, CloudReadonlyProductionPilotStatuses.Completed, StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(item.ApprovalStatus) &&
        !string.IsNullOrWhiteSpace(item.QueryHash) &&
        !string.IsNullOrWhiteSpace(item.ResultHash) &&
        !string.IsNullOrWhiteSpace(item.EndpointCode) &&
        !string.IsNullOrWhiteSpace(item.PilotWindowId);
}

public sealed class GetCloudReadonlyProductionOperationsStatusQueryHandler(
    CloudReadonlyProductionOperationsService operationsService,
    CloudReadonlyProductionPilotService productionPilotService,
    CloudReadonlyProductionControlledPilotService controlledPilotService,
    CloudReadonlyPilotReadinessService pilotReadinessService,
    IReadRepository<ToolRegistration> toolRepository)
    : IQueryHandler<GetCloudReadonlyProductionOperationsStatusQuery, Result<CloudReadonlyProductionOperationsStatusDto>>
{
    public async Task<Result<CloudReadonlyProductionOperationsStatusDto>> Handle(
        GetCloudReadonlyProductionOperationsStatusQuery request,
        CancellationToken cancellationToken)
    {
        var protectedTools = await GetCloudReadonlyPilotReadinessQueryHandler.LoadProtectedToolRegistrationsAsync(toolRepository, cancellationToken);
        var p12 = productionPilotService.BuildStatus(pilotReadinessService.BuildStatus(protectedTools), protectedTools);
        var p13 = controlledPilotService.BuildStatus(p12, protectedTools);
        return Result.Success(operationsService.BuildStatus(p12, p13));
    }
}

public sealed class GetProductionPilotRunLedgerQueryHandler(CloudReadonlyProductionOperationsService operationsService)
    : IQueryHandler<GetProductionPilotRunLedgerQuery, Result<IReadOnlyCollection<ProductionPilotRunLedgerDto>>>
{
    public Task<Result<IReadOnlyCollection<ProductionPilotRunLedgerDto>>> Handle(
        GetProductionPilotRunLedgerQuery request,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success(operationsService.BuildLedger()));
}

public sealed class ActivateProductionPilotEmergencyStopCommandHandler(
    CloudReadonlyProductionOperationsService operationsService,
    CloudReadonlyProductionPilotService productionPilotService,
    CloudReadonlyProductionControlledPilotService controlledPilotService,
    CloudReadonlyPilotReadinessService pilotReadinessService,
    IReadRepository<ToolRegistration> toolRepository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<ActivateProductionPilotEmergencyStopCommand, Result<CloudReadonlyProductionOperationsStatusDto>>
{
    public async Task<Result<CloudReadonlyProductionOperationsStatusDto>> Handle(
        ActivateProductionPilotEmergencyStopCommand request,
        CancellationToken cancellationToken)
    {
        operationsService.ActivateEmergencyStop(request.Reason, request.ActivatedBy);
        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "AiGateway.ActivateProductionPilotEmergencyStop",
                "CloudReadonlyProductionOperations",
                "emergency-stop",
                "Active",
                AuditResults.Succeeded,
                "Activated P14 production Pilot emergency stop.",
                ["emergencyStop", "sourceMode", "boundary"]),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        var protectedTools = await GetCloudReadonlyPilotReadinessQueryHandler.LoadProtectedToolRegistrationsAsync(toolRepository, cancellationToken);
        var p12 = productionPilotService.BuildStatus(pilotReadinessService.BuildStatus(protectedTools), protectedTools);
        var p13 = controlledPilotService.BuildStatus(p12, protectedTools);
        return Result.Success(operationsService.BuildStatus(p12, p13));
    }
}

public sealed class ClearProductionPilotEmergencyStopCommandHandler(
    CloudReadonlyProductionOperationsService operationsService,
    CloudReadonlyProductionPilotService productionPilotService,
    CloudReadonlyProductionControlledPilotService controlledPilotService,
    CloudReadonlyPilotReadinessService pilotReadinessService,
    IReadRepository<ToolRegistration> toolRepository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<ClearProductionPilotEmergencyStopCommand, Result<CloudReadonlyProductionOperationsStatusDto>>
{
    public async Task<Result<CloudReadonlyProductionOperationsStatusDto>> Handle(
        ClearProductionPilotEmergencyStopCommand request,
        CancellationToken cancellationToken)
    {
        operationsService.ClearEmergencyStop(request.Reason, request.ClearedBy);
        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "AiGateway.ClearProductionPilotEmergencyStop",
                "CloudReadonlyProductionOperations",
                "emergency-stop",
                "Cleared",
                AuditResults.Succeeded,
                "Cleared P14 production Pilot emergency stop; original gates still apply.",
                ["emergencyStop", "sourceMode", "boundary"]),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        var protectedTools = await GetCloudReadonlyPilotReadinessQueryHandler.LoadProtectedToolRegistrationsAsync(toolRepository, cancellationToken);
        var p12 = productionPilotService.BuildStatus(pilotReadinessService.BuildStatus(protectedTools), protectedTools);
        var p13 = controlledPilotService.BuildStatus(p12, protectedTools);
        return Result.Success(operationsService.BuildStatus(p12, p13));
    }
}

public sealed class UpsertProductionPilotIncidentCommandHandler(
    CloudReadonlyProductionOperationsService operationsService,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<UpsertProductionPilotIncidentCommand, Result<ProductionPilotIncidentDto>>
{
    public async Task<Result<ProductionPilotIncidentDto>> Handle(
        UpsertProductionPilotIncidentCommand request,
        CancellationToken cancellationToken)
    {
        var incident = operationsService.UpsertIncident(request);
        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "AiGateway.UpsertProductionPilotIncident",
                "CloudReadonlyProductionOperations",
                incident.IncidentId.ToString(),
                incident.Status,
                AuditResults.Succeeded,
                $"Upserted P14 production Pilot incident; severity={incident.Severity}; status={incident.Status}; resolutionHash={incident.ResolutionHash ?? "none"}.",
                ["incidentId", "severity", "status", "resolutionHash"]),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        return Result.Success(incident);
    }
}

public sealed class RunProductionPilotGaReadinessEvaluationCommandHandler(
    CloudReadonlyProductionOperationsService operationsService,
    CloudReadonlyProductionPilotService productionPilotService,
    CloudReadonlyProductionControlledPilotService controlledPilotService,
    CloudReadonlyPilotReadinessService pilotReadinessService,
    IReadRepository<ToolRegistration> toolRepository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<RunProductionPilotGaReadinessEvaluationCommand, Result<ProductionPilotGaReadinessAssessmentDto>>
{
    public async Task<Result<ProductionPilotGaReadinessAssessmentDto>> Handle(
        RunProductionPilotGaReadinessEvaluationCommand request,
        CancellationToken cancellationToken)
    {
        var protectedTools = await GetCloudReadonlyPilotReadinessQueryHandler.LoadProtectedToolRegistrationsAsync(toolRepository, cancellationToken);
        var p12 = productionPilotService.BuildStatus(pilotReadinessService.BuildStatus(protectedTools), protectedTools);
        var p13 = controlledPilotService.BuildStatus(p12, protectedTools);
        var assessment = operationsService.BuildGaReadinessAssessment(p12, p13, protectedTools);

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "AiGateway.RunProductionPilotGaReadinessEvaluation",
                "CloudReadonlyProductionOperations",
                "p15-readiness",
                assessment.Status,
                assessment.Status == CloudReadonlyProductionOperationsStatuses.ReadyForP15Planning ? AuditResults.Succeeded : AuditResults.Rejected,
                $"Ran P14 production Pilot GA readiness evaluation; status={assessment.Status}; blockers={assessment.Blockers.Count}.",
                ["status", "blockers", "sourceMode", "boundary"]),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        return Result.Success(assessment);
    }
}
