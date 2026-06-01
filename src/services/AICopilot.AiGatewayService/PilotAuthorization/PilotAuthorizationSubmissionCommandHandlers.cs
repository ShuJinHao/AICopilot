using AICopilot.Core.AiGateway.Aggregates.PilotAuthorization;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.PilotAuthorization;

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
        return Result.Success(PilotAuthorizationMapper.Map(submission));
    }
}
