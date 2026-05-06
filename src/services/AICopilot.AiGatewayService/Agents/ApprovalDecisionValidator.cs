using System.Text;
using AICopilot.AiGatewayService.Approvals;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Workflows;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Agents;

internal static class ApprovalDecisionValidator
{
    public static async Task<ApprovalDecisionValidation> ValidateAsync(
        ApprovalDecisionStreamRequest request,
        StoredToolApprovalRequest storedApproval,
        SessionRuntimeSnapshot session,
        ApprovalRequirementResolver approvalRequirementResolver,
        StringBuilder assistantText,
        CancellationToken cancellationToken)
    {
        if (!TryParseDecision(request, assistantText, out var isApproved, out var error))
        {
            return ApprovalDecisionValidation.Failed(error!);
        }

        if (!ApprovalIdentityMatches(request, storedApproval))
        {
            return ApprovalDecisionValidation.Failed(ChatStreamRuntime.CreateErrorChunk(
                assistantText,
                AppProblemCodes.ApprovalAlreadyProcessed,
                "审批请求的工具身份与待审批上下文不一致。",
                nameof(ApprovalDecisionStreamHandler),
                "审批请求已失效，请重新发起新的诊断请求。"));
        }

        var identity = BuildStoredIdentity(storedApproval);
        var toolName = identity?.ToolName ?? storedApproval.ToolName ?? storedApproval.CallId;
        var requirement = await approvalRequirementResolver.GetMergedRequirementByIdentityAsync(identity, cancellationToken);
        if (!TryValidateOnsiteAttestation(request, session, requirement, assistantText, isApproved, out error))
        {
            return ApprovalDecisionValidation.Failed(error!);
        }

        return ApprovalDecisionValidation.Valid(isApproved, identity, toolName, requirement);
    }

    private static bool TryParseDecision(
        ApprovalDecisionStreamRequest request,
        StringBuilder assistantText,
        out bool isApproved,
        out ChatChunk? error)
    {
        isApproved = false;
        error = null;

        var decision = request.Decision.Trim();
        if (string.Equals(decision, "approved", StringComparison.OrdinalIgnoreCase))
        {
            isApproved = true;
            return true;
        }

        if (string.Equals(decision, "rejected", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        error = ChatStreamRuntime.CreateErrorChunk(
            assistantText,
            "invalid_approval_decision",
            "审批决策只能是 approved 或 rejected。",
            nameof(ApprovalDecisionStreamHandler),
            "审批决策无效，请重新选择批准或拒绝。");
        return false;
    }

    private static bool ApprovalIdentityMatches(
        ApprovalDecisionStreamRequest request,
        StoredToolApprovalRequest storedApproval)
    {
        if (string.IsNullOrWhiteSpace(storedApproval.TargetType)
            || string.IsNullOrWhiteSpace(storedApproval.TargetName)
            || string.IsNullOrWhiteSpace(storedApproval.ToolName))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.TargetType)
            || string.IsNullOrWhiteSpace(request.TargetName)
            || string.IsNullOrWhiteSpace(request.ToolName))
        {
            return false;
        }

        return string.Equals(request.TargetType, storedApproval.TargetType, StringComparison.OrdinalIgnoreCase)
               && string.Equals(request.TargetName, storedApproval.TargetName, StringComparison.OrdinalIgnoreCase)
               && string.Equals(request.ToolName, storedApproval.ToolName, StringComparison.OrdinalIgnoreCase);
    }

    private static AiToolIdentity? BuildStoredIdentity(StoredToolApprovalRequest storedApproval)
    {
        if (!Enum.TryParse<AiToolTargetType>(storedApproval.TargetType, ignoreCase: true, out var targetType)
            || string.IsNullOrWhiteSpace(storedApproval.TargetName)
            || string.IsNullOrWhiteSpace(storedApproval.ToolName))
        {
            return null;
        }

        var kind = Enum.TryParse<AiToolCallKind>(storedApproval.ToolKind, ignoreCase: true, out var parsedKind)
            ? parsedKind
            : targetType == AiToolTargetType.McpServer
                ? AiToolCallKind.Mcp
                : AiToolCallKind.Function;

        return new AiToolIdentity(kind, targetType, storedApproval.TargetName, storedApproval.ToolName);
    }

    private static bool TryValidateOnsiteAttestation(
        ApprovalDecisionStreamRequest request,
        SessionRuntimeSnapshot session,
        ApprovalRequirement requirement,
        StringBuilder assistantText,
        bool isApproved,
        out ChatChunk? error)
    {
        error = null;
        if (!isApproved || !requirement.RequiresOnsiteAttestation)
        {
            return true;
        }

        if (!session.OnsiteConfirmationExpiresAt.HasValue || !session.OnsiteConfirmedAt.HasValue)
        {
            error = ChatStreamRuntime.CreateErrorChunk(
                assistantText,
                AppProblemCodes.OnsitePresenceRequired,
                "该工具能力要求先完成会话级人工在岗声明。",
                nameof(ApprovalDecisionStreamHandler),
                "该建议需要先确认现场有人在岗，请先设置在岗声明。");
            return false;
        }

        if (!session.HasValidOnsiteAttestation(DateTimeOffset.UtcNow))
        {
            error = ChatStreamRuntime.CreateErrorChunk(
                assistantText,
                AppProblemCodes.OnsitePresenceExpired,
                "当前会话的在岗声明已过期，请重新确认人工在场状态。",
                nameof(ApprovalDecisionStreamHandler),
                "当前会话的在岗声明已过期，请重新确认现场有人在岗。");
            return false;
        }

        if (!request.OnsiteConfirmed)
        {
            error = ChatStreamRuntime.CreateErrorChunk(
                assistantText,
                AppProblemCodes.ApprovalReconfirmationRequired,
                "审批前必须再次显式确认现场有人在岗。",
                nameof(ApprovalDecisionStreamHandler),
                "批准前需要再次确认现场有人在岗。");
            return false;
        }

        return true;
    }
}

internal sealed record ApprovalDecisionValidation(
    bool IsValid,
    bool IsApproved,
    AiToolIdentity? Identity,
    string ToolName,
    ApprovalRequirement Requirement,
    ChatChunk? Error)
{
    public static ApprovalDecisionValidation Valid(
        bool isApproved,
        AiToolIdentity? identity,
        string toolName,
        ApprovalRequirement requirement)
    {
        return new ApprovalDecisionValidation(true, isApproved, identity, toolName, requirement, null);
    }

    public static ApprovalDecisionValidation Failed(ChatChunk error)
    {
        return new ApprovalDecisionValidation(false, false, null, string.Empty, ApprovalRequirement.None, error);
    }
}
