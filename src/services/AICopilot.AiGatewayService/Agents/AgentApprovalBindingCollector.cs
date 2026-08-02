using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.AiGatewayService.Agents;

internal static class AgentApprovalBindingCollector
{
    public static void Capture(
        RuntimeAgentUpdate update,
        IReadOnlyCollection<AiToolDefinition> tools,
        SessionRuntimeSnapshot session,
        string? tenantId,
        ICollection<AgentApprovalBinding> approvals)
    {
        foreach (var approval in update.Contents.OfType<AiToolApprovalRequestContent>())
        {
            var call = approval.Request.ToolCall;
            var existing = approvals.FirstOrDefault(binding =>
                string.Equals(binding.ToolCallId, call.CallId, StringComparison.Ordinal));
            if (existing is not null)
            {
                continue;
            }

            if (approvals.Count > 0)
            {
                throw new AgentRuntimeMultipleToolCallsException();
            }

            var definition = tools.SingleOrDefault(
                tool => string.Equals(tool.Name, call.Name, StringComparison.Ordinal));
            if (definition is null || !definition.RequiresApproval)
            {
                throw new InvalidOperationException(
                    "Harness surfaced an approval for an unknown or non-approval tool.");
            }

            approvals.Add(new AgentApprovalBinding(
                session.Id,
                session.UserId,
                string.IsNullOrWhiteSpace(tenantId) ? null : tenantId.Trim(),
                approval.Request.RequestId,
                call.CallId,
                call.Name,
                call.Kind,
                call.ServerName,
                call.TargetType,
                call.TargetName,
                call.ToolName,
                call.Arguments,
                definition.SchemaVersion,
                CanonicalJson.ComputeSha256(CanonicalJson.Serialize(call.Arguments))));
        }
    }

    public static bool HasMultipleDifferentToolCalls(
        IEnumerable<AgentApprovalBinding> approvals)
    {
        return approvals
            .Select(binding => binding.ToolCallId)
            .Distinct(StringComparer.Ordinal)
            .Skip(1)
            .Any();
    }
}
