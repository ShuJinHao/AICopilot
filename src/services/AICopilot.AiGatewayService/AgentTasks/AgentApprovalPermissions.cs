using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal static class AgentApprovalPermissions
{
    public const string GetAgentTask = "AiGateway.GetAgentTask";
    public const string GetWorkspace = "AiGateway.GetWorkspace";
    public const string DownloadArtifact = "AiGateway.DownloadArtifact";
    public const string EditArtifact = "AiGateway.EditArtifact";
    public const string ApproveAgentTaskPlan = "AiGateway.ApproveAgentTaskPlan";
    public const string ApproveAgentToolCall = "AiGateway.ApproveAgentToolCall";
    public const string ApproveFinalOutput = "AiGateway.ApproveFinalOutput";
    public const string SubmitFinalReview = "AiGateway.SubmitFinalReview";
    public const string FinalizeWorkspace = "AiGateway.FinalizeWorkspace";
    public const string ReconcileAgentOutcome = "AiGateway.ReconcileAgentOutcome";

    public static string GetRequiredDecisionPermission(AgentApprovalType approvalType)
    {
        return approvalType switch
        {
            AgentApprovalType.Plan => ApproveAgentTaskPlan,
            AgentApprovalType.ToolCall => ApproveAgentToolCall,
            AgentApprovalType.Artifact or AgentApprovalType.FinalOutput => ApproveFinalOutput,
            _ => throw new ArgumentOutOfRangeException(nameof(approvalType), approvalType, "Unknown approval type.")
        };
    }

    public static bool AllowsCrossUserDecision(AgentApprovalType approvalType)
    {
        return approvalType is AgentApprovalType.ToolCall or AgentApprovalType.Artifact or AgentApprovalType.FinalOutput;
    }

    public static bool CanReadFinalReviewWorkspace(CurrentUserAccess access)
    {
        return HasPermission(access, ApproveFinalOutput) ||
               HasPermission(access, FinalizeWorkspace);
    }

    public static bool HasPermission(CurrentUserAccess? access, string permission)
    {
        return access?.Permissions.Contains(permission, StringComparer.Ordinal) == true;
    }

    public static bool HasAnyPermission(CurrentUserAccess? access, params string[] permissions)
    {
        return permissions.Any(permission => HasPermission(access, permission));
    }

    public static async Task<Result<CurrentUserAccess>> LoadCurrentUserAccessAsync(
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

    public static Result ForbiddenMissing(string permission)
    {
        return Result.Forbidden(new ApiProblemDescriptor(
            AuthProblemCodes.MissingPermission,
            "Current account is missing the required permission.",
            new Dictionary<string, object?>
            {
                [ApiProblemExtensionKeys.MissingPermissions] = new[] { permission }
            }));
    }
}
