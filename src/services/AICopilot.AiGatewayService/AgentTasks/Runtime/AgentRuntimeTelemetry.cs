using System.Diagnostics.Metrics;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;

namespace AICopilot.AiGatewayService.AgentTasks;

internal static class AgentRuntimeTelemetry
{
    public const string MeterName = "AICopilot.AgentRuntime";

    private static readonly Meter Meter = new(MeterName, "1.0.0");
    private static readonly Histogram<double> QueueWaitMilliseconds = Meter.CreateHistogram<double>(
        "aicopilot.agent.queue_wait_ms",
        "ms");
    private static readonly Histogram<double> NodeDurationMilliseconds = Meter.CreateHistogram<double>(
        "aicopilot.agent.node_duration_ms",
        "ms");
    private static readonly Histogram<double> CheckpointLatencyMilliseconds = Meter.CreateHistogram<double>(
        "aicopilot.agent.checkpoint_latency_ms",
        "ms");
    private static readonly Counter<long> ClaimConflicts = Meter.CreateCounter<long>(
        "aicopilot.agent.claim_conflict_count");
    private static readonly Counter<long> LeaseRenewalFailures = Meter.CreateCounter<long>(
        "aicopilot.agent.lease_renewal_failure_count");
    private static readonly Counter<long> StaleWorkerRejects = Meter.CreateCounter<long>(
        "aicopilot.agent.stale_worker_reject_count");
    private static readonly Counter<long> RetryCount = Meter.CreateCounter<long>(
        "aicopilot.agent.retry_count");
    private static readonly Counter<long> OutcomeUnknownCount = Meter.CreateCounter<long>(
        "aicopilot.agent.outcome_unknown_count");
    private static readonly Counter<long> EvidenceNormalizationFailures = Meter.CreateCounter<long>(
        "aicopilot.agent.evidence_normalization_failure_count");
    private static readonly Counter<long> EvidenceAccessRejects = Meter.CreateCounter<long>(
        "aicopilot.agent.evidence_access_reject_count");
    private static readonly Counter<long> BudgetRejects = Meter.CreateCounter<long>(
        "aicopilot.agent.budget_reject_count");
    private static readonly Counter<long> RequiredNodeFailures = Meter.CreateCounter<long>(
        "aicopilot.agent.required_node_failure_count");
    private static readonly Histogram<long> InputTokens = Meter.CreateHistogram<long>(
        "aicopilot.agent.input_tokens",
        "token");
    private static readonly Histogram<long> OutputTokens = Meter.CreateHistogram<long>(
        "aicopilot.agent.output_tokens",
        "token");
    private static readonly Histogram<double> CostAmount = Meter.CreateHistogram<double>(
        "aicopilot.agent.cost_amount");
    private static readonly Histogram<double> CrossSurfaceEvidenceConsistency = Meter.CreateHistogram<double>(
        "aicopilot.agent.chat_durable_evidence_consistency",
        "%");

    public static void RecordQueueWait(TimeSpan elapsed) =>
        QueueWaitMilliseconds.Record(Math.Max(0, elapsed.TotalMilliseconds));

    public static void RecordNodeDuration(TimeSpan elapsed, string nodeKind) =>
        NodeDurationMilliseconds.Record(
            Math.Max(0, elapsed.TotalMilliseconds),
            new KeyValuePair<string, object?>("node.kind", nodeKind));

    public static void RecordCheckpointLatency(TimeSpan elapsed, string outcome) =>
        CheckpointLatencyMilliseconds.Record(
            Math.Max(0, elapsed.TotalMilliseconds),
            new KeyValuePair<string, object?>("outcome", outcome));

    public static void RecordClaimConflict() => ClaimConflicts.Add(1);

    public static void RecordLeaseRenewalFailure(string outcome) =>
        LeaseRenewalFailures.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public static void RecordStaleWorkerReject(string surface) =>
        StaleWorkerRejects.Add(1, new KeyValuePair<string, object?>("surface", surface));

    public static void RecordRetry(string nodeKind) =>
        RetryCount.Add(1, new KeyValuePair<string, object?>("node.kind", nodeKind));

    public static void RecordOutcomeUnknown() => OutcomeUnknownCount.Add(1);

    public static void RecordEvidenceNormalizationFailure(string errorCode) =>
        EvidenceNormalizationFailures.Add(
            1,
            new KeyValuePair<string, object?>("error.code", BoundCode(errorCode)));

    public static void RecordEvidenceAccessReject() => EvidenceAccessRejects.Add(1);

    public static void RecordBudgetReject(string reason) =>
        BudgetRejects.Add(1, new KeyValuePair<string, object?>("reason", BoundCode(reason)));

    public static void RecordRequiredNodeFailure(string nodeKind) =>
        RequiredNodeFailures.Add(1, new KeyValuePair<string, object?>("node.kind", nodeKind));

    public static void RecordUsage(AgentRunUsageLedgerEntry usage)
    {
        if (usage.InputTokens > 0)
        {
            InputTokens.Record(usage.InputTokens);
        }

        if (usage.OutputTokens > 0)
        {
            OutputTokens.Record(usage.OutputTokens);
        }

        if (usage.CostAmount > 0)
        {
            CostAmount.Record(
                (double)usage.CostAmount,
                new KeyValuePair<string, object?>("currency", usage.CostCurrency));
        }
    }

    public static void RecordCrossSurfaceEvidenceConsistency(bool consistent) =>
        CrossSurfaceEvidenceConsistency.Record(consistent ? 100d : 0d);

    private static string BoundCode(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? "unknown"
            : value.Length <= 120
                ? value
                : value[..120];
}
