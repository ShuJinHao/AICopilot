using System.Text;
using AICopilot.AiGatewayService.Models;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Agents;

internal static class ApprovalDecisionValidator
{
    public static ApprovalDecisionValidation Validate(
        ApprovalDecisionStreamRequest request,
        AgentApprovalBinding storedApproval,
        StringBuilder assistantText)
    {
        if (!TryParseDecision(request, assistantText, out var isApproved, out var error))
        {
            return ApprovalDecisionValidation.Failed(error!);
        }

        if (!string.Equals(request.CallId, storedApproval.ToolCallId, StringComparison.Ordinal))
        {
            return ApprovalDecisionValidation.Failed(AgentStreamRuntime.CreateErrorChunk(
                assistantText,
                AppProblemCodes.ApprovalAlreadyProcessed,
                "审批调用与服务端待审批绑定不一致。",
                nameof(ApprovalDecisionStreamHandler),
                "审批请求已失效，请重新发起新的请求。"));
        }

        var identity = storedApproval.TargetType is { } targetType &&
                       !string.IsNullOrWhiteSpace(storedApproval.TargetName) &&
                       !string.IsNullOrWhiteSpace(storedApproval.CanonicalToolName)
            ? new AiToolIdentity(
                storedApproval.ToolKind,
                targetType,
                storedApproval.TargetName,
                storedApproval.CanonicalToolName)
            : null;
        return ApprovalDecisionValidation.Valid(
            isApproved,
            identity,
            identity?.ToolName ?? storedApproval.CanonicalToolName ?? storedApproval.ToolName);
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

        error = AgentStreamRuntime.CreateErrorChunk(
            assistantText,
            "invalid_approval_decision",
            "审批决策只能是 approved 或 rejected。",
            nameof(ApprovalDecisionStreamHandler),
            "审批决策无效，请重新选择批准或拒绝。");
        return false;
    }
}

internal sealed record ApprovalDecisionValidation(
    bool IsValid,
    bool IsApproved,
    AiToolIdentity? Identity,
    string ToolName,
    ChatChunk? Error)
{
    public static ApprovalDecisionValidation Valid(
        bool isApproved,
        AiToolIdentity? identity,
        string toolName) =>
        new(true, isApproved, identity, toolName, null);

    public static ApprovalDecisionValidation Failed(ChatChunk error) =>
        new(false, false, null, string.Empty, error);
}
