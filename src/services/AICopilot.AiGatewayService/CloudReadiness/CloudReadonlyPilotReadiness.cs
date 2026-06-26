using AICopilot.AiGatewayService.TrialOperations;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.CloudReadiness;

public static class CloudReadonlyPilotReadinessStatuses
{
    private const string ScopeGuardBoundaryMarker = "PilotReadinessRehearsal";
    private const string ScopeGuardProductionToolMarker = "query_cloud_data_readonly must remain disabled";

    public const string NotConfigured = "NotConfigured";
    public const string CollectingEvidence = "CollectingEvidence";
    public const string RehearsalReady = "RehearsalReady";
    public const string RehearsalPassed = "RehearsalPassed";
    public const string Blocked = "Blocked";
    public const string Failed = "Failed";
}

public sealed record CloudReadonlyPilotContractCheckSummaryDto(
    int Total,
    int Passed,
    int BlockedByPolicy,
    int Failed,
    DateTimeOffset? LastCheckedAt);

public sealed record CloudReadonlyPilotReadinessStatusDto(
    string Status,
    bool Enabled,
    string? EvidencePackageId,
    CloudReadonlyPilotConfigPackageDto? ConfigSummary,
    string ApprovalRehearsalStatus,
    CloudReadonlyPilotContractCheckSummaryDto ContractCheckSummary,
    IReadOnlyCollection<string> Blockers,
    IReadOnlyCollection<string> Warnings,
    DateTimeOffset? LastCheckedAt);

public sealed record CloudReadonlyPilotConfigPackageDto(
    string PackageId,
    IReadOnlyCollection<string> AllowedEndpointCodes,
    int MaxTimeRangeDays,
    int MaxRows,
    int TimeoutMs,
    string ApprovalPolicy,
    string RollbackPolicy,
    string OwnerDepartment,
    IReadOnlyCollection<string> EvidenceRefs,
    string Status);

public sealed record PilotApprovalRehearsalStepDto(
    string Code,
    string Label,
    string Status,
    bool IsBlocking,
    string AuditRef);

public sealed record PilotApprovalRehearsalDto(
    string RehearsalId,
    string PackageId,
    IReadOnlyCollection<PilotApprovalRehearsalStepDto> Steps,
    string Status,
    IReadOnlyCollection<string> Approvers,
    IReadOnlyCollection<string> AuditRefs,
    DateTimeOffset GeneratedAt);

public sealed record CloudReadonlyPilotContractRehearsalDto(
    string PackageId,
    string SourceMode,
    string Boundary,
    bool IsProductionData,
    IReadOnlyCollection<CloudAiReadEndpointCheckDto> Checks,
    IReadOnlyCollection<string> BlockedSamples,
    DateTimeOffset GeneratedAt);

[AuthorizeRequirement(TrialOperationsPermissions.Read)]
public sealed record GetCloudReadonlyPilotReadinessQuery
    : IQuery<Result<CloudReadonlyPilotReadinessStatusDto>>;

[AuthorizeRequirement(TrialOperationsPermissions.Manage)]
public sealed record CreateCloudReadonlyPilotConfigPackageCommand(
    Guid CampaignId,
    IReadOnlyCollection<string>? AllowedEndpointCodes = null,
    int? MaxTimeRangeDays = null,
    int? MaxRows = null,
    int? TimeoutMs = null,
    string? ApprovalPolicy = null,
    string? RollbackPolicy = null,
    string? OwnerDepartment = null) : ICommand<Result<CloudReadonlyPilotConfigPackageDto>>;

[AuthorizeRequirement(TrialOperationsPermissions.AuditView)]
public sealed record RunCloudReadonlyPilotGateEvaluationCommand(
    Guid CampaignId) : ICommand<Result<CloudReadonlyPilotReadinessStatusDto>>;

[AuthorizeRequirement(TrialOperationsPermissions.AuditView)]
public sealed record RunCloudReadonlyPilotApprovalRehearsalCommand(
    string PackageId) : ICommand<Result<PilotApprovalRehearsalDto>>;

[AuthorizeRequirement(TrialOperationsPermissions.AuditView)]
public sealed record RunCloudReadonlyPilotContractRehearsalCommand(
    string PackageId,
    IReadOnlyCollection<string>? EndpointCodes = null,
    int? MaxRows = null,
    int? TimeoutMs = null) : ICommand<Result<CloudReadonlyPilotContractRehearsalDto>>;

public interface ICloudReadonlyPilotReadinessStore
{
    void SavePackage(CloudReadonlyPilotConfigPackageDto package);

    CloudReadonlyPilotConfigPackageDto? GetPackage(string packageId);

    CloudReadonlyPilotConfigPackageDto? LatestPackage();

    void SaveApprovalRehearsal(PilotApprovalRehearsalDto rehearsal);

    PilotApprovalRehearsalDto? LatestApprovalRehearsal();

    void SaveContractRehearsal(CloudReadonlyPilotContractRehearsalDto rehearsal);

    CloudReadonlyPilotContractRehearsalDto? LatestContractRehearsal();
}
