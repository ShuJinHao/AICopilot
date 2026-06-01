using AICopilot.AiGatewayService.TrialOperations;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.CloudReadiness;

public static class CloudReadonlyProductionPilotStatuses
{
    public const string Disabled = "Disabled";
    public const string NotConfigured = "NotConfigured";
    public const string P11GateRequired = "P11GateRequired";
    public const string WindowPendingApproval = "WindowPendingApproval";
    public const string WindowNotStarted = "WindowNotStarted";
    public const string Ready = "Ready";
    public const string Paused = "Paused";
    public const string Expired = "Expired";
    public const string EmergencyStopped = "EmergencyStopped";
    public const string Blocked = "Blocked";
    public const string Failed = "Failed";
    public const string Completed = "Completed";
}

public static class CloudReadonlyProductionPilotWindowStatuses
{
    public const string PendingApproval = "PendingApproval";
    public const string Approved = "Approved";
    public const string Paused = "Paused";
    public const string Completed = "Completed";
    public const string EmergencyStopped = "EmergencyStopped";
}

public sealed record CloudReadonlyProductionPilotStatusDto(
    string Status,
    bool Enabled,
    string? PilotWindowId,
    string? WindowStatus,
    IReadOnlyCollection<string> AllowedEndpointCodes,
    string ApprovalStatus,
    bool ToolVisible,
    bool ToolExecutable,
    DateTimeOffset? LastRunAt,
    IReadOnlyCollection<string> Blockers,
    IReadOnlyCollection<string> Warnings);

public sealed record CloudReadonlyProductionPilotWindowDto(
    string WindowId,
    string Name,
    string Status,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    IReadOnlyCollection<string> AllowedEndpointCodes,
    int MaxTimeRangeDays,
    int MaxRows,
    int TimeoutMs,
    string OwnerDepartment,
    string ApprovalPolicy,
    string RollbackPolicy);

public sealed record CloudProductionPilotTimeRangeDto(DateTimeOffset? From, DateTimeOffset? To);

public sealed record CloudProductionPilotQueryResultDto(
    string EndpointCode,
    string SourceType,
    string SourceMode,
    bool IsProductionData,
    bool IsSandbox,
    bool IsSimulation,
    string SourceLabel,
    string Boundary,
    string PilotWindowId,
    string QueryHash,
    string ResultHash,
    int RowCount,
    bool IsTruncated,
    IReadOnlyCollection<IReadOnlyDictionary<string, object?>> Rows,
    DateTimeOffset ExecutedAt,
    long DurationMs,
    string ApprovalStatus);

public sealed record CloudReadonlyProductionPilotScenarioResultDto(
    string ScenarioId,
    string ScenarioTitle,
    string Status,
    CloudProductionPilotQueryResultDto QueryResult,
    IReadOnlyCollection<string> ArtifactTypes,
    string Boundary = CloudReadonlyProductionPilotMarkers.Boundary);

[AuthorizeRequirement(TrialOperationsPermissions.Read)]
public sealed record GetCloudReadonlyProductionPilotStatusQuery
    : IQuery<Result<CloudReadonlyProductionPilotStatusDto>>;

[AuthorizeRequirement(TrialOperationsPermissions.Manage)]
public sealed record CreateCloudReadonlyProductionPilotWindowCommand(
    string? Name = null,
    DateTimeOffset? StartAt = null,
    DateTimeOffset? EndAt = null,
    IReadOnlyCollection<string>? AllowedEndpointCodes = null,
    int? MaxTimeRangeDays = null,
    int? MaxRows = null,
    int? TimeoutMs = null,
    string? OwnerDepartment = null,
    string? ApprovalPolicy = null,
    string? RollbackPolicy = null) : ICommand<Result<CloudReadonlyProductionPilotWindowDto>>;

[AuthorizeRequirement(TrialOperationsPermissions.Manage)]
public sealed record UpdateCloudReadonlyProductionPilotWindowStatusCommand(
    string WindowId,
    string Status) : ICommand<Result<CloudReadonlyProductionPilotWindowDto>>;

[AuthorizeRequirement(TrialOperationsPermissions.AuditView)]
public sealed record RunCloudReadonlyProductionPilotGateEvaluationCommand
    : ICommand<Result<CloudReadonlyProductionPilotStatusDto>>;

[AuthorizeRequirement("AiGateway.RunAgentTask")]
public sealed record RunCloudReadonlyProductionPilotScenarioCommand(
    string ScenarioId,
    IReadOnlyCollection<string>? ArtifactTypes = null,
    string? PilotWindowId = null,
    CloudProductionPilotTimeRangeDto? TimeRange = null,
    int MaxRows = 20,
    int TimeoutMs = 5000) : ICommand<Result<CloudReadonlyProductionPilotScenarioResultDto>>;

public interface ICloudReadonlyProductionPilotStore
{
    void SaveWindow(CloudReadonlyProductionPilotWindowDto window);

    CloudReadonlyProductionPilotWindowDto? GetWindow(string windowId);

    CloudReadonlyProductionPilotWindowDto? LatestWindow();

    void SaveRun(CloudReadonlyProductionPilotScenarioResultDto result);

    IReadOnlyCollection<CloudReadonlyProductionPilotScenarioResultDto> ListRuns();
}
