using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.ProductionOperations;

public sealed class ProductionPilotRunLedger
    : BaseEntity<ProductionPilotRunLedgerId>, IAggregateRoot<ProductionPilotRunLedgerId>
{
    private ProductionPilotRunLedger()
    {
    }

    public ProductionPilotRunLedger(
        string runId,
        Guid? taskId,
        string sourceMode,
        string boundary,
        string trialMode,
        string pilotWindowId,
        string? intentId,
        string endpointCode,
        IReadOnlyCollection<Guid>? artifactIds,
        string approvalStatus,
        string status,
        long durationMs,
        int rowCount,
        bool isTruncated,
        string queryHash,
        string resultHash,
        DateTimeOffset executedAt,
        DateTimeOffset nowUtc)
    {
        Id = ProductionPilotRunLedgerId.New();
        CreatedAt = nowUtc;
        Update(
            runId,
            taskId,
            sourceMode,
            boundary,
            trialMode,
            pilotWindowId,
            intentId,
            endpointCode,
            artifactIds,
            approvalStatus,
            status,
            durationMs,
            rowCount,
            isTruncated,
            queryHash,
            resultHash,
            executedAt,
            nowUtc);
    }

    public string RunId { get; private set; } = string.Empty;

    public Guid? TaskId { get; private set; }

    public string SourceMode { get; private set; } = string.Empty;

    public string Boundary { get; private set; } = string.Empty;

    public string TrialMode { get; private set; } = string.Empty;

    public string PilotWindowId { get; private set; } = string.Empty;

    public string? IntentId { get; private set; }

    public string EndpointCode { get; private set; } = string.Empty;

    public Guid[] ArtifactIds { get; private set; } = [];

    public string ApprovalStatus { get; private set; } = string.Empty;

    public string Status { get; private set; } = string.Empty;

    public long DurationMs { get; private set; }

    public int RowCount { get; private set; }

    public bool IsTruncated { get; private set; }

    public string QueryHash { get; private set; } = string.Empty;

    public string ResultHash { get; private set; } = string.Empty;

    public DateTimeOffset ExecutedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(
        string runId,
        Guid? taskId,
        string sourceMode,
        string boundary,
        string trialMode,
        string pilotWindowId,
        string? intentId,
        string endpointCode,
        IReadOnlyCollection<Guid>? artifactIds,
        string approvalStatus,
        string status,
        long durationMs,
        int rowCount,
        bool isTruncated,
        string queryHash,
        string resultHash,
        DateTimeOffset executedAt,
        DateTimeOffset nowUtc)
    {
        RunId = ProductionPilotEmergencyStopState.NormalizeRequired(runId, nameof(runId), 200);
        TaskId = taskId == Guid.Empty ? null : taskId;
        SourceMode = ProductionPilotEmergencyStopState.NormalizeRequired(sourceMode, nameof(sourceMode), 80);
        Boundary = ProductionPilotEmergencyStopState.NormalizeRequired(boundary, nameof(boundary), 120);
        TrialMode = ProductionPilotEmergencyStopState.NormalizeRequired(trialMode, nameof(trialMode), 120);
        PilotWindowId = ProductionPilotEmergencyStopState.NormalizeRequired(pilotWindowId, nameof(pilotWindowId), 160);
        IntentId = ProductionPilotEmergencyStopState.NormalizeOptional(intentId, 200);
        EndpointCode = ProductionPilotEmergencyStopState.NormalizeRequired(endpointCode, nameof(endpointCode), 120);
        ArtifactIds = ProductionPilotEmergencyStopState.NormalizeGuids(artifactIds);
        ApprovalStatus = ProductionPilotEmergencyStopState.NormalizeRequired(approvalStatus, nameof(approvalStatus), 80);
        Status = ProductionPilotEmergencyStopState.NormalizeRequired(status, nameof(status), 80);
        DurationMs = Math.Max(0, durationMs);
        RowCount = Math.Max(0, rowCount);
        IsTruncated = isTruncated;
        QueryHash = ProductionPilotEmergencyStopState.NormalizeRequired(queryHash, nameof(queryHash), 128);
        ResultHash = ProductionPilotEmergencyStopState.NormalizeRequired(resultHash, nameof(resultHash), 128);
        ExecutedAt = executedAt;
        UpdatedAt = nowUtc;
    }
}
