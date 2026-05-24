using System.Text.RegularExpressions;
using AICopilot.Core.AiGateway.Aggregates.PilotAuthorization;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.PilotAuthorization;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

public sealed class GetPilotAuthorizationSubmissionsQueryHandler(
    IReadRepository<PilotAuthorizationSubmission> repository,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService)
    : IQueryHandler<GetPilotAuthorizationSubmissionsQuery, Result<IReadOnlyCollection<PilotAuthorizationSubmissionDto>>>
{
    public async Task<Result<IReadOnlyCollection<PilotAuthorizationSubmissionDto>>> Handle(
        GetPilotAuthorizationSubmissionsQuery request,
        CancellationToken cancellationToken)
    {
        var accessResult = await PilotAuthorizationAccess.LoadAsync(
            currentUser,
            identityAccessService,
            cancellationToken);
        if (!accessResult.IsSuccess || accessResult.Value is null)
        {
            return Result.From(accessResult);
        }

        var access = accessResult.Value;
        Guid? filterUserId = PilotAuthorizationAccess.CanViewAll(access) ? null : access.UserId;
        var submissions = await repository.ListAsync(
            new PilotAuthorizationSubmissionListSpec(filterUserId),
            cancellationToken);
        return Result.Success<IReadOnlyCollection<PilotAuthorizationSubmissionDto>>(
            submissions.Select(PilotAuthorizationMapper.Map).ToArray());
    }
}

public sealed class GetPilotAuthorizationSubmissionQueryHandler(
    IReadRepository<PilotAuthorizationSubmission> repository,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService)
    : IQueryHandler<GetPilotAuthorizationSubmissionQuery, Result<PilotAuthorizationSubmissionDto>>
{
    public async Task<Result<PilotAuthorizationSubmissionDto>> Handle(
        GetPilotAuthorizationSubmissionQuery request,
        CancellationToken cancellationToken)
    {
        var loadResult = await PilotAuthorizationAccess.LoadSubmissionAsync(
            repository,
            currentUser,
            identityAccessService,
            request.SubmissionId,
            requireOwnerOrViewAll: true,
            cancellationToken);
        return !loadResult.IsSuccess || loadResult.Value is null
            ? Result.From(loadResult)
            : Result.Success(PilotAuthorizationMapper.Map(loadResult.Value.Submission));
    }
}

public sealed class GetPilotAuthorizationAuditTimelineQueryHandler(
    IReadRepository<PilotAuthorizationSubmission> repository,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService,
    IAuditLogQueryService auditLogQueryService)
    : IQueryHandler<GetPilotAuthorizationAuditTimelineQuery, Result<IReadOnlyCollection<PilotAuthorizationAuditTimelineItemDto>>>
{
    private const int MaxTimelineItems = 500;

    public async Task<Result<IReadOnlyCollection<PilotAuthorizationAuditTimelineItemDto>>> Handle(
        GetPilotAuthorizationAuditTimelineQuery request,
        CancellationToken cancellationToken)
    {
        var loadResult = await PilotAuthorizationAccess.LoadSubmissionAsync(
            repository,
            currentUser,
            identityAccessService,
            request.SubmissionId,
            requireOwnerOrViewAll: true,
            cancellationToken);
        if (!loadResult.IsSuccess || loadResult.Value is null)
        {
            return Result.From(loadResult);
        }

        if (!PilotAuthorizationAccess.HasPermission(loadResult.Value.Access, PilotAuthorizationPermissions.Audit))
        {
            return Result.Forbidden(new ApiProblemDescriptor(
                AuthProblemCodes.MissingPermission,
                "Current account is missing the Pilot authorization audit permission.",
                new Dictionary<string, object?>
                {
                    [ApiProblemExtensionKeys.MissingPermissions] = new[] { PilotAuthorizationPermissions.Audit }
                }));
        }

        var logs = await auditLogQueryService.GetListAsync(
            page: 1,
            pageSize: MaxTimelineItems,
            actionGroup: AuditActionGroups.AiGateway,
            actionCode: null,
            targetType: "PilotAuthorizationSubmission",
            targetId: request.SubmissionId.ToString(),
            targetName: null,
            operatorUserName: null,
            result: null,
            from: null,
            to: null,
            cancellationToken);

        return Result.Success<IReadOnlyCollection<PilotAuthorizationAuditTimelineItemDto>>(
            logs.Items
                .OrderBy(item => item.CreatedAt)
                .ThenBy(item => item.Id)
                .Select(item => PilotAuthorizationAudit.MapTimelineItem(request.SubmissionId, item))
                .ToArray());
    }
}

public sealed class CreatePilotAuthorizationSubmissionCommandHandler(
    IRepository<PilotAuthorizationSubmission> repository,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<CreatePilotAuthorizationSubmissionCommand, Result<PilotAuthorizationSubmissionDto>>
{
    public async Task<Result<PilotAuthorizationSubmissionDto>> Handle(
        CreatePilotAuthorizationSubmissionCommand request,
        CancellationToken cancellationToken)
    {
        var accessResult = await PilotAuthorizationAccess.LoadAsync(
            currentUser,
            identityAccessService,
            cancellationToken);
        if (!accessResult.IsSuccess || accessResult.Value is null)
        {
            return Result.From(accessResult);
        }

        var access = accessResult.Value;
        PilotAuthorizationSubmission submission;
        try
        {
            submission = new PilotAuthorizationSubmission(
                access.UserId,
                access.UserName,
                request.Title,
                request.BusinessPurpose,
                request.EndpointCodes,
                request.MaxRows,
                request.TimeRangeDays,
                request.DataOwner,
                request.ToolOwner,
                request.FinalOwner,
                request.RollbackOwner,
                request.EmergencyOwner,
                request.EvidenceSummary,
                request.RollbackSummary,
                request.BusinessScope,
                request.Department,
                request.PilotOwner,
                request.ExecutionWindowStart,
                request.ExecutionWindowEnd,
                request.RollbackWindowStart,
                request.RollbackWindowEnd,
                request.CredentialOwner,
                request.SecretStorageMode,
                request.SecretReferenceNameHash,
                request.PostRunAuditArchiveFormat,
                request.SignedApprovalRef,
                request.ExpiresAt,
                DateTimeOffset.UtcNow);
        }
        catch (PilotAuthorizationUnsafeContentException ex)
        {
            await PilotAuthorizationAudit.WriteRejectedDraftAsync(
                auditLogWriter,
                PilotAuthorizationAuditActions.UnsafeDraftRejected,
                "Unsafe Pilot authorization draft creation was rejected before persistence.",
                null,
                cancellationToken);
            await auditLogWriter.SaveChangesAsync(cancellationToken);
            return Result.Invalid(ex.Message);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Result.Invalid(ex.Message);
        }

        repository.Add(submission);
        await PilotAuthorizationAudit.WriteAsync(
            auditLogWriter,
            PilotAuthorizationAuditActions.DraftCreated,
            AuditResults.Succeeded,
            submission,
            "Created Pilot authorization draft.",
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);
        return Result.Success(PilotAuthorizationMapper.Map(submission));
    }
}

public sealed class UpdatePilotAuthorizationSubmissionCommandHandler(
    IRepository<PilotAuthorizationSubmission> repository,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<UpdatePilotAuthorizationSubmissionCommand, Result<PilotAuthorizationSubmissionDto>>
{
    public async Task<Result<PilotAuthorizationSubmissionDto>> Handle(
        UpdatePilotAuthorizationSubmissionCommand request,
        CancellationToken cancellationToken)
    {
        var loadResult = await PilotAuthorizationAccess.LoadSubmissionAsync(
            repository,
            currentUser,
            identityAccessService,
            request.SubmissionId,
            requireOwnerOrViewAll: false,
            cancellationToken);
        if (!loadResult.IsSuccess || loadResult.Value is null)
        {
            return Result.From(loadResult);
        }

        var submission = loadResult.Value.Submission;
        try
        {
            submission.UpdateDraft(
                request.Title,
                request.BusinessPurpose,
                request.EndpointCodes,
                request.MaxRows,
                request.TimeRangeDays,
                request.DataOwner,
                request.ToolOwner,
                request.FinalOwner,
                request.RollbackOwner,
                request.EmergencyOwner,
                request.EvidenceSummary,
                request.RollbackSummary,
                request.BusinessScope,
                request.Department,
                request.PilotOwner,
                request.ExecutionWindowStart,
                request.ExecutionWindowEnd,
                request.RollbackWindowStart,
                request.RollbackWindowEnd,
                request.CredentialOwner,
                request.SecretStorageMode,
                request.SecretReferenceNameHash,
                request.PostRunAuditArchiveFormat,
                request.SignedApprovalRef,
                request.ExpiresAt,
                DateTimeOffset.UtcNow);
        }
        catch (PilotAuthorizationUnsafeContentException ex)
        {
            await PilotAuthorizationAudit.WriteRejectedDraftAsync(
                auditLogWriter,
                PilotAuthorizationAuditActions.UnsafeDraftRejected,
                "Unsafe Pilot authorization draft update was rejected before persistence.",
                submission,
                cancellationToken);
            await auditLogWriter.SaveChangesAsync(cancellationToken);
            return Result.Invalid(ex.Message);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Result.Invalid(ex.Message);
        }

        repository.Update(submission);
        await PilotAuthorizationAudit.WriteAsync(
            auditLogWriter,
            PilotAuthorizationAuditActions.DraftCreated,
            AuditResults.Succeeded,
            submission,
            "Updated Pilot authorization draft.",
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);
        return Result.Success(PilotAuthorizationMapper.Map(submission));
    }
}

public sealed class SubmitPilotAuthorizationSubmissionCommandHandler(
    IRepository<PilotAuthorizationSubmission> repository,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService,
    PilotAuthorizationMachineValidator validator,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<SubmitPilotAuthorizationSubmissionCommand, Result<PilotAuthorizationSubmissionDto>>
{
    public async Task<Result<PilotAuthorizationSubmissionDto>> Handle(
        SubmitPilotAuthorizationSubmissionCommand request,
        CancellationToken cancellationToken)
    {
        var loadResult = await PilotAuthorizationAccess.LoadSubmissionAsync(
            repository,
            currentUser,
            identityAccessService,
            request.SubmissionId,
            requireOwnerOrViewAll: false,
            cancellationToken);
        if (!loadResult.IsSuccess || loadResult.Value is null)
        {
            return Result.From(loadResult);
        }

        var submission = loadResult.Value.Submission;
        var validation = validator.Validate(submission);
        try
        {
            submission.Submit(validation, DateTimeOffset.UtcNow);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Invalid(ex.Message);
        }

        repository.Update(submission);
        await PilotAuthorizationAudit.WriteAsync(
            auditLogWriter,
            PilotAuthorizationAuditActions.Submitted,
            AuditResults.Succeeded,
            submission,
            "Submitted Pilot authorization package for machine validation.",
            cancellationToken);

        if (validation.IsAccepted)
        {
            await PilotAuthorizationAudit.WriteAsync(
                auditLogWriter,
                PilotAuthorizationAuditActions.ReviewStarted,
                AuditResults.Succeeded,
                submission,
                "Pilot authorization package passed machine validation and entered review.",
                cancellationToken);
        }
        else
        {
            await PilotAuthorizationAudit.WriteAsync(
                auditLogWriter,
                PilotAuthorizationAuditActions.MachineRejected,
                AuditResults.Rejected,
                submission,
                "Pilot authorization package was machine rejected.",
                cancellationToken);
        }

        await repository.SaveChangesAsync(cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);
        return Result.Success(PilotAuthorizationMapper.Map(submission));
    }
}

public sealed class ApprovePilotAuthorizationCredentialWindowPlanningCommandHandler(
    IRepository<PilotAuthorizationSubmission> repository,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<ApprovePilotAuthorizationCredentialWindowPlanningCommand, Result<PilotAuthorizationSubmissionDto>>
{
    public async Task<Result<PilotAuthorizationSubmissionDto>> Handle(
        ApprovePilotAuthorizationCredentialWindowPlanningCommand request,
        CancellationToken cancellationToken)
    {
        return await PilotAuthorizationDecisionHandlers.DecideAsync(
            repository,
            currentUser,
            identityAccessService,
            auditLogWriter,
            request.SubmissionId,
            PilotAuthorizationPermissions.ApprovePlanning,
            PilotAuthorizationAuditActions.ApprovedForCredentialWindowPlanning,
            [request.Reason, request.CredentialWindowPlanningSummary],
            (submission, access) => submission.ApproveCredentialWindowPlanning(
                access.UserId,
                access.UserName,
                request.Reason,
                request.CredentialWindowPlanningSummary,
                DateTimeOffset.UtcNow),
            "Approved credential-window planning only.",
            cancellationToken);
    }
}

public sealed class ApprovePilotAuthorizationLimitedPilotExecutionPlanningCommandHandler(
    IRepository<PilotAuthorizationSubmission> repository,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<ApprovePilotAuthorizationLimitedPilotExecutionPlanningCommand, Result<PilotAuthorizationSubmissionDto>>
{
    public async Task<Result<PilotAuthorizationSubmissionDto>> Handle(
        ApprovePilotAuthorizationLimitedPilotExecutionPlanningCommand request,
        CancellationToken cancellationToken)
    {
        return await PilotAuthorizationDecisionHandlers.DecideAsync(
            repository,
            currentUser,
            identityAccessService,
            auditLogWriter,
            request.SubmissionId,
            PilotAuthorizationPermissions.ApprovePlanning,
            PilotAuthorizationAuditActions.ApprovedForLimitedPilotExecutionPlanning,
            [request.Reason],
            (submission, access) => submission.ApproveLimitedPilotExecutionPlanning(
                access.UserId,
                access.UserName,
                request.Reason,
                DateTimeOffset.UtcNow),
            "Approved limited Pilot execution planning only; execution remains not granted.",
            cancellationToken);
    }
}

public sealed class RejectPilotAuthorizationSubmissionCommandHandler(
    IRepository<PilotAuthorizationSubmission> repository,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<RejectPilotAuthorizationSubmissionCommand, Result<PilotAuthorizationSubmissionDto>>
{
    public async Task<Result<PilotAuthorizationSubmissionDto>> Handle(
        RejectPilotAuthorizationSubmissionCommand request,
        CancellationToken cancellationToken)
    {
        return await PilotAuthorizationDecisionHandlers.DecideAsync(
            repository,
            currentUser,
            identityAccessService,
            auditLogWriter,
            request.SubmissionId,
            PilotAuthorizationPermissions.Reject,
            PilotAuthorizationAuditActions.Rejected,
            [request.Reason],
            (submission, access) => submission.Reject(
                access.UserId,
                access.UserName,
                request.Reason,
                DateTimeOffset.UtcNow),
            "Rejected Pilot authorization package.",
            cancellationToken);
    }
}

public sealed class RevokePilotAuthorizationSubmissionCommandHandler(
    IRepository<PilotAuthorizationSubmission> repository,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<RevokePilotAuthorizationSubmissionCommand, Result<PilotAuthorizationSubmissionDto>>
{
    public async Task<Result<PilotAuthorizationSubmissionDto>> Handle(
        RevokePilotAuthorizationSubmissionCommand request,
        CancellationToken cancellationToken)
    {
        return await PilotAuthorizationDecisionHandlers.DecideAsync(
            repository,
            currentUser,
            identityAccessService,
            auditLogWriter,
            request.SubmissionId,
            PilotAuthorizationPermissions.Reject,
            PilotAuthorizationAuditActions.Revoked,
            [request.Reason],
            (submission, access) => submission.Revoke(
                access.UserId,
                access.UserName,
                request.Reason,
                DateTimeOffset.UtcNow),
            "Revoked Pilot authorization planning approval.",
            cancellationToken);
    }
}

public sealed class ExpirePilotAuthorizationSubmissionCommandHandler(
    IRepository<PilotAuthorizationSubmission> repository,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<ExpirePilotAuthorizationSubmissionCommand, Result<PilotAuthorizationSubmissionDto>>
{
    public async Task<Result<PilotAuthorizationSubmissionDto>> Handle(
        ExpirePilotAuthorizationSubmissionCommand request,
        CancellationToken cancellationToken)
    {
        return await PilotAuthorizationDecisionHandlers.DecideAsync(
            repository,
            currentUser,
            identityAccessService,
            auditLogWriter,
            request.SubmissionId,
            PilotAuthorizationPermissions.Expire,
            PilotAuthorizationAuditActions.Expired,
            [request.Reason],
            (submission, access) => submission.Expire(
                access.UserId,
                access.UserName,
                request.Reason,
                DateTimeOffset.UtcNow),
            "Expired Pilot authorization package before any execution permission.",
            cancellationToken);
    }
}

public sealed class PilotAuthorizationMachineValidator
{
    private static readonly HashSet<string> AllowedEndpoints = new(StringComparer.OrdinalIgnoreCase)
    {
        "devices",
        "capacity_summary",
        "device_logs",
        "pass_station_records"
    };

    private static readonly (Regex Pattern, string Reason)[] ForbiddenPatterns =
    [
        (new Regex(@"\b(token|bearer|x-api-key|api\s*key|apikey|client[_-]?secret|access[_-]?token|refresh[_-]?token|connection\s*string|raw\s*payload|raw\s*rows|full\s*sql|free\s*sql)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Sensitive or unrestricted execution wording is not allowed."),
        (new Regex(@"\b(jdbc|odbc):[^\s]+|\b(postgres|postgresql|mysql|sqlserver|mongodb)://[^\s]+", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Database URL material is not allowed."),
        (new Regex(@"\b(recipe|version)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Recipe/version scope is not allowed."),
        (new Regex(@"\b(cloud\s*write|insert\s+into|update\s+\w+|delete\s+from|truncate\s+table|drop\s+table)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Cloud write or mutating SQL wording is not allowed.")
    ];

    public PilotAuthorizationMachineValidationResult Validate(PilotAuthorizationSubmission submission)
    {
        var rejected = new List<string>();

        if (submission.EndpointCodes.Length == 0)
        {
            rejected.Add("At least one endpoint is required.");
        }

        rejected.AddRange(submission.EndpointCodes
            .Where(endpoint => !AllowedEndpoints.Contains(endpoint))
            .Select(endpoint => $"Endpoint is not allowed: {endpoint}."));

        if (submission.MaxRows is <= 0 or > 50)
        {
            rejected.Add("maxRows must be between 1 and 50.");
        }

        if (submission.TimeRangeDays is <= 0 or > 7)
        {
            rejected.Add("timeRangeDays must be between 1 and 7.");
        }

        foreach (var (label, value) in new[]
                 {
                     ("data owner", submission.DataOwner),
                     ("tool owner", submission.ToolOwner),
                     ("final owner", submission.FinalOwner),
                     ("rollback owner", submission.RollbackPlan.RollbackOwner),
                     ("emergency owner", submission.RollbackPlan.EmergencyOwner),
                     ("business scope", submission.MaterialIntake.BusinessScope),
                     ("department", submission.MaterialIntake.Department),
                     ("pilot owner", submission.MaterialIntake.PilotOwner),
                     ("credential owner", submission.MaterialIntake.CredentialOwner),
                     ("secret storage mode", submission.MaterialIntake.SecretStorageMode),
                     ("secret reference name hash", submission.MaterialIntake.SecretReferenceNameHash),
                     ("post-run audit archive format", submission.MaterialIntake.PostRunAuditArchiveFormat),
                     ("signed approval ref", submission.MaterialIntake.SignedApprovalRef)
                 })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                rejected.Add($"{label} is required.");
            }
        }

        if (submission.MaterialIntake.ExecutionWindowStart is null
            || submission.MaterialIntake.ExecutionWindowEnd is null)
        {
            rejected.Add("execution window start and end are required.");
        }
        else if (submission.MaterialIntake.ExecutionWindowEnd <= submission.MaterialIntake.ExecutionWindowStart)
        {
            rejected.Add("execution window end must be after start.");
        }

        if (submission.MaterialIntake.RollbackWindowStart is null
            || submission.MaterialIntake.RollbackWindowEnd is null)
        {
            rejected.Add("rollback window start and end are required.");
        }
        else if (submission.MaterialIntake.RollbackWindowEnd <= submission.MaterialIntake.RollbackWindowStart)
        {
            rejected.Add("rollback window end must be after start.");
        }

        if (submission.ExpiresAt is null)
        {
            rejected.Add("expiresAt is required.");
        }
        else if (submission.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            rejected.Add("expiresAt must be in the future.");
        }

        var text = string.Join(
            "\n",
            submission.Title,
            submission.BusinessPurpose,
            string.Join(",", submission.EndpointCodes),
            submission.DataOwner,
            submission.ToolOwner,
            submission.FinalOwner,
            submission.RollbackPlan.RollbackOwner,
            submission.RollbackPlan.EmergencyOwner,
            submission.RollbackPlan.RollbackSummary,
            submission.EvidenceArchive.EvidenceSummary,
            submission.MaterialIntake.BusinessScope,
            submission.MaterialIntake.Department,
            submission.MaterialIntake.PilotOwner,
            submission.MaterialIntake.CredentialOwner,
            submission.MaterialIntake.SecretStorageMode,
            submission.MaterialIntake.SecretReferenceNameHash,
            submission.MaterialIntake.PostRunAuditArchiveFormat,
            submission.MaterialIntake.SignedApprovalRef);

        rejected.AddRange(ForbiddenPatterns
            .Where(rule => rule.Pattern.IsMatch(text))
            .Select(rule => rule.Reason));

        var sensitiveCheck = PilotAuthorizationSensitiveContentGuard.CheckSubmission(submission);
        if (!sensitiveCheck.IsSafe)
        {
            rejected.Add("Pilot authorization material contains sensitive or unrestricted content.");
        }

        var distinct = rejected
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return distinct.Length == 0
            ? PilotAuthorizationMachineValidationResult.Accepted()
            : PilotAuthorizationMachineValidationResult.Rejected(distinct);
    }
}

internal static class PilotAuthorizationAccess
{
    public static async Task<Result<CurrentUserAccess>> LoadAsync(
        ICurrentUser currentUser,
        IIdentityAccessService identityAccessService,
        CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } userId)
        {
            return Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.Unauthorized,
                "Current user id is missing or invalid."));
        }

        var access = await identityAccessService.GetCurrentUserAccessAsync(userId, cancellationToken);
        return access is null
            ? Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.UserMissing,
                "Current user does not exist."))
            : Result.Success(access);
    }

    public static async Task<Result<PilotAuthorizationLoadedSubmission>> LoadSubmissionAsync(
        IReadRepository<PilotAuthorizationSubmission> repository,
        ICurrentUser currentUser,
        IIdentityAccessService identityAccessService,
        Guid submissionId,
        bool requireOwnerOrViewAll,
        CancellationToken cancellationToken)
    {
        if (submissionId == Guid.Empty)
        {
            return Result.Invalid("Pilot authorization submission id is required.");
        }

        var accessResult = await LoadAsync(currentUser, identityAccessService, cancellationToken);
        if (!accessResult.IsSuccess || accessResult.Value is null)
        {
            return Result.From(accessResult);
        }

        var access = accessResult.Value;
        var submission = await repository.FirstOrDefaultAsync(
            new PilotAuthorizationSubmissionByIdSpec(new PilotAuthorizationSubmissionId(submissionId)),
            cancellationToken);
        if (submission is null)
        {
            return Result.NotFound();
        }

        if (requireOwnerOrViewAll && !CanAccessSubmission(access, submission))
        {
            return Result.Forbidden(new ApiProblemDescriptor(
                AuthProblemCodes.MissingPermission,
                "Current account cannot view this Pilot authorization submission.",
                new Dictionary<string, object?>
                {
                    [ApiProblemExtensionKeys.MissingPermissions] = new[] { PilotAuthorizationPermissions.Audit }
                }));
        }

        if (!requireOwnerOrViewAll && submission.RequestedByUserId != access.UserId)
        {
            return Result.Forbidden(new ApiProblemDescriptor(
                AuthProblemCodes.MissingPermission,
                "Current account cannot modify another user's Pilot authorization submission.",
                new Dictionary<string, object?>
                {
                    [ApiProblemExtensionKeys.MissingPermissions] = new[] { PilotAuthorizationPermissions.Review }
                }));
        }

        return Result.Success(new PilotAuthorizationLoadedSubmission(submission, access));
    }

    public static bool CanViewAll(CurrentUserAccess access)
    {
        return HasAny(
            access,
            PilotAuthorizationPermissions.Review,
            PilotAuthorizationPermissions.ApprovePlanning,
            PilotAuthorizationPermissions.Reject,
            PilotAuthorizationPermissions.Expire,
            PilotAuthorizationPermissions.Audit);
    }

    public static bool HasPermission(CurrentUserAccess access, string permission)
    {
        return access.Permissions.Contains(permission, StringComparer.Ordinal);
    }

    public static bool HasAny(CurrentUserAccess access, params string[] permissions)
    {
        return permissions.Any(permission => HasPermission(access, permission));
    }

    private static bool CanAccessSubmission(CurrentUserAccess access, PilotAuthorizationSubmission submission)
    {
        return submission.RequestedByUserId == access.UserId || CanViewAll(access);
    }
}

internal sealed record PilotAuthorizationLoadedSubmission(
    PilotAuthorizationSubmission Submission,
    CurrentUserAccess Access);

internal static class PilotAuthorizationDecisionHandlers
{
    public static async Task<Result<PilotAuthorizationSubmissionDto>> DecideAsync(
        IRepository<PilotAuthorizationSubmission> repository,
        ICurrentUser currentUser,
        IIdentityAccessService identityAccessService,
        IAuditLogWriter auditLogWriter,
        Guid submissionId,
        string requiredPermission,
        string auditAction,
        IReadOnlyCollection<string?> decisionTexts,
        Action<PilotAuthorizationSubmission, CurrentUserAccess> applyDecision,
        string auditSummary,
        CancellationToken cancellationToken)
    {
        var loadResult = await PilotAuthorizationAccess.LoadSubmissionAsync(
            repository,
            currentUser,
            identityAccessService,
            submissionId,
            requireOwnerOrViewAll: true,
            cancellationToken);
        if (!loadResult.IsSuccess || loadResult.Value is null)
        {
            return Result.From(loadResult);
        }

        if (!PilotAuthorizationAccess.HasPermission(loadResult.Value.Access, requiredPermission))
        {
            return Result.Forbidden(new ApiProblemDescriptor(
                AuthProblemCodes.MissingPermission,
                "Current account is missing the required Pilot authorization review permission.",
                new Dictionary<string, object?>
                {
                    [ApiProblemExtensionKeys.MissingPermissions] = new[] { requiredPermission }
                }));
        }

        var submission = loadResult.Value.Submission;
        var access = loadResult.Value.Access;
        try
        {
            PilotAuthorizationSensitiveContentGuard.ThrowIfUnsafe(
                decisionTexts.Select((value, index) =>
                    new PilotAuthorizationSensitiveField($"decisionText{index}", value)));
        }
        catch (PilotAuthorizationUnsafeContentException ex)
        {
            await PilotAuthorizationAudit.WriteAsync(
                auditLogWriter,
                PilotAuthorizationAuditActions.UnsafeDecisionRejected,
                AuditResults.Rejected,
                submission,
                "Unsafe Pilot authorization decision text was rejected before persistence.",
                cancellationToken);
            await auditLogWriter.SaveChangesAsync(cancellationToken);
            return Result.Invalid(ex.Message);
        }

        if (submission.RequestedByUserId == access.UserId)
        {
            await PilotAuthorizationAudit.WriteAsync(
                auditLogWriter,
                PilotAuthorizationAuditActions.SelfReviewForbidden,
                AuditResults.Rejected,
                submission,
                "Pilot authorization self-review was blocked.",
                cancellationToken);
            await auditLogWriter.SaveChangesAsync(cancellationToken);
            return Result.Forbidden(new ApiProblemDescriptor(
                "pilot_authorization_self_review_forbidden",
                "Pilot authorization requester cannot review, reject, revoke, or expire their own submission."));
        }

        try
        {
            applyDecision(submission, access);
        }
        catch (PilotAuthorizationUnsafeContentException ex)
        {
            await PilotAuthorizationAudit.WriteAsync(
                auditLogWriter,
                PilotAuthorizationAuditActions.UnsafeDecisionRejected,
                AuditResults.Rejected,
                submission,
                "Unsafe Pilot authorization decision text was rejected before persistence.",
                cancellationToken);
            await auditLogWriter.SaveChangesAsync(cancellationToken);
            return Result.Invalid(ex.Message);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Result.Invalid(ex.Message);
        }

        repository.Update(submission);
        await PilotAuthorizationAudit.WriteAsync(
            auditLogWriter,
            auditAction,
            auditAction == PilotAuthorizationAuditActions.Rejected ? AuditResults.Rejected : AuditResults.Succeeded,
            submission,
            auditSummary,
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);
        return Result.Success(PilotAuthorizationMapper.Map(submission));
    }
}

public sealed class PilotAuthorizationExpiryWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<PilotAuthorizationExpiryWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessOnceAsync(stoppingToken);
                if (!processed)
                {
                    await Task.Delay(PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Pilot authorization expiry worker iteration failed.");
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    public async Task<bool> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<PilotAuthorizationSubmission>>();
        var auditLogWriter = scope.ServiceProvider.GetRequiredService<IAuditLogWriter>();
        return await ExpireDueSubmissionsAsync(
            repository,
            auditLogWriter,
            DateTimeOffset.UtcNow,
            cancellationToken);
    }

    internal static async Task<bool> ExpireDueSubmissionsAsync(
        IRepository<PilotAuthorizationSubmission> repository,
        IAuditLogWriter auditLogWriter,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var dueSubmissions = await repository.ListAsync(
            new PilotAuthorizationExpiredOpenSubmissionsSpec(nowUtc),
            cancellationToken);

        foreach (var submission in dueSubmissions)
        {
            submission.ExpireBySystem(nowUtc);
            repository.Update(submission);
            await PilotAuthorizationAudit.WriteAsync(
                auditLogWriter,
                PilotAuthorizationAuditActions.Expired,
                AuditResults.Succeeded,
                submission,
                "Expired Pilot authorization package by DataWorker before any execution permission.",
                cancellationToken);
        }

        if (dueSubmissions.Count == 0)
        {
            return false;
        }

        await repository.SaveChangesAsync(cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);
        return true;
    }
}

internal static class PilotAuthorizationMapper
{
    public static PilotAuthorizationSubmissionDto Map(PilotAuthorizationSubmission submission)
    {
        return new PilotAuthorizationSubmissionDto(
            submission.Id.Value,
            submission.Status.ToString(),
            PilotAuthorizationSafeText.Redact(submission.Title) ?? string.Empty,
            PilotAuthorizationSafeText.Redact(submission.BusinessPurpose) ?? string.Empty,
            submission.RequestedByUserId,
            PilotAuthorizationSafeText.Redact(submission.RequestedByUserName),
            submission.EndpointCodes,
            submission.MaxRows,
            submission.TimeRangeDays,
            PilotAuthorizationSafeText.Redact(submission.DataOwner) ?? string.Empty,
            PilotAuthorizationSafeText.Redact(submission.ToolOwner) ?? string.Empty,
            PilotAuthorizationSafeText.Redact(submission.FinalOwner) ?? string.Empty,
            PilotAuthorizationSafeText.Redact(submission.RollbackPlan.RollbackOwner) ?? string.Empty,
            PilotAuthorizationSafeText.Redact(submission.RollbackPlan.EmergencyOwner) ?? string.Empty,
            submission.MachineValidationStatus,
            submission.MachineRejectedReasons,
            PilotAuthorizationSafeText.Redact(submission.EvidenceArchive.EvidenceSummary),
            PilotAuthorizationSafeText.Redact(submission.RollbackPlan.RollbackSummary),
            PilotAuthorizationSafeText.Redact(submission.MaterialIntake.BusinessScope),
            PilotAuthorizationSafeText.Redact(submission.MaterialIntake.Department),
            PilotAuthorizationSafeText.Redact(submission.MaterialIntake.PilotOwner),
            submission.MaterialIntake.ExecutionWindowStart,
            submission.MaterialIntake.ExecutionWindowEnd,
            submission.MaterialIntake.RollbackWindowStart,
            submission.MaterialIntake.RollbackWindowEnd,
            PilotAuthorizationSafeText.Redact(submission.MaterialIntake.CredentialOwner),
            PilotAuthorizationSafeText.Redact(submission.MaterialIntake.SecretStorageMode),
            PilotAuthorizationSafeText.Redact(submission.MaterialIntake.SecretReferenceNameHash),
            PilotAuthorizationSafeText.Redact(submission.MaterialIntake.PostRunAuditArchiveFormat),
            PilotAuthorizationSafeText.Redact(submission.MaterialIntake.SignedApprovalRef),
            submission.ExpiresAt,
            PilotAuthorizationSafeText.Redact(submission.CredentialWindow.PlanningSummary),
            submission.Review.LastDecisionStatus,
            PilotAuthorizationSafeText.Redact(submission.Review.LastDecisionReason),
            PilotAuthorizationGateState.Calculate(submission),
            submission.CreatedAt,
            submission.UpdatedAt);
    }
}

internal static class PilotAuthorizationGateState
{
    public const string BlockedMissingAuthorizationMaterials = "BlockedMissingAuthorizationMaterials";
    public const string BlockedUnsafeAuthorizationMaterials = "BlockedUnsafeAuthorizationMaterials";
    public const string ReviewPending = "ReviewPending";
    public const string ApprovedForCredentialWindowPlanning = "ApprovedForCredentialWindowPlanning";
    public const string ApprovedForLimitedPilotExecutionPlanning = "ApprovedForLimitedPilotExecutionPlanning";
    public const string BlockedUntilExplicitM7Authorization = "BlockedUntilExplicitM7Authorization";

    public static string Calculate(PilotAuthorizationSubmission submission)
    {
        if (!PilotAuthorizationSensitiveContentGuard.CheckSubmission(submission).IsSafe)
        {
            return BlockedUnsafeAuthorizationMaterials;
        }

        if (HasMissingAuthorizationMaterials(submission))
        {
            return BlockedMissingAuthorizationMaterials;
        }

        return submission.Status switch
        {
            PilotAuthorizationSubmissionStatus.ReviewPending => ReviewPending,
            PilotAuthorizationSubmissionStatus.ApprovedForCredentialWindowPlanning =>
                BlockedUntilExplicitM7Authorization,
            PilotAuthorizationSubmissionStatus.ApprovedForLimitedPilotExecutionPlanning =>
                BlockedUntilExplicitM7Authorization,
            _ => BlockedUntilExplicitM7Authorization
        };
    }

    private static bool HasMissingAuthorizationMaterials(PilotAuthorizationSubmission submission)
    {
        return submission.ExpiresAt is null
               || submission.EndpointCodes.Length == 0
               || submission.MaxRows is <= 0 or > 50
               || submission.TimeRangeDays is <= 0 or > 7
               || string.IsNullOrWhiteSpace(submission.DataOwner)
               || string.IsNullOrWhiteSpace(submission.ToolOwner)
               || string.IsNullOrWhiteSpace(submission.FinalOwner)
               || string.IsNullOrWhiteSpace(submission.RollbackPlan.RollbackOwner)
               || string.IsNullOrWhiteSpace(submission.RollbackPlan.EmergencyOwner)
               || string.IsNullOrWhiteSpace(submission.MaterialIntake.BusinessScope)
               || string.IsNullOrWhiteSpace(submission.MaterialIntake.Department)
               || string.IsNullOrWhiteSpace(submission.MaterialIntake.PilotOwner)
               || submission.MaterialIntake.ExecutionWindowStart is null
               || submission.MaterialIntake.ExecutionWindowEnd is null
               || submission.MaterialIntake.RollbackWindowStart is null
               || submission.MaterialIntake.RollbackWindowEnd is null
               || string.IsNullOrWhiteSpace(submission.MaterialIntake.CredentialOwner)
               || string.IsNullOrWhiteSpace(submission.MaterialIntake.SecretStorageMode)
               || string.IsNullOrWhiteSpace(submission.MaterialIntake.SecretReferenceNameHash)
               || string.IsNullOrWhiteSpace(submission.MaterialIntake.PostRunAuditArchiveFormat)
               || string.IsNullOrWhiteSpace(submission.MaterialIntake.SignedApprovalRef);
    }
}

internal static class PilotAuthorizationSafeText
{
    private static readonly Regex SensitiveTextPattern = new(
        @"\b(?:authorization|proxy-authorization)\s*:\s*Bearer\s+[A-Za-z0-9._~+/=-]+|\b(token|bearer|x-api-key|api\s*key|apikey|connection\s*string|client[_-]?secret|access[_-]?token|refresh[_-]?token|raw\s*payload|raw\s*(business\s*)?(rows|records)|full\s*sql|free\s*sql|private\s*key)\b\s*[:=]?\s*[\w\-./+=:;,@]*|\b(?:openai|azure_openai|anthropic|cohere|gemini)_?api_?key\s*[:=]\s*[A-Za-z0-9._~+/=-]+|\b(sk|pk|rk)-[A-Za-z0-9][A-Za-z0-9_\-]{7,}\b|\b(password|pwd|secret)\s*=\s*[^;\s]+|\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b|https?://[^\s]+|\b(jdbc|odbc):[^\s]+|\b(postgres|postgresql|mysql|sqlserver|mongodb)://[^\s]+|密钥|令牌|访问令牌|刷新令牌|凭据|连接串|连接字符串|数据库连接|明文密码|原始载荷|原始行|原始业务行|完整\s*SQL|自由\s*SQL|私钥|密码",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string? Redact(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? value
            : SensitiveTextPattern.Replace(value, "[redacted]");
    }
}

internal static class PilotAuthorizationAudit
{
    private static readonly HashSet<string> SafeMetadataKeys = new(StringComparer.Ordinal)
    {
        "pilotAuthorizationStatus",
        "endpointCount",
        "maxRows",
        "timeRangeDays",
        "ownerCount",
        "machineValidationStatus"
    };

    private static readonly HashSet<string> SafeChangedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "security",
        "status"
    };

    public static Task WriteRejectedDraftAsync(
        IAuditLogWriter auditLogWriter,
        string actionCode,
        string summary,
        PilotAuthorizationSubmission? submission,
        CancellationToken cancellationToken)
    {
        return auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                actionCode,
                "PilotAuthorizationSubmission",
                submission?.Id.Value.ToString(),
                "PilotAuthorizationSubmission",
                AuditResults.Rejected,
                summary,
                ["security"],
                BuildMetadata(submission)),
            cancellationToken);
    }

    public static PilotAuthorizationAuditTimelineItemDto MapTimelineItem(
        Guid submissionId,
        AuditLogSummaryDto item)
    {
        return new PilotAuthorizationAuditTimelineItemDto(
            item.Id,
            submissionId,
            item.ActionCode,
            item.TargetType,
            item.Result,
            PilotAuthorizationSafeText.Redact(item.Summary) ?? string.Empty,
            item.ChangedFields
                .Where(field => SafeChangedFields.Contains(field))
                .OrderBy(field => field, StringComparer.Ordinal)
                .ToArray(),
            SanitizeTimelineMetadata(item.Metadata),
            item.CreatedAt);
    }

    public static Task WriteAsync(
        IAuditLogWriter auditLogWriter,
        string actionCode,
        string result,
        PilotAuthorizationSubmission submission,
        string summary,
        CancellationToken cancellationToken)
    {
        return auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                actionCode,
                "PilotAuthorizationSubmission",
                submission.Id.Value.ToString(),
                "PilotAuthorizationSubmission",
                result,
                summary,
                ["status"],
                new Dictionary<string, string>
                {
                    ["pilotAuthorizationStatus"] = submission.Status.ToString(),
                    ["endpointCount"] = submission.EndpointCodes.Length.ToString(),
                    ["maxRows"] = submission.MaxRows.ToString(),
                    ["timeRangeDays"] = submission.TimeRangeDays.ToString(),
                    ["ownerCount"] = "5",
                    ["machineValidationStatus"] = submission.MachineValidationStatus
                }),
            cancellationToken);
    }

    private static Dictionary<string, string>? BuildMetadata(PilotAuthorizationSubmission? submission)
    {
        return submission is null
            ? null
            : new Dictionary<string, string>
            {
                ["pilotAuthorizationStatus"] = submission.Status.ToString(),
                ["endpointCount"] = submission.EndpointCodes.Length.ToString(),
                ["maxRows"] = submission.MaxRows.ToString(),
                ["timeRangeDays"] = submission.TimeRangeDays.ToString(),
                ["ownerCount"] = "5",
                ["machineValidationStatus"] = submission.MachineValidationStatus
            };
    }

    private static IReadOnlyDictionary<string, string> SanitizeTimelineMetadata(
        IReadOnlyDictionary<string, string> metadata)
    {
        return metadata
            .Where(item => SafeMetadataKeys.Contains(item.Key) && !string.IsNullOrWhiteSpace(item.Value))
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(
                item => item.Key,
                item => PilotAuthorizationSafeText.Redact(item.Value.Trim()) ?? string.Empty,
                StringComparer.Ordinal);
    }
}
