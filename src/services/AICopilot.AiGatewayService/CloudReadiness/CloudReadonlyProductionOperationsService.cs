using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.CloudReadiness;

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
            productionPilotStore is not InMemoryCloudReadonlyProductionPilotStore,
            controlledPilotStore is not InMemoryCloudReadonlyProductionControlledPilotStore,
            ArtifactRefsBackfillEnabled: true,
            RowsRetentionPolicy: RowsRetentionPolicy,
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

    public static ProductionPilotRowsRetentionPolicyDto RowsRetentionPolicy { get; } = new(
        "HashOnly",
        RuntimeRowsTtlMinutes: 60,
        LedgerStoresRows: false,
        LedgerStoresRawPayload: false,
        ReportsReturnRows: false,
        "Artifacts may use only approved, truncated, source-marked runtime rows; operations evidence stores hashes and counts only.",
        "Downloads are allowed only through Artifact Workspace permission checks after final approval.",
        "Operations audit records sourceMode, endpoint, rowCount, truncation, query/result hash, approval status, and artifact refs; no raw rows or payload.");

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

    public IReadOnlyCollection<string> BackfillFinalArtifactRefs(Guid taskId, IReadOnlyCollection<Artifact> finalArtifacts)
    {
        var warnings = new List<string>();
        var ledgers = operationsStore.ListRunLedgers();
        foreach (var artifact in finalArtifacts.Where(IsProductionPilotArtifact))
        {
            var matches = ledgers
                .Where(ledger => MatchesArtifact(ledger, artifact))
                .ToArray();
            if (matches.Length == 0)
            {
                warnings.Add($"Missing ProductionPilotRunLedger for final artifact {artifact.Id.Value} with sourceMode={artifact.SourceMode}, queryHash={artifact.QueryHash}, resultHash={artifact.ResultHash}.");
                continue;
            }

            foreach (var ledger in matches)
            {
                var artifactIds = ledger.ArtifactIds
                    .Append(artifact.Id.Value)
                    .Where(value => value != Guid.Empty)
                    .Distinct()
                    .OrderBy(value => value)
                    .ToArray();
                operationsStore.UpsertRunLedger(
                    ledger with
                    {
                        TaskId = taskId,
                        ArtifactIds = artifactIds,
                        ApprovalStatus = "Finalized"
                    },
                    DateTimeOffset.UtcNow);
            }
        }

        return warnings;
    }

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

    private static bool IsProductionPilotArtifact(Artifact artifact) =>
        artifact.Status == ArtifactStatus.Final &&
        (string.Equals(artifact.SourceMode, CloudReadonlyProductionPilotMarkers.SourceMode, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(artifact.SourceMode, CloudReadonlyProductionControlledPilotMarkers.SourceMode, StringComparison.OrdinalIgnoreCase));

    private static bool MatchesArtifact(ProductionPilotRunLedgerDto ledger, Artifact artifact) =>
        string.Equals(ledger.SourceMode, artifact.SourceMode, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(ledger.Boundary, artifact.Boundary, StringComparison.OrdinalIgnoreCase) &&
        (string.Equals(ledger.QueryHash, artifact.QueryHash, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(ledger.ResultHash, artifact.ResultHash, StringComparison.OrdinalIgnoreCase));
}
