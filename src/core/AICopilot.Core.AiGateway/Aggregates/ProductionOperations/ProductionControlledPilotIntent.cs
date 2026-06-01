using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.ProductionOperations;

public sealed class ProductionControlledPilotIntent
    : BaseEntity<ProductionControlledPilotIntentId>, IAggregateRoot<ProductionControlledPilotIntentId>
{
    private ProductionControlledPilotIntent()
    {
    }

    public ProductionControlledPilotIntent(
        string intentId,
        string goalHash,
        IReadOnlyCollection<string>? endpointCodes,
        DateTimeOffset? timeRangeFrom,
        DateTimeOffset? timeRangeTo,
        int maxRows,
        IReadOnlyCollection<string>? artifactTypes,
        string analysisType,
        IReadOnlyCollection<string>? warnings,
        IReadOnlyCollection<string>? rejectedReasons,
        bool requiresToolApproval,
        bool requiresFinalApproval,
        DateTimeOffset nowUtc)
    {
        Id = ProductionControlledPilotIntentId.New();
        CreatedAt = nowUtc;
        Update(
            intentId,
            goalHash,
            endpointCodes,
            timeRangeFrom,
            timeRangeTo,
            maxRows,
            artifactTypes,
            analysisType,
            warnings,
            rejectedReasons,
            requiresToolApproval,
            requiresFinalApproval,
            nowUtc);
    }

    public string IntentId { get; private set; } = string.Empty;

    public string GoalHash { get; private set; } = string.Empty;

    public string[] EndpointCodes { get; private set; } = [];

    public DateTimeOffset? TimeRangeFrom { get; private set; }

    public DateTimeOffset? TimeRangeTo { get; private set; }

    public int MaxRows { get; private set; }

    public string[] ArtifactTypes { get; private set; } = [];

    public string AnalysisType { get; private set; } = string.Empty;

    public string[] Warnings { get; private set; } = [];

    public string[] RejectedReasons { get; private set; } = [];

    public bool RequiresToolApproval { get; private set; }

    public bool RequiresFinalApproval { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(
        string intentId,
        string goalHash,
        IReadOnlyCollection<string>? endpointCodes,
        DateTimeOffset? timeRangeFrom,
        DateTimeOffset? timeRangeTo,
        int maxRows,
        IReadOnlyCollection<string>? artifactTypes,
        string analysisType,
        IReadOnlyCollection<string>? warnings,
        IReadOnlyCollection<string>? rejectedReasons,
        bool requiresToolApproval,
        bool requiresFinalApproval,
        DateTimeOffset nowUtc)
    {
        IntentId = ProductionPilotEmergencyStopState.NormalizeRequired(intentId, nameof(intentId), 200);
        GoalHash = ProductionPilotEmergencyStopState.NormalizeRequired(goalHash, nameof(goalHash), 128);
        EndpointCodes = ProductionPilotEmergencyStopState.NormalizeStrings(endpointCodes, 120);
        TimeRangeFrom = timeRangeFrom;
        TimeRangeTo = timeRangeTo;
        MaxRows = Math.Max(1, maxRows);
        ArtifactTypes = ProductionPilotEmergencyStopState.NormalizeStrings(artifactTypes, 80);
        AnalysisType = ProductionPilotEmergencyStopState.NormalizeRequired(analysisType, nameof(analysisType), 120);
        Warnings = ProductionPilotEmergencyStopState.NormalizeStrings(warnings, 500);
        RejectedReasons = ProductionPilotEmergencyStopState.NormalizeStrings(rejectedReasons, 500);
        RequiresToolApproval = requiresToolApproval;
        RequiresFinalApproval = requiresFinalApproval;
        UpdatedAt = nowUtc;
    }
}
