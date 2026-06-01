using AICopilot.Core.AiGateway.Aggregates.TrialOperations;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.TrialOperations;

public static class TrialOperationsPermissions
{
    public const string Read = "AiGateway.TrialOperations.Read";
    public const string Manage = "AiGateway.TrialOperations.Manage";
    public const string AuditView = "AiGateway.TrialOperations.AuditView";
}

public sealed record TrialCampaignSummaryDto(
    int ScenarioRunCount,
    int PassedRunCount,
    int FailedRunCount,
    int BlockedRunCount,
    int FinalArtifactCount,
    int PendingApprovalCount,
    int UnresolvedRiskCount,
    int QueryHashCount,
    int ResultHashCount);

public sealed record TrialCampaignDto(
    Guid CampaignId,
    string Name,
    string Status,
    IReadOnlyCollection<string> AllowedSourceModes,
    string? OwnerDepartment,
    DateTimeOffset? StartAt,
    DateTimeOffset? EndAt,
    TrialCampaignSummaryDto Summary,
    DateTimeOffset CreatedAt)
{
    public string? Description { get; init; }

    public string ReadinessStatus { get; init; } = PilotReadinessStatus.NotEvaluated.ToString();

    public DateTimeOffset UpdatedAt { get; init; }

    public IReadOnlyCollection<TrialScenarioRunDto> ScenarioRuns { get; init; } = [];

    public IReadOnlyCollection<TrialRiskIssueDto> Risks { get; init; } = [];
}

public sealed record TrialScenarioRunDto(
    Guid RunId,
    Guid CampaignId,
    string ScenarioId,
    string TrialMode,
    string SourceMode,
    string? Boundary,
    Guid TaskId,
    IReadOnlyCollection<Guid> ArtifactIds,
    IReadOnlyCollection<string> QueryHashes,
    IReadOnlyCollection<string> ResultHashes,
    string ApprovalStatus,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

public sealed record TrialRiskIssueDto(
    Guid IssueId,
    Guid CampaignId,
    string Severity,
    string Category,
    string Status,
    string? Owner,
    string? SourceRef,
    string? ResolutionHash,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PilotReadinessCheckDto(
    string Code,
    string Label,
    string Status,
    bool IsBlocking,
    string Message);

public sealed record PilotReadinessMetricsDto(
    int ScenarioRuns,
    int PassedRuns,
    int FinalArtifacts,
    int PendingApprovals,
    int UnresolvedRisks,
    int QueryHashSamples,
    int ResultHashSamples);

public sealed record PilotReadinessAssessmentDto(
    Guid CampaignId,
    string Status,
    IReadOnlyCollection<PilotReadinessCheckDto> Checks,
    IReadOnlyCollection<string> Blockers,
    IReadOnlyCollection<string> Warnings,
    PilotReadinessMetricsDto Metrics,
    DateTimeOffset GeneratedAt);

public sealed record TrialEvidenceMetricDto(string Code, string Label, int Value);

public sealed record TrialEvidenceItemDto(
    string EvidenceType,
    string SourceMode,
    string? Boundary,
    string Status,
    IReadOnlyCollection<string> HashSamples,
    string ReferenceId);

public sealed record TrialEvidencePackageDto(
    Guid CampaignId,
    string ReadinessStatus,
    IReadOnlyCollection<TrialEvidenceMetricDto> Metrics,
    IReadOnlyCollection<TrialEvidenceItemDto> EvidenceItems,
    IReadOnlyCollection<TrialRiskIssueDto> UnresolvedRisks,
    Guid? ReportArtifactId,
    DateTimeOffset GeneratedAt);

[AuthorizeRequirement(TrialOperationsPermissions.Read)]
public sealed record GetTrialCampaignsQuery : IQuery<Result<IReadOnlyCollection<TrialCampaignDto>>>;

[AuthorizeRequirement(TrialOperationsPermissions.Read)]
public sealed record GetTrialCampaignDetailQuery(Guid CampaignId) : IQuery<Result<TrialCampaignDto>>;

[AuthorizeRequirement(TrialOperationsPermissions.Manage)]
public sealed record CreateTrialCampaignCommand(
    string Name,
    IReadOnlyCollection<string>? AllowedSourceModes = null,
    string? OwnerDepartment = null,
    DateTimeOffset? StartAt = null,
    DateTimeOffset? EndAt = null,
    string? Summary = null) : ICommand<Result<TrialCampaignDto>>;

[AuthorizeRequirement(TrialOperationsPermissions.Manage)]
public sealed record UpdateTrialCampaignStatusCommand(Guid CampaignId, string Status) : ICommand<Result<TrialCampaignDto>>;

[AuthorizeRequirement(TrialOperationsPermissions.Manage)]
public sealed record AttachAgentTaskToTrialCampaignCommand(
    Guid CampaignId,
    Guid TaskId,
    string? ScenarioId = null,
    string? TrialMode = null) : ICommand<Result<TrialCampaignDto>>;

[AuthorizeRequirement(TrialOperationsPermissions.Manage)]
public sealed record UpsertTrialRiskIssueCommand(
    Guid CampaignId,
    Guid? IssueId,
    string Severity,
    string Category,
    string Status,
    string? Owner = null,
    string? SourceRef = null,
    string? ResolutionHash = null) : ICommand<Result<TrialCampaignDto>>;

[AuthorizeRequirement(TrialOperationsPermissions.AuditView)]
public sealed record RunPilotReadinessEvaluationCommand(Guid CampaignId) : ICommand<Result<PilotReadinessAssessmentDto>>;

[AuthorizeRequirement(TrialOperationsPermissions.AuditView)]
public sealed record GenerateTrialEvidencePackageCommand(Guid CampaignId) : ICommand<Result<TrialEvidencePackageDto>>;
