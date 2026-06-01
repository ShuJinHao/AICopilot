using AICopilot.AiGatewayService.TrialOperations;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
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

public sealed record ProductionPilotRowsRetentionPolicyDto(
    string PersistenceMode,
    int RuntimeRowsTtlMinutes,
    bool LedgerStoresRows,
    bool LedgerStoresRawPayload,
    bool ReportsReturnRows,
    string ArtifactUsePolicy,
    string DownloadPolicy,
    string AuditSummary);

public sealed record CloudReadonlyProductionOperationsStatusDto(
    string Status,
    string P12PilotStatus,
    string P13ControlledPilotStatus,
    bool OperationsStorePersisted,
    bool P12PilotStorePersisted,
    bool P13ControlledPilotStorePersisted,
    bool ArtifactRefsBackfillEnabled,
    ProductionPilotRowsRetentionPolicyDto RowsRetentionPolicy,
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
