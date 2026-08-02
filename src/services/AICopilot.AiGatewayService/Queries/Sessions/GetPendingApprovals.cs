using System.Text.Json;
using AICopilot.AiGatewayService.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentSessions;
using AICopilot.Core.AiGateway.Specifications.Sessions;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Queries.Sessions;

public sealed record PendingApprovalDto(
    string CallId,
    string Name,
    string? RuntimeName,
    string? TargetType,
    string? TargetName,
    string? ToolName,
    IReadOnlyDictionary<string, object?> Args,
    bool RequiresOnsiteAttestation,
    DateTimeOffset? AttestationExpiresAt);

[AuthorizeRequirement("AiGateway.Chat")]
public sealed record GetPendingApprovalsQuery(Guid SessionId)
    : IQuery<Result<IList<PendingApprovalDto>>>;

public sealed class GetPendingApprovalsQueryHandler(
    IReadRepository<Session> sessionRepository,
    ICurrentUser currentUser,
    IAgentSessionStateStore agentSessionStateStore,
    ApprovalRequirementResolver approvalRequirementResolver)
    : IQueryHandler<GetPendingApprovalsQuery, Result<IList<PendingApprovalDto>>>
{
    public async Task<Result<IList<PendingApprovalDto>>> Handle(
        GetPendingApprovalsQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } userId)
        {
            return Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.Unauthorized,
                "Current user id is missing or invalid."));
        }

        var session = await sessionRepository.FirstOrDefaultAsync(
            new SessionByIdForUserSpec(new SessionId(request.SessionId), userId),
            cancellationToken);
        if (session is null)
        {
            return Result.NotFound();
        }

        AgentSessionStateSnapshot state;
        try
        {
            state = await agentSessionStateStore.LoadOwnedAsync(
                request.SessionId,
                userId,
                currentUser.CloudTenantId,
                cancellationToken);
        }
        catch (AgentSessionStateException exception)
        {
            return exception.Failure switch
            {
                AgentSessionStateFailure.OwnershipMismatch => Result.NotFound(),
                AgentSessionStateFailure.Interrupted =>
                    Result.Invalid(new ApiProblemDescriptor(
                        AppProblemCodes.AgentSessionInterrupted,
                        "The AgentSession is interrupted and pending approvals are invalid.")),
                _ => Result.Invalid(new ApiProblemDescriptor(
                    AppProblemCodes.AgentSessionResetRequired,
                    "The persisted AgentSession cannot be restored safely."))
            };
        }

        if (state.Status == AgentSessionRuntimeStatus.Interrupted)
        {
            return Result.Invalid(new ApiProblemDescriptor(
                AppProblemCodes.AgentSessionInterrupted,
                "The AgentSession is interrupted and pending approvals are invalid."));
        }

        if (state.PendingApprovals.Count == 0)
        {
            return Result.Success<IList<PendingApprovalDto>>([]);
        }

        var approvals = new List<PendingApprovalDto>(state.PendingApprovals.Count);
        foreach (var approval in state.PendingApprovals)
        {
            var toolName = string.IsNullOrWhiteSpace(approval.CanonicalToolName)
                ? approval.ToolName
                : approval.CanonicalToolName!;
            var identity = BuildStoredIdentity(approval);
            var requirement = await approvalRequirementResolver
                .GetMergedRequirementByIdentityAsync(identity, cancellationToken);

            approvals.Add(new PendingApprovalDto(
                approval.ToolCallId,
                toolName,
                approval.ToolName,
                approval.TargetType?.ToString(),
                approval.TargetName,
                approval.CanonicalToolName,
                NormalizeArguments(approval.Arguments),
                requirement.RequiresOnsiteAttestation,
                session.OnsiteConfirmationExpiresAt));
        }

        return Result.Success<IList<PendingApprovalDto>>(approvals);
    }

    private static IReadOnlyDictionary<string, object?> NormalizeArguments(
        IReadOnlyDictionary<string, object?> arguments)
    {
        return arguments.ToDictionary(
            item => item.Key,
            item => NormalizeJsonElement(item.Value),
            StringComparer.OrdinalIgnoreCase);
    }

    private static AiToolIdentity? BuildStoredIdentity(AgentApprovalBinding approval)
    {
        if (approval.TargetType is not { } targetType ||
            string.IsNullOrWhiteSpace(approval.TargetName) ||
            string.IsNullOrWhiteSpace(approval.CanonicalToolName))
        {
            return null;
        }

        return new AiToolIdentity(
            approval.ToolKind,
            targetType,
            approval.TargetName,
            approval.CanonicalToolName);
    }

    private static object? NormalizeJsonElement(object? value)
    {
        if (value is not JsonElement jsonElement)
        {
            return value;
        }

        return jsonElement.ValueKind switch
        {
            JsonValueKind.String => jsonElement.GetString(),
            JsonValueKind.Number when jsonElement.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when jsonElement.TryGetDouble(out var doubleValue) => doubleValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Object => jsonElement.Deserialize<Dictionary<string, object?>>(),
            JsonValueKind.Array => jsonElement.Deserialize<object?[]>(),
            _ => jsonElement.ToString()
        };
    }
}
