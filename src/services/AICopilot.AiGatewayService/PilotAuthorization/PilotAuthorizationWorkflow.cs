using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.PilotAuthorization;

public static class PilotAuthorizationPermissions
{
    public const string Submit = "PilotAuthorization.Submit";
    public const string View = "PilotAuthorization.View";
    public const string Review = "PilotAuthorization.Review";
    public const string ApprovePlanning = "PilotAuthorization.ApprovePlanning";
    public const string Reject = "PilotAuthorization.Reject";
    public const string Expire = "PilotAuthorization.Expire";
    public const string Audit = "PilotAuthorization.Audit";
}

public static class PilotAuthorizationAuditActions
{
    public const string DraftCreated = "PilotAuthorization.DraftCreated";
    public const string Submitted = "PilotAuthorization.Submitted";
    public const string MachineRejected = "PilotAuthorization.MachineRejected";
    public const string ReviewStarted = "PilotAuthorization.ReviewStarted";
    public const string ApprovedForCredentialWindowPlanning = "PilotAuthorization.ApprovedForCredentialWindowPlanning";
    public const string ApprovedForLimitedPilotExecutionPlanning = "PilotAuthorization.ApprovedForLimitedPilotExecutionPlanning";
    public const string Rejected = "PilotAuthorization.Rejected";
    public const string Expired = "PilotAuthorization.Expired";
    public const string Revoked = "PilotAuthorization.Revoked";
    public const string UnsafeDraftRejected = "PilotAuthorization.UnsafeDraftRejected";
    public const string UnsafeDecisionRejected = "PilotAuthorization.UnsafeDecisionRejected";
    public const string SelfReviewForbidden = "PilotAuthorization.SelfReviewForbidden";
}

public sealed record PilotAuthorizationSubmissionDto(
    Guid SubmissionId,
    string Status,
    string Title,
    string BusinessPurpose,
    Guid RequestedByUserId,
    string? RequestedByUserName,
    IReadOnlyCollection<string> EndpointCodes,
    int MaxRows,
    int TimeRangeDays,
    string DataOwner,
    string ToolOwner,
    string FinalOwner,
    string RollbackOwner,
    string EmergencyOwner,
    string MachineValidationStatus,
    IReadOnlyCollection<string> MachineRejectedReasons,
    string? EvidenceSummary,
    string? RollbackSummary,
    string? BusinessScope,
    string? Department,
    string? PilotOwner,
    DateTimeOffset? ExecutionWindowStart,
    DateTimeOffset? ExecutionWindowEnd,
    DateTimeOffset? RollbackWindowStart,
    DateTimeOffset? RollbackWindowEnd,
    string? CredentialOwner,
    string? SecretStorageMode,
    string? SecretReferenceNameHash,
    string? PostRunAuditArchiveFormat,
    string? SignedApprovalRef,
    DateTimeOffset? ExpiresAt,
    string? CredentialWindowPlanningSummary,
    string? LastDecisionStatus,
    string? LastDecisionReason,
    string GateState,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PilotAuthorizationAuditTimelineItemDto(
    Guid Id,
    Guid SubmissionId,
    string ActionCode,
    string TargetType,
    string Result,
    string Summary,
    IReadOnlyCollection<string> ChangedFields,
    IReadOnlyDictionary<string, string> Metadata,
    DateTime CreatedAt);

public sealed record PilotAuthorizationSubmissionUpsertRequest(
    string Title,
    string BusinessPurpose,
    IReadOnlyCollection<string>? EndpointCodes,
    int MaxRows,
    int TimeRangeDays,
    string DataOwner,
    string ToolOwner,
    string FinalOwner,
    string RollbackOwner,
    string EmergencyOwner,
    string? EvidenceSummary = null,
    string? RollbackSummary = null,
    string? BusinessScope = null,
    string? Department = null,
    string? PilotOwner = null,
    DateTimeOffset? ExecutionWindowStart = null,
    DateTimeOffset? ExecutionWindowEnd = null,
    DateTimeOffset? RollbackWindowStart = null,
    DateTimeOffset? RollbackWindowEnd = null,
    string? CredentialOwner = null,
    string? SecretStorageMode = null,
    string? SecretReferenceNameHash = null,
    string? PostRunAuditArchiveFormat = null,
    string? SignedApprovalRef = null,
    DateTimeOffset? ExpiresAt = null);

public sealed record PilotAuthorizationDecisionRequest(
    string? Reason = null,
    string? CredentialWindowPlanningSummary = null);

[AuthorizeRequirement(PilotAuthorizationPermissions.View)]
public sealed record GetPilotAuthorizationSubmissionsQuery
    : IQuery<Result<IReadOnlyCollection<PilotAuthorizationSubmissionDto>>>;

[AuthorizeRequirement(PilotAuthorizationPermissions.View)]
public sealed record GetPilotAuthorizationSubmissionQuery(Guid SubmissionId)
    : IQuery<Result<PilotAuthorizationSubmissionDto>>;

[AuthorizeRequirement(PilotAuthorizationPermissions.Audit)]
public sealed record GetPilotAuthorizationAuditTimelineQuery(Guid SubmissionId)
    : IQuery<Result<IReadOnlyCollection<PilotAuthorizationAuditTimelineItemDto>>>;

[AuthorizeRequirement(PilotAuthorizationPermissions.Submit)]
public sealed record CreatePilotAuthorizationSubmissionCommand(
    string Title,
    string BusinessPurpose,
    IReadOnlyCollection<string>? EndpointCodes,
    int MaxRows,
    int TimeRangeDays,
    string DataOwner,
    string ToolOwner,
    string FinalOwner,
    string RollbackOwner,
    string EmergencyOwner,
    string? EvidenceSummary = null,
    string? RollbackSummary = null,
    string? BusinessScope = null,
    string? Department = null,
    string? PilotOwner = null,
    DateTimeOffset? ExecutionWindowStart = null,
    DateTimeOffset? ExecutionWindowEnd = null,
    DateTimeOffset? RollbackWindowStart = null,
    DateTimeOffset? RollbackWindowEnd = null,
    string? CredentialOwner = null,
    string? SecretStorageMode = null,
    string? SecretReferenceNameHash = null,
    string? PostRunAuditArchiveFormat = null,
    string? SignedApprovalRef = null,
    DateTimeOffset? ExpiresAt = null) : ICommand<Result<PilotAuthorizationSubmissionDto>>;

[AuthorizeRequirement(PilotAuthorizationPermissions.Submit)]
public sealed record UpdatePilotAuthorizationSubmissionCommand(
    Guid SubmissionId,
    string Title,
    string BusinessPurpose,
    IReadOnlyCollection<string>? EndpointCodes,
    int MaxRows,
    int TimeRangeDays,
    string DataOwner,
    string ToolOwner,
    string FinalOwner,
    string RollbackOwner,
    string EmergencyOwner,
    string? EvidenceSummary = null,
    string? RollbackSummary = null,
    string? BusinessScope = null,
    string? Department = null,
    string? PilotOwner = null,
    DateTimeOffset? ExecutionWindowStart = null,
    DateTimeOffset? ExecutionWindowEnd = null,
    DateTimeOffset? RollbackWindowStart = null,
    DateTimeOffset? RollbackWindowEnd = null,
    string? CredentialOwner = null,
    string? SecretStorageMode = null,
    string? SecretReferenceNameHash = null,
    string? PostRunAuditArchiveFormat = null,
    string? SignedApprovalRef = null,
    DateTimeOffset? ExpiresAt = null) : ICommand<Result<PilotAuthorizationSubmissionDto>>;

[AuthorizeRequirement(PilotAuthorizationPermissions.Submit)]
public sealed record SubmitPilotAuthorizationSubmissionCommand(Guid SubmissionId)
    : ICommand<Result<PilotAuthorizationSubmissionDto>>;

[AuthorizeRequirement(PilotAuthorizationPermissions.ApprovePlanning)]
public sealed record ApprovePilotAuthorizationCredentialWindowPlanningCommand(
    Guid SubmissionId,
    string? Reason = null,
    string? CredentialWindowPlanningSummary = null) : ICommand<Result<PilotAuthorizationSubmissionDto>>;

[AuthorizeRequirement(PilotAuthorizationPermissions.ApprovePlanning)]
public sealed record ApprovePilotAuthorizationLimitedPilotExecutionPlanningCommand(
    Guid SubmissionId,
    string? Reason = null) : ICommand<Result<PilotAuthorizationSubmissionDto>>;

[AuthorizeRequirement(PilotAuthorizationPermissions.Reject)]
public sealed record RejectPilotAuthorizationSubmissionCommand(Guid SubmissionId, string Reason)
    : ICommand<Result<PilotAuthorizationSubmissionDto>>;

[AuthorizeRequirement(PilotAuthorizationPermissions.Reject)]
public sealed record RevokePilotAuthorizationSubmissionCommand(Guid SubmissionId, string Reason)
    : ICommand<Result<PilotAuthorizationSubmissionDto>>;

[AuthorizeRequirement(PilotAuthorizationPermissions.Expire)]
public sealed record ExpirePilotAuthorizationSubmissionCommand(Guid SubmissionId, string? Reason = null)
    : ICommand<Result<PilotAuthorizationSubmissionDto>>;
