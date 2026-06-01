using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.CloudReadiness;

public static class CloudReadonlySandboxControlledTrialStatuses
{
    public const string Disabled = "Disabled";
    public const string Ready = "Ready";
    public const string FreeGoalDisabled = "FreeGoalDisabled";
    public const string SandboxSmokeRequired = "SandboxSmokeRequired";
    public const string FixedTrialRequired = "FixedTrialRequired";
    public const string Blocked = "Blocked";
    public const string Failed = "Failed";
    public const string Completed = "Completed";
}

public sealed record CloudReadonlySandboxControlledTrialStatusDto(
    string Status,
    string SandboxSmokeStatus,
    string FixedTrialStatus,
    bool ControlledTrialEnabled,
    bool FreeGoalEnabled,
    bool ToolVisible,
    bool ToolExecutable,
    DateTimeOffset? LastTrialAt,
    IReadOnlyCollection<string> Errors,
    IReadOnlyCollection<string> Warnings,
    string Boundary = CloudReadonlySandboxControlledTrialMarkers.Boundary);

public sealed record CloudSandboxGoalTimeRangeDto(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null);

public sealed record CloudSandboxGoalIntentDto(
    string IntentId,
    string GoalHash,
    IReadOnlyCollection<string> EndpointCodes,
    CloudSandboxGoalTimeRangeDto TimeRange,
    int MaxRows,
    IReadOnlyCollection<string> ArtifactTypes,
    string AnalysisType,
    IReadOnlyCollection<string> Warnings,
    IReadOnlyCollection<string> RejectedReasons,
    bool RequiresToolApproval,
    bool RequiresFinalApproval);

public sealed record CloudReadonlySandboxControlledPlanDto(
    AgentTaskDto Task,
    CloudSandboxGoalIntentDto Intent);

[AuthorizeRequirement("AiGateway.ToolRegistry.Read")]
public sealed record GetCloudReadonlySandboxControlledTrialStatusQuery
    : IQuery<Result<CloudReadonlySandboxControlledTrialStatusDto>>;

[AuthorizeRequirement("AiGateway.PlanAgentTask")]
public sealed record CreateCloudReadonlySandboxControlledPlanCommand(
    Guid SessionId,
    string Goal,
    Guid? ModelId = null,
    IReadOnlyCollection<string>? ArtifactTypes = null,
    CloudSandboxGoalTimeRangeDto? TimeRange = null,
    int? MaxRows = null,
    string? PlannerMode = null) : ICommand<Result<CloudReadonlySandboxControlledPlanDto>>;

public interface ICloudReadonlySandboxControlledTrialIntentStore
{
    void Save(CloudSandboxGoalIntentDto intent);

    CloudSandboxGoalIntentDto? Get(string intentId);
}
