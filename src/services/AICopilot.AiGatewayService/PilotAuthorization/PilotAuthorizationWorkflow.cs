using System.Text.RegularExpressions;
using AICopilot.Core.AiGateway.Aggregates.PilotAuthorization;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.PilotAuthorization;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.PilotAuthorization;

public static class PilotAuthorizationPermissions
{
    public const string Submit = "PilotAuthorization.Submit";
    public const string View = "PilotAuthorization.View";
    public const string Review = "PilotAuthorization.Review";
    public const string ApprovePlanning = "PilotAuthorization.ApprovePlanning";
    public const string Reject = "PilotAuthorization.Reject";
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
    string? CredentialWindowPlanningSummary,
    string? LastDecisionStatus,
    string? LastDecisionReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

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
    string? RollbackSummary = null);

public sealed record PilotAuthorizationDecisionRequest(
    string? Reason = null,
    string? CredentialWindowPlanningSummary = null);

[AuthorizeRequirement(PilotAuthorizationPermissions.View)]
public sealed record GetPilotAuthorizationSubmissionsQuery
    : IQuery<Result<IReadOnlyCollection<PilotAuthorizationSubmissionDto>>>;

[AuthorizeRequirement(PilotAuthorizationPermissions.View)]
public sealed record GetPilotAuthorizationSubmissionQuery(Guid SubmissionId)
    : IQuery<Result<PilotAuthorizationSubmissionDto>>;

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
    string? RollbackSummary = null) : ICommand<Result<PilotAuthorizationSubmissionDto>>;

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
    string? RollbackSummary = null) : ICommand<Result<PilotAuthorizationSubmissionDto>>;

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
                DateTimeOffset.UtcNow);
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
                DateTimeOffset.UtcNow);
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
            submission => submission.ApproveCredentialWindowPlanning(
                currentUser.Id!.Value,
                currentUser.UserName,
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
            submission => submission.ApproveLimitedPilotExecutionPlanning(
                currentUser.Id!.Value,
                currentUser.UserName,
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
            submission => submission.Reject(
                currentUser.Id!.Value,
                currentUser.UserName,
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
            submission => submission.Revoke(
                currentUser.Id!.Value,
                currentUser.UserName,
                request.Reason,
                DateTimeOffset.UtcNow),
            "Revoked Pilot authorization planning approval.",
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
        (new Regex(@"\b(token|api\s*key|apikey|connection\s*string|raw\s*payload|raw\s*rows|full\s*sql|free\s*sql)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Sensitive or unrestricted execution wording is not allowed."),
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
                     ("emergency owner", submission.RollbackPlan.EmergencyOwner)
                 })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                rejected.Add($"{label} is required.");
            }
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
            submission.EvidenceArchive.EvidenceSummary);

        rejected.AddRange(ForbiddenPatterns
            .Where(rule => rule.Pattern.IsMatch(text))
            .Select(rule => rule.Reason));

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
        Action<PilotAuthorizationSubmission> applyDecision,
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
        try
        {
            applyDecision(submission);
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
            PilotAuthorizationSafeText.Redact(submission.CredentialWindow.PlanningSummary),
            submission.Review.LastDecisionStatus,
            PilotAuthorizationSafeText.Redact(submission.Review.LastDecisionReason),
            submission.CreatedAt,
            submission.UpdatedAt);
    }
}

internal static class PilotAuthorizationSafeText
{
    private static readonly Regex SensitiveTextPattern = new(
        @"\b(token|api\s*key|apikey|connection\s*string|raw\s*payload|raw\s*(business\s*)?(rows|records)|full\s*sql|free\s*sql)\b\s*[:=]?\s*[\w\-./+=:;,@]*|\b(sk|pk|rk)-[A-Za-z0-9][A-Za-z0-9_\-]{7,}\b|\b(password|pwd|secret)\s*=\s*[^;\s]+",
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
}
