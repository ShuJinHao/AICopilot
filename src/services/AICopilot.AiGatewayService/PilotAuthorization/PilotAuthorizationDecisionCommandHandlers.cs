using AICopilot.Core.AiGateway.Aggregates.PilotAuthorization;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.PilotAuthorization;

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
        return Result.Success(PilotAuthorizationMapper.Map(submission));
    }
}
