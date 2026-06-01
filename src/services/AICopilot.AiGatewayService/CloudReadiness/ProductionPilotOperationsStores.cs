using System.Text.Json;
using AICopilot.Core.AiGateway.Aggregates.ProductionOperations;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Repository;

namespace AICopilot.AiGatewayService.CloudReadiness;

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
    : RepositoryCloudReadinessStoreBase, IProductionPilotOperationsStore
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

}
