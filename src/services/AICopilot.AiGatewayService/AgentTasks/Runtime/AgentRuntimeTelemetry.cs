using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;

namespace AICopilot.AiGatewayService.AgentTasks;

internal static class AgentRuntimeTelemetry
{
    public const string MeterName = "AICopilot.AgentRuntime";

    private static readonly Meter Meter = new(MeterName, "1.0.0");
    private static readonly ConcurrentDictionary<string, Counter<long>> Counters = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<(string Name, string? Unit), Histogram<double>> DoubleHistograms = new();
    private static readonly ConcurrentDictionary<(string Name, string? Unit), Histogram<long>> LongHistograms = new();

    public static void RecordQueueWait(TimeSpan elapsed) =>
        DoubleHistogram("aicopilot.agent.queue_wait_ms", "ms").Record(Math.Max(0, elapsed.TotalMilliseconds));

    public static void RecordNodeDuration(TimeSpan elapsed, string nodeKind) =>
        DoubleHistogram("aicopilot.agent.node_duration_ms", "ms").Record(
            Math.Max(0, elapsed.TotalMilliseconds),
            new KeyValuePair<string, object?>("node.kind", nodeKind));

    public static void RecordCheckpointLatency(TimeSpan elapsed, string outcome) =>
        DoubleHistogram("aicopilot.agent.checkpoint_latency_ms", "ms").Record(
            Math.Max(0, elapsed.TotalMilliseconds),
            new KeyValuePair<string, object?>("outcome", outcome));

    public static void RecordClaimConflict() => Counter("aicopilot.agent.claim_conflict_count").Add(1);

    public static void RecordLeaseRenewalFailure(string outcome) =>
        Counter("aicopilot.agent.lease_renewal_failure_count").Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public static void RecordStaleWorkerReject(string surface) =>
        Counter("aicopilot.agent.stale_worker_reject_count").Add(1, new KeyValuePair<string, object?>("surface", surface));

    public static void RecordRetry(string nodeKind) =>
        Counter("aicopilot.agent.retry_count").Add(1, new KeyValuePair<string, object?>("node.kind", nodeKind));

    public static void RecordOutcomeUnknown() => Counter("aicopilot.agent.outcome_unknown_count").Add(1);

    public static void RecordEvidenceNormalizationFailure(string errorCode) =>
        Counter("aicopilot.agent.evidence_normalization_failure_count").Add(
            1,
            new KeyValuePair<string, object?>("error.code", BoundCode(errorCode)));

    public static void RecordEvidenceAccessReject() => Counter("aicopilot.agent.evidence_access_reject_count").Add(1);

    public static void RecordBudgetReject(string reason) =>
        Counter("aicopilot.agent.budget_reject_count").Add(1, new KeyValuePair<string, object?>("reason", BoundCode(reason)));

    public static void RecordRequiredNodeFailure(string nodeKind) =>
        Counter("aicopilot.agent.required_node_failure_count").Add(1, new KeyValuePair<string, object?>("node.kind", nodeKind));

    public static void RecordUsage(AgentRunUsageLedgerEntry usage)
    {
        if (usage.InputTokens > 0)
        {
            LongHistogram("aicopilot.agent.input_tokens", "token").Record(usage.InputTokens);
        }

        if (usage.OutputTokens > 0)
        {
            LongHistogram("aicopilot.agent.output_tokens", "token").Record(usage.OutputTokens);
        }

        if (usage.CostAmount > 0)
        {
            DoubleHistogram("aicopilot.agent.cost_amount").Record(
                (double)usage.CostAmount,
                new KeyValuePair<string, object?>("currency", usage.CostCurrency));
        }
    }

    public static void RecordCrossSurfaceEvidenceConsistency(bool consistent) =>
        DoubleHistogram("aicopilot.agent.chat_durable_evidence_consistency", "%").Record(consistent ? 100d : 0d);

    private static Counter<long> Counter(string name) =>
        Counters.GetOrAdd(name, instrumentName => Meter.CreateCounter<long>(instrumentName));

    private static Histogram<double> DoubleHistogram(string name, string? unit = null) =>
        DoubleHistograms.GetOrAdd((name, unit), key => Meter.CreateHistogram<double>(key.Name, key.Unit));

    private static Histogram<long> LongHistogram(string name, string? unit = null) =>
        LongHistograms.GetOrAdd((name, unit), key => Meter.CreateHistogram<long>(key.Name, key.Unit));

    private static string BoundCode(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? "unknown"
            : value.Length <= 120
                ? value
                : value[..120];
}
