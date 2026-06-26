using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.CloudReadiness;

public static class CloudReadonlySandboxAgentTrialStatuses
{
    public const string Disabled = "Disabled";
    public const string Ready = "Ready";
    public const string SandboxSmokeRequired = "SandboxSmokeRequired";
    public const string Blocked = "Blocked";
    public const string Failed = "Failed";
    public const string Completed = "Completed";
}

public sealed record CloudReadonlySandboxAgentTrialStatusDto(
    string Status,
    string SandboxSmokeStatus,
    bool TrialEnabled,
    IReadOnlyCollection<string> AvailableScenarioIds,
    bool ToolVisible,
    bool ToolExecutable,
    DateTimeOffset? LastTrialAt,
    IReadOnlyCollection<string> Errors,
    IReadOnlyCollection<string> Warnings,
    string Boundary = CloudReadonlySandboxAgentTrialMarkers.Boundary);

public sealed record CloudSandboxQueryResultDto(
    string EndpointCode,
    string SourceType,
    string SourceMode,
    bool IsSandbox,
    bool IsSimulation,
    string SourceLabel,
    string Boundary,
    string QueryHash,
    string ResultHash,
    int RowCount,
    bool IsTruncated,
    IReadOnlyCollection<IReadOnlyDictionary<string, object?>> Rows,
    DateTimeOffset ExecutedAt,
    long DurationMs,
    string ApprovalStatus);

public sealed record CloudReadonlySandboxAgentTrialResultDto(
    string ScenarioId,
    string ScenarioTitle,
    string Status,
    CloudSandboxQueryResultDto QueryResult,
    IReadOnlyCollection<string> ArtifactTypes,
    string Boundary = CloudReadonlySandboxAgentTrialMarkers.Boundary);

[AuthorizeRequirement("AiGateway.ToolRegistry.Read")]
public sealed record GetCloudReadonlySandboxAgentTrialStatusQuery
    : IQuery<Result<CloudReadonlySandboxAgentTrialStatusDto>>;

[AuthorizeRequirement("AiGateway.RunAgentTask")]
public sealed record RunCloudReadonlySandboxAgentTrialCommand(
    string ScenarioId,
    IReadOnlyCollection<string>? ArtifactTypes = null,
    int MaxRows = 20,
    int TimeoutMs = 5000,
    string TrialMode = CloudReadonlySandboxControlledTrialMarkers.FixedScenarioTrialMode,
    string? IntentId = null) : ICommand<Result<CloudReadonlySandboxAgentTrialResultDto>>;

public interface ICloudReadonlySandboxAgentTrialHistoryStore
{
    void Save(CloudReadonlySandboxAgentTrialResultDto result);

    IReadOnlyCollection<CloudReadonlySandboxAgentTrialResultDto> List();
}
