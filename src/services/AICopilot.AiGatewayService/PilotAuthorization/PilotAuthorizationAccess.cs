using AICopilot.Core.AiGateway.Aggregates.PilotAuthorization;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.PilotAuthorization;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.PilotAuthorization;

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
