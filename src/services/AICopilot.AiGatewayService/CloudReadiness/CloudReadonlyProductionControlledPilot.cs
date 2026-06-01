using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.TrialOperations;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.CloudReadiness;

public static class CloudReadonlyProductionControlledPilotStatuses
{
    public const string Disabled = "Disabled";
    public const string FreeGoalDisabled = "FreeGoalDisabled";
    public const string P12GateRequired = "P12GateRequired";
    public const string Ready = "Ready";
    public const string EmergencyStopped = "EmergencyStopped";
    public const string Blocked = "Blocked";
    public const string Failed = "Failed";
    public const string Completed = "Completed";
}

public sealed record CloudReadonlyProductionControlledPilotStatusDto(
    string Status,
    bool Enabled,
    string P12GateStatus,
    string? PilotWindowId,
    string? WindowStatus,
    bool FreeGoalEnabled,
    IReadOnlyCollection<string> AllowedEndpointCodes,
    bool ToolVisible,
    bool ToolExecutable,
    DateTimeOffset? LastRunAt,
    IReadOnlyCollection<string> Blockers,
    IReadOnlyCollection<string> Warnings,
    string Boundary = CloudReadonlyProductionControlledPilotMarkers.Boundary);

public sealed record CloudProductionGoalTimeRangeDto(DateTimeOffset? From = null, DateTimeOffset? To = null);

public sealed record CloudProductionGoalIntentDto(
    string IntentId,
    string GoalHash,
    IReadOnlyCollection<string> EndpointCodes,
    CloudProductionGoalTimeRangeDto TimeRange,
    int MaxRows,
    IReadOnlyCollection<string> ArtifactTypes,
    string AnalysisType,
    IReadOnlyCollection<string> Warnings,
    IReadOnlyCollection<string> RejectedReasons,
    bool RequiresToolApproval,
    bool RequiresFinalApproval);

public sealed record CloudReadonlyProductionControlledPlanDto(
    AgentTaskDto Task,
    CloudProductionGoalIntentDto Intent);

public sealed record CloudProductionControlledQueryResultDto(
    string EndpointCode,
    string SourceType,
    string SourceMode,
    bool IsProductionData,
    bool IsSandbox,
    bool IsSimulation,
    string SourceLabel,
    string Boundary,
    string PilotWindowId,
    string IntentId,
    string QueryHash,
    string ResultHash,
    int RowCount,
    bool IsTruncated,
    IReadOnlyCollection<IReadOnlyDictionary<string, object?>> Rows,
    DateTimeOffset ExecutedAt,
    long DurationMs,
    string ApprovalStatus);

public sealed record CloudReadonlyProductionControlledPilotResultDto(
    string IntentId,
    string AnalysisType,
    string Status,
    CloudProductionControlledQueryResultDto QueryResult,
    IReadOnlyCollection<string> ArtifactTypes,
    string Boundary = CloudReadonlyProductionControlledPilotMarkers.Boundary);

[AuthorizeRequirement(TrialOperationsPermissions.Read)]
public sealed record GetCloudReadonlyProductionControlledPilotStatusQuery
    : IQuery<Result<CloudReadonlyProductionControlledPilotStatusDto>>;

[AuthorizeRequirement("AiGateway.PlanAgentTask")]
public sealed record CreateCloudReadonlyProductionControlledPlanCommand(
    Guid SessionId,
    string Goal,
    Guid? ModelId = null,
    IReadOnlyCollection<string>? ArtifactTypes = null,
    CloudProductionGoalTimeRangeDto? TimeRange = null,
    int? MaxRows = null,
    string? PlannerMode = null) : ICommand<Result<CloudReadonlyProductionControlledPlanDto>>;

[AuthorizeRequirement("AiGateway.RunAgentTask")]
public sealed record RunCloudReadonlyProductionControlledPilotCommand(
    string IntentId,
    IReadOnlyCollection<string>? ArtifactTypes = null,
    int MaxRows = 20,
    int TimeoutMs = 5000) : ICommand<Result<CloudReadonlyProductionControlledPilotResultDto>>;

public interface ICloudReadonlyProductionControlledPilotStore
{
    void SaveIntent(CloudProductionGoalIntentDto intent);

    CloudProductionGoalIntentDto? GetIntent(string intentId);

    void SaveRun(CloudReadonlyProductionControlledPilotResultDto result);

    IReadOnlyCollection<CloudReadonlyProductionControlledPilotResultDto> ListRuns();
}
