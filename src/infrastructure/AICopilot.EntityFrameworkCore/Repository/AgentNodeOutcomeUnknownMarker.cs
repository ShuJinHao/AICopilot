using AICopilot.Core.AiGateway.Runtime.AgentExecution;

namespace AICopilot.EntityFrameworkCore.Repository;

internal static class AgentNodeOutcomeUnknownMarker
{
    private static readonly TimeSpan InitialReconciliationDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ReconciliationDeadline = TimeSpan.FromHours(24);

    public static void Mark(
        AgentNodeRun node,
        string reconciliationPolicy,
        string lastConfirmedStage,
        string safeMessage,
        DateTimeOffset nowUtc)
    {
        node.MarkOutcomeUnknown(
            node.TaskFencingToken,
            node.NodeFencingToken,
            node.ProviderOperationCode ?? node.ToolCode ?? node.NodeKind,
            node.ProviderReceiptHash,
            reconciliationPolicy,
            lastConfirmedStage,
            "not-confirmed",
            safeMessage,
            nowUtc,
            nowUtc.Add(InitialReconciliationDelay),
            nowUtc.Add(ReconciliationDeadline));
    }
}
