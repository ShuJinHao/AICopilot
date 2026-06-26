using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.ProductionOperations;

public sealed class ProductionPilotRun
    : BaseEntity<ProductionPilotRunId>, IAggregateRoot<ProductionPilotRunId>
{
    private ProductionPilotRun()
    {
    }

    public ProductionPilotRun(
        string runId,
        string scenarioId,
        string scenarioTitle,
        string status,
        string endpointCode,
        string sourceType,
        string sourceMode,
        bool isProductionData,
        bool isSandbox,
        bool isSimulation,
        string sourceLabel,
        string boundary,
        string pilotWindowId,
        string queryHash,
        string resultHash,
        int rowCount,
        bool isTruncated,
        DateTimeOffset executedAt,
        long durationMs,
        string approvalStatus,
        IReadOnlyCollection<string>? artifactTypes,
        DateTimeOffset nowUtc)
    {
        Id = ProductionPilotRunId.New();
        CreatedAt = nowUtc;
        Update(
            runId,
            scenarioId,
            scenarioTitle,
            status,
            endpointCode,
            sourceType,
            sourceMode,
            isProductionData,
            isSandbox,
            isSimulation,
            sourceLabel,
            boundary,
            pilotWindowId,
            queryHash,
            resultHash,
            rowCount,
            isTruncated,
            executedAt,
            durationMs,
            approvalStatus,
            artifactTypes,
            nowUtc);
    }

    public string RunId { get; private set; } = string.Empty;

    public string ScenarioId { get; private set; } = string.Empty;

    public string ScenarioTitle { get; private set; } = string.Empty;

    public string Status { get; private set; } = string.Empty;

    public string EndpointCode { get; private set; } = string.Empty;

    public string SourceType { get; private set; } = string.Empty;

    public string SourceMode { get; private set; } = string.Empty;

    public bool IsProductionData { get; private set; }

    public bool IsSandbox { get; private set; }

    public bool IsSimulation { get; private set; }

    public string SourceLabel { get; private set; } = string.Empty;

    public string Boundary { get; private set; } = string.Empty;

    public string PilotWindowId { get; private set; } = string.Empty;

    public string QueryHash { get; private set; } = string.Empty;

    public string ResultHash { get; private set; } = string.Empty;

    public int RowCount { get; private set; }

    public bool IsTruncated { get; private set; }

    public DateTimeOffset ExecutedAt { get; private set; }

    public long DurationMs { get; private set; }

    public string ApprovalStatus { get; private set; } = string.Empty;

    public string[] ArtifactTypes { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(
        string runId,
        string scenarioId,
        string scenarioTitle,
        string status,
        string endpointCode,
        string sourceType,
        string sourceMode,
        bool isProductionData,
        bool isSandbox,
        bool isSimulation,
        string sourceLabel,
        string boundary,
        string pilotWindowId,
        string queryHash,
        string resultHash,
        int rowCount,
        bool isTruncated,
        DateTimeOffset executedAt,
        long durationMs,
        string approvalStatus,
        IReadOnlyCollection<string>? artifactTypes,
        DateTimeOffset nowUtc)
    {
        RunId = ProductionPilotEmergencyStopState.NormalizeRequired(runId, nameof(runId), 200);
        ScenarioId = ProductionPilotEmergencyStopState.NormalizeRequired(scenarioId, nameof(scenarioId), 160);
        ScenarioTitle = ProductionPilotEmergencyStopState.NormalizeRequired(scenarioTitle, nameof(scenarioTitle), 200);
        Status = ProductionPilotEmergencyStopState.NormalizeRequired(status, nameof(status), 80);
        EndpointCode = ProductionPilotEmergencyStopState.NormalizeRequired(endpointCode, nameof(endpointCode), 120);
        SourceType = ProductionPilotEmergencyStopState.NormalizeRequired(sourceType, nameof(sourceType), 80);
        SourceMode = ProductionPilotEmergencyStopState.NormalizeRequired(sourceMode, nameof(sourceMode), 80);
        IsProductionData = isProductionData;
        IsSandbox = isSandbox;
        IsSimulation = isSimulation;
        SourceLabel = ProductionPilotEmergencyStopState.NormalizeRequired(sourceLabel, nameof(sourceLabel), 200);
        Boundary = ProductionPilotEmergencyStopState.NormalizeRequired(boundary, nameof(boundary), 120);
        PilotWindowId = ProductionPilotEmergencyStopState.NormalizeRequired(pilotWindowId, nameof(pilotWindowId), 160);
        QueryHash = ProductionPilotEmergencyStopState.NormalizeRequired(queryHash, nameof(queryHash), 128);
        ResultHash = ProductionPilotEmergencyStopState.NormalizeRequired(resultHash, nameof(resultHash), 128);
        RowCount = Math.Max(0, rowCount);
        IsTruncated = isTruncated;
        ExecutedAt = executedAt;
        DurationMs = Math.Max(0, durationMs);
        ApprovalStatus = ProductionPilotEmergencyStopState.NormalizeRequired(approvalStatus, nameof(approvalStatus), 80);
        ArtifactTypes = ProductionPilotEmergencyStopState.NormalizeStrings(artifactTypes, 80);
        UpdatedAt = nowUtc;
    }
}
