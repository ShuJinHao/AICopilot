using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;

namespace AICopilot.EntityFrameworkCore.Repository;

internal static class AgentNodeRunBudgetSettlement
{
    public static bool TrySettle(
        AgentTaskRunAttempt attempt,
        AgentNodeRun node,
        long nodeFencingToken,
        AgentRunUsageLedgerEntry? usage,
        DateTimeOffset nowUtc,
        bool conservativelyConsumed)
    {
        if (usage is not null &&
            !string.Equals(
                usage.CostCurrency,
                attempt.BudgetCostCurrency,
                StringComparison.Ordinal))
        {
            return false;
        }

        AgentRunBudgetCharge reservation;
        try
        {
            reservation = node.GetActiveBudgetReservation(nodeFencingToken);
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        var actual = usage is null
            ? AgentRunBudgetCharge.Zero
            : new AgentRunBudgetCharge(
                usage.ToolCalls,
                usage.ModelCalls,
                usage.InputTokens,
                usage.OutputTokens,
                usage.ElapsedMilliseconds,
                usage.CostAmount,
                reservation.RetryCount,
                usage.ArtifactCount,
                usage.ArtifactBytes);
        if (!attempt.TrySettleBudget(reservation, actual, conservativelyConsumed))
        {
            return false;
        }

        node.CloseBudgetReservation(
            nodeFencingToken,
            conservativelyConsumed
                ? AgentBudgetReservationStatus.ConservativelyConsumed
                : AgentBudgetReservationStatus.Settled,
            nowUtc);
        return true;
    }
}
