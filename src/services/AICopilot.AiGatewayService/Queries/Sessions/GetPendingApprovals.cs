using System.Text.Json;
using AICopilot.AiGatewayService.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Ids;
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
    IFinalAgentContextStore finalAgentContextStore,
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

        var storedContext = await finalAgentContextStore.GetAsync(request.SessionId, cancellationToken);
        if (storedContext?.PendingApprovals.Count is not > 0)
        {
            return Result.Success<IList<PendingApprovalDto>>([]);
        }

        var approvals = new List<PendingApprovalDto>(storedContext.PendingApprovals.Count);
        foreach (var approval in storedContext.PendingApprovals)
        {
            var toolName = string.IsNullOrWhiteSpace(approval.ToolName)
                ? approval.CallId
                : approval.ToolName!;
            var identity = BuildStoredIdentity(approval);
            var requirement = await approvalRequirementResolver
                .GetMergedRequirementByIdentityAsync(identity, cancellationToken);

            approvals.Add(new PendingApprovalDto(
                approval.CallId,
                toolName,
                approval.RuntimeName,
                approval.TargetType,
                approval.TargetName,
                approval.ToolName,
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

    private static AiToolIdentity? BuildStoredIdentity(StoredToolApprovalRequest approval)
    {
        if (!Enum.TryParse<AiToolTargetType>(approval.TargetType, ignoreCase: true, out var targetType)
            || string.IsNullOrWhiteSpace(approval.TargetName)
            || string.IsNullOrWhiteSpace(approval.ToolName))
        {
            return null;
        }

        var kind = Enum.TryParse<AiToolCallKind>(approval.ToolKind, ignoreCase: true, out var parsedKind)
            ? parsedKind
            : targetType == AiToolTargetType.McpServer
                ? AiToolCallKind.Mcp
                : AiToolCallKind.Function;

        return new AiToolIdentity(kind, targetType, approval.TargetName, approval.ToolName);
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
