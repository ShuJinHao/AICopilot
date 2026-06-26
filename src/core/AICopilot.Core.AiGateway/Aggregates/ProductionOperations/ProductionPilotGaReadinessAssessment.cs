using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.ProductionOperations;

public sealed class ProductionPilotGaReadinessAssessment
    : BaseEntity<ProductionPilotGaReadinessAssessmentId>, IAggregateRoot<ProductionPilotGaReadinessAssessmentId>
{
    private ProductionPilotGaReadinessAssessment()
    {
    }

    public ProductionPilotGaReadinessAssessment(
        string status,
        string checksJson,
        IReadOnlyCollection<string>? blockers,
        IReadOnlyCollection<string>? warnings,
        int totalRuns,
        int succeededRuns,
        int failedRuns,
        int rejectedRuns,
        int timeoutRuns,
        int truncatedRuns,
        int totalRows,
        int finalArtifactCount,
        int openIncidentCount,
        string endpointDistributionJson,
        DateTimeOffset generatedAt,
        DateTimeOffset nowUtc)
    {
        Id = ProductionPilotGaReadinessAssessmentId.New();
        Status = ProductionPilotEmergencyStopState.NormalizeRequired(status, nameof(status), 80);
        ChecksJson = ProductionPilotEmergencyStopState.NormalizeRequired(checksJson, nameof(checksJson), 8000);
        Blockers = ProductionPilotEmergencyStopState.NormalizeStrings(blockers, 500);
        Warnings = ProductionPilotEmergencyStopState.NormalizeStrings(warnings, 500);
        TotalRuns = Math.Max(0, totalRuns);
        SucceededRuns = Math.Max(0, succeededRuns);
        FailedRuns = Math.Max(0, failedRuns);
        RejectedRuns = Math.Max(0, rejectedRuns);
        TimeoutRuns = Math.Max(0, timeoutRuns);
        TruncatedRuns = Math.Max(0, truncatedRuns);
        TotalRows = Math.Max(0, totalRows);
        FinalArtifactCount = Math.Max(0, finalArtifactCount);
        OpenIncidentCount = Math.Max(0, openIncidentCount);
        EndpointDistributionJson = ProductionPilotEmergencyStopState.NormalizeRequired(endpointDistributionJson, nameof(endpointDistributionJson), 8000);
        GeneratedAt = generatedAt;
        CreatedAt = nowUtc;
    }

    public string Status { get; private set; } = string.Empty;

    public string ChecksJson { get; private set; } = "[]";

    public string[] Blockers { get; private set; } = [];

    public string[] Warnings { get; private set; } = [];

    public int TotalRuns { get; private set; }

    public int SucceededRuns { get; private set; }

    public int FailedRuns { get; private set; }

    public int RejectedRuns { get; private set; }

    public int TimeoutRuns { get; private set; }

    public int TruncatedRuns { get; private set; }

    public int TotalRows { get; private set; }

    public int FinalArtifactCount { get; private set; }

    public int OpenIncidentCount { get; private set; }

    public string EndpointDistributionJson { get; private set; } = "{}";

    public DateTimeOffset GeneratedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
