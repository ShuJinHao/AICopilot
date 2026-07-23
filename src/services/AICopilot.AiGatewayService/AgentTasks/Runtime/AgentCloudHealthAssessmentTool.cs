using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed record AgentCloudHealthAssessmentOutput(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("resultType")] string ResultType,
    [property: JsonPropertyName("algorithmVersion")] string AlgorithmVersion,
    [property: JsonPropertyName("assessmentType")] string AssessmentType,
    [property: JsonPropertyName("truthClass")] string TruthClass,
    [property: JsonPropertyName("healthScore")] int HealthScore,
    [property: JsonPropertyName("healthLevel")] string HealthLevel,
    [property: JsonPropertyName("safeSummary")] string SafeSummary,
    [property: JsonPropertyName("findings")] IReadOnlyCollection<string> Findings,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("missingRate")] double MissingRate,
    [property: JsonPropertyName("inputEvidenceCount")] int InputEvidenceCount,
    [property: JsonPropertyName("evidenceSetDigest")] string EvidenceSetDigest,
    [property: JsonPropertyName("sourceAsOfUtc")] DateTimeOffset SourceAsOfUtc,
    [property: JsonPropertyName("sourceMode")] string SourceMode,
    [property: JsonPropertyName("isSimulation")] bool IsSimulation,
    [property: JsonPropertyName("rowCount")] int RowCount,
    [property: JsonPropertyName("isTruncated")] bool IsTruncated,
    [property: JsonPropertyName("typedMetrics")] IReadOnlyDictionary<string, decimal> TypedMetrics);

internal static class AgentCloudHealthAssessmentOutputAuthority
{
    private static readonly string[] RequiredMetricCodes =
    [
        "futureHeartbeatCount",
        "missingHeartbeatCount",
        "reportedIssueStatusCount",
        "staleHeartbeatCount",
        "totalDeviceCount",
        "unknownRuntimeStatusCount"
    ];

    public static bool TryRead(
        string canonicalJson,
        out AgentCloudHealthAssessmentOutput? output)
    {
        output = null;
        try
        {
            var parsed = JsonSerializer.Deserialize<AgentCloudHealthAssessmentOutput>(
                canonicalJson,
                CanonicalJson.SerializerOptions);
            if (parsed is null ||
                !string.Equals(parsed.Status, "completed", StringComparison.Ordinal) ||
                !string.Equals(parsed.ResultType, "cloud-health-assessment", StringComparison.Ordinal) ||
                !string.Equals(parsed.AlgorithmVersion, AgentCloudHealthAssessmentTool.AlgorithmVersion, StringComparison.Ordinal) ||
                !string.Equals(parsed.AssessmentType, "CurrentDeviceRuntimeHealth", StringComparison.Ordinal) ||
                !string.Equals(parsed.TruthClass, "DerivedFact", StringComparison.Ordinal) ||
                parsed.HealthScore is < 0 or > 100 ||
                parsed.HealthLevel is not ("Stable" or "Watch" or "Attention" or "DataInsufficient") ||
                parsed.Confidence is < 0 or > 1 ||
                parsed.MissingRate is < 0 or > 1 ||
                double.IsNaN(parsed.Confidence) ||
                double.IsInfinity(parsed.Confidence) ||
                double.IsNaN(parsed.MissingRate) ||
                double.IsInfinity(parsed.MissingRate) ||
                parsed.InputEvidenceCount != 1 ||
                parsed.SourceAsOfUtc == default ||
                parsed.RowCount < 0 ||
                string.IsNullOrWhiteSpace(parsed.SourceMode) ||
                !IsSha256(parsed.EvidenceSetDigest) ||
                string.IsNullOrWhiteSpace(parsed.SafeSummary) ||
                parsed.SafeSummary.Length > 1_000 ||
                parsed.Findings is null ||
                parsed.Findings.Count is < 1 or > 8 ||
                parsed.Findings.Any(string.IsNullOrWhiteSpace) ||
                parsed.TypedMetrics is null ||
                !parsed.TypedMetrics.Keys.OrderBy(value => value, StringComparer.Ordinal)
                    .SequenceEqual(RequiredMetricCodes, StringComparer.Ordinal) ||
                parsed.TypedMetrics.Values.Any(value => value < 0) ||
                !MatchesLevel(parsed.HealthScore, parsed.HealthLevel, parsed.RowCount, parsed.MissingRate) ||
                !string.Equals(CanonicalJson.Serialize(parsed), canonicalJson, StringComparison.Ordinal))
            {
                return false;
            }

            output = parsed;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool MatchesLevel(int score, string level, int rowCount, double missingRate)
    {
        var expected = rowCount == 0 || missingRate >= 0.5
            ? "DataInsufficient"
            : score >= 85
                ? "Stable"
                : score >= 60
                    ? "Watch"
                    : "Attention";
        return string.Equals(expected, level, StringComparison.Ordinal) &&
               (level != "DataInsufficient" || score == 0);
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

internal static class AgentCloudHealthAssessmentTool
{
    public const string AlgorithmVersion = "cloud-health-assessment:v1";
    private static readonly TimeSpan HeartbeatStaleAfter = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan FutureClockTolerance = TimeSpan.FromMinutes(5);

    private static readonly IReadOnlySet<string> ReportedIssueStatuses = new HashSet<string>(
        ["Degraded", "Error", "Failed", "Faulted", "Offline", "Stopped", "Unhealthy"],
        StringComparer.OrdinalIgnoreCase);

    public static AgentCloudHealthAssessmentOutput Assess(
        AgentTaskPlanDocument plan,
        AgentTaskRunState state,
        IReadOnlyCollection<AgentEvidenceRecord> inputEvidence)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(inputEvidence);

        if (inputEvidence.Count != 1)
        {
            throw new AgentToolExecutionException(
                AppProblemCodes.AgentNodeRunStateConflict,
                "Current health assessment requires exactly one sealed Cloud device-status Evidence input.");
        }

        var parent = inputEvidence.Single();
        var sourceState = RestoreParentState(parent);
        var parentNode = plan.Nodes?.SingleOrDefault(node =>
            string.Equals(node.NodeId, parent.NodeId, StringComparison.Ordinal));
        var frozenIntent = parentNode?.Input?.SemanticIntent is { } semanticIntent &&
                           parentNode.Input.SemanticPlanDigest is { } semanticPlanDigest
            ? plan.CloudReadonlyIntents?.SingleOrDefault(intent =>
                string.Equals(intent.Intent, semanticIntent, StringComparison.Ordinal) &&
                string.Equals(intent.SemanticPlanDigest, semanticPlanDigest, StringComparison.Ordinal))
            : null;
        var restoredSnapshot = frozenIntent is null
            ? null
            : sourceState.CloudReadonlyResults.SingleOrDefault(result =>
                string.Equals(result.Intent, frozenIntent.Intent, StringComparison.Ordinal) &&
                string.Equals(result.SemanticPlanDigest, frozenIntent.SemanticPlanDigest, StringComparison.Ordinal));
        var sourceRows = restoredSnapshot?.Rows ?? sourceState.CloudReadonlyRows;
        var sourceMode = restoredSnapshot?.SourceMode ?? sourceState.CloudReadonlySourceMode;
        var sourceIsSimulation = restoredSnapshot?.IsSimulation ?? sourceState.CloudReadonlyIsSimulation;
        var sourceRowCount = restoredSnapshot?.RowCount ?? sourceState.CloudReadonlyRowCount;
        var sourceIsTruncated = restoredSnapshot?.IsTruncated ?? sourceState.CloudReadonlyIsTruncated;
        var sourceQueriedAtUtc = restoredSnapshot?.QueriedAtUtc ?? sourceState.CloudReadonlyQueriedAtUtc;
        if (
            parent.EvidenceKind != AgentEvidenceKind.DataQuery ||
            parent.TruthClass != AgentEvidenceTruthClass.ObservedFact ||
            !TryReadSealedParent(parent, out var parentDocument) ||
            parentDocument is null ||
            !string.Equals(parentDocument.Producer.NodeKind, "CloudReadNode", StringComparison.Ordinal) ||
            !string.Equals(parentDocument.Source.Provider, "CloudAiRead", StringComparison.Ordinal) ||
            !string.Equals(parentDocument.Source.SemanticIntent, "Analysis.Device.Status", StringComparison.Ordinal) ||
            !string.Equals(frozenIntent?.Intent, "Analysis.Device.Status", StringComparison.Ordinal) ||
            (sourceState.CloudReadonlyResults.Count != 0 && restoredSnapshot is null) ||
            parentDocument.Quality.RowCount != sourceRowCount ||
            parentDocument.Quality.IsTruncated != sourceIsTruncated ||
            parentDocument.Source.IsSimulation != sourceIsSimulation ||
            !string.Equals(parentDocument.Source.SourceMode, sourceMode, StringComparison.Ordinal))
        {
            throw new AgentToolExecutionException(
                AppProblemCodes.AgentNodeRunStateConflict,
                "Current health assessment requires one sealed Cloud device-status Evidence input matching the restored checkpoint.");
        }

        var sourceAsOfUtc = (parentDocument.Source.AsOfUtc ??
                             sourceQueriedAtUtc ??
                             parentDocument.CreatedAtUtc).ToUniversalTime();
        var rows = sourceRows;
        var rowCount = rows.Count;

        var missingHeartbeatCount = 0;
        var staleHeartbeatCount = 0;
        var futureHeartbeatCount = 0;
        var unknownRuntimeStatusCount = 0;
        var reportedIssueStatusCount = 0;
        foreach (var row in rows)
        {
            if (!TryGetDateTimeOffset(row, "lastRuntimeHeartbeatAtUtc", out var heartbeat))
            {
                missingHeartbeatCount++;
            }
            else if (heartbeat > sourceAsOfUtc + FutureClockTolerance)
            {
                futureHeartbeatCount++;
            }
            else if (sourceAsOfUtc - heartbeat > HeartbeatStaleAfter)
            {
                staleHeartbeatCount++;
            }

            var runtimeStatus = ReadString(row, "runtimeStatus");
            if (string.IsNullOrWhiteSpace(runtimeStatus) ||
                string.Equals(runtimeStatus, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                unknownRuntimeStatusCount++;
            }
            else if (ReportedIssueStatuses.Contains(runtimeStatus))
            {
                reportedIssueStatusCount++;
            }
        }

        var denominator = Math.Max(1, rowCount);
        var missingRate = rowCount == 0
            ? 1d
            : (double)missingHeartbeatCount / denominator;
        var staleRate = (double)staleHeartbeatCount / denominator;
        var futureRate = (double)futureHeartbeatCount / denominator;
        var unknownStatusRate = (double)unknownRuntimeStatusCount / denominator;
        var issueStatusRate = (double)reportedIssueStatusCount / denominator;
        var confidence = rowCount == 0
            ? 0d
            : Math.Clamp(
                (1d - missingRate) *
                (sourceIsTruncated ? 0.8d : 1d) *
                (futureHeartbeatCount > 0 ? 0.9d : 1d),
                0d,
                1d);
        var dataInsufficient = rowCount == 0 || missingRate >= 0.5d;
        var score = dataInsufficient
            ? 0
            : Math.Clamp(
                (int)Math.Round(
                    100d -
                    55d * staleRate -
                    50d * issueStatusRate -
                    35d * missingRate -
                    20d * futureRate -
                    10d * unknownStatusRate,
                    MidpointRounding.AwayFromZero),
                0,
                100);
        var level = dataInsufficient
            ? "DataInsufficient"
            : score >= 85
                ? "Stable"
                : score >= 60
                    ? "Watch"
                    : "Attention";
        var findings = BuildFindings(
            rowCount,
            missingHeartbeatCount,
            staleHeartbeatCount,
            futureHeartbeatCount,
            reportedIssueStatusCount,
            sourceIsTruncated);
        var safeSummary = dataInsufficient
            ? $"当前运行健康评估为数据不足；已评估 {rowCount} 台设备，结果不包含故障概率、剩余寿命或自动维修结论。"
            : $"当前运行健康等级为 {level}，固定算法得分 {score}/100；已评估 {rowCount} 台设备。";
        if (!AgentEvidenceSetDigestAuthority.TryComputeEffective(
                inputEvidence,
                out var evidenceSetDigest) ||
            evidenceSetDigest is null)
        {
            throw new AgentToolExecutionException(
                AppProblemCodes.AgentNodeRunStateConflict,
                "Current health assessment could not seal its input Evidence set digest.");
        }

        var output = new AgentCloudHealthAssessmentOutput(
            "completed",
            "cloud-health-assessment",
            AlgorithmVersion,
            "CurrentDeviceRuntimeHealth",
            "DerivedFact",
            score,
            level,
            safeSummary,
            findings,
            confidence,
            missingRate,
            1,
            evidenceSetDigest,
            sourceAsOfUtc,
            sourceMode!,
            sourceIsSimulation,
            rowCount,
            sourceIsTruncated,
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["futureHeartbeatCount"] = futureHeartbeatCount,
                ["missingHeartbeatCount"] = missingHeartbeatCount,
                ["reportedIssueStatusCount"] = reportedIssueStatusCount,
                ["staleHeartbeatCount"] = staleHeartbeatCount,
                ["totalDeviceCount"] = rowCount,
                ["unknownRuntimeStatusCount"] = unknownRuntimeStatusCount
            });
        CopyCloudCheckpoint(sourceState, state, restoredSnapshot);
        state.CloudHealthAssessment = output;
        return output;
    }

    private static AgentTaskRunState RestoreParentState(AgentEvidenceRecord parent)
    {
        if (parent.StorageMode != AgentEvidenceStorageMode.InlineCanonicalJson ||
            string.IsNullOrWhiteSpace(parent.InlinePayloadJson))
        {
            throw new AgentToolExecutionException(
                AppProblemCodes.AgentNodeRunStateConflict,
                "Current health assessment requires an inline authorized Cloud Evidence checkpoint.");
        }

        try
        {
            var sourceState = new AgentTaskRunState();
            _ = AgentTaskRunStateCheckpointCodec.RestoreEvidencePayload(
                sourceState,
                parent.InlinePayloadJson);
            return sourceState;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw new AgentToolExecutionException(
                AppProblemCodes.AgentNodeRunStateConflict,
                "Current health assessment could not restore its authorized Cloud Evidence checkpoint.");
        }
    }

    private static void CopyCloudCheckpoint(
        AgentTaskRunState source,
        AgentTaskRunState destination,
        AgentCloudReadonlyQuerySnapshot? selectedSnapshot)
    {
        destination.CloudReadonlySummary = selectedSnapshot?.Summary ?? source.CloudReadonlySummary;
        destination.CloudReadonlyRows = selectedSnapshot?.Rows ?? source.CloudReadonlyRows;
        destination.CloudReadonlySourceLabel = selectedSnapshot?.SourceLabel ?? source.CloudReadonlySourceLabel;
        destination.CloudReadonlySourcePath = null;
        destination.CloudReadonlySourceMode = selectedSnapshot?.SourceMode ?? source.CloudReadonlySourceMode;
        destination.CloudReadonlyIsSimulation = selectedSnapshot?.IsSimulation ?? source.CloudReadonlyIsSimulation;
        destination.CloudReadonlyRowCount = selectedSnapshot?.RowCount ?? source.CloudReadonlyRowCount;
        destination.CloudReadonlyIsTruncated = selectedSnapshot?.IsTruncated ?? source.CloudReadonlyIsTruncated;
        destination.CloudReadonlyQueriedAtUtc = selectedSnapshot?.QueriedAtUtc ?? source.CloudReadonlyQueriedAtUtc;
        foreach (var snapshot in source.CloudReadonlyResults)
        {
            destination.CloudReadonlyResults.RemoveAll(existing =>
                string.Equals(existing.Intent, snapshot.Intent, StringComparison.Ordinal) &&
                string.Equals(existing.SemanticPlanDigest, snapshot.SemanticPlanDigest, StringComparison.Ordinal));
            destination.CloudReadonlyResults.Add(snapshot);
        }
        destination.CloudReadonlyResults.Sort((left, right) =>
        {
            var comparison = StringComparer.Ordinal.Compare(left.Intent, right.Intent);
            return comparison != 0
                ? comparison
                : StringComparer.Ordinal.Compare(left.SemanticPlanDigest, right.SemanticPlanDigest);
        });
    }

    private static IReadOnlyCollection<string> BuildFindings(
        int rowCount,
        int missingHeartbeatCount,
        int staleHeartbeatCount,
        int futureHeartbeatCount,
        int reportedIssueStatusCount,
        bool isTruncated)
    {
        var findings = new List<string>
        {
            $"固定算法输入包含 {rowCount} 台设备的 Cloud 只读当前状态记录。"
        };
        if (reportedIssueStatusCount > 0)
        {
            findings.Add($"Cloud 明确上报非正常运行状态 {reportedIssueStatusCount} 条。");
        }

        if (staleHeartbeatCount > 0)
        {
            findings.Add($"心跳超过 15 分钟 {staleHeartbeatCount} 条；心跳陈旧不等同于设备离线或停机。");
        }

        if (missingHeartbeatCount > 0)
        {
            findings.Add($"缺少可解析心跳时间 {missingHeartbeatCount} 条。");
        }

        if (futureHeartbeatCount > 0)
        {
            findings.Add($"心跳时钟超前评估时点超过 5 分钟 {futureHeartbeatCount} 条，已降低置信度。");
        }

        if (isTruncated)
        {
            findings.Add("输入数据已截断，评估只覆盖当前返回范围。");
        }

        findings.Add("本结果仅是当前状态与数据质量的确定性评估，不提供故障概率、剩余寿命或自动维修结论。");
        return findings;
    }

    private static bool TryReadSealedParent(
        AgentEvidenceRecord evidence,
        out AgentEvidenceEnvelopeDocument? document)
    {
        document = null;
        try
        {
            var parsed = JsonSerializer.Deserialize<AgentEvidenceEnvelopeDocument>(
                evidence.CanonicalEnvelopeJson,
                CanonicalJson.SerializerOptions);
            if (parsed is null)
            {
                return false;
            }

            var sealedEnvelope = AgentEvidenceCanonicalizer.Seal(parsed);
            if (!sealedEnvelope.IsSuccess ||
                !string.Equals(sealedEnvelope.Value!.CanonicalJson, evidence.CanonicalEnvelopeJson, StringComparison.Ordinal) ||
                !string.Equals(sealedEnvelope.Value.Digest, evidence.EnvelopeDigest, StringComparison.Ordinal))
            {
                return false;
            }

            document = sealedEnvelope.Value.Document;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool TryGetDateTimeOffset(
        IReadOnlyDictionary<string, object?> row,
        string key,
        out DateTimeOffset value)
    {
        value = default;
        if (!row.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        switch (raw)
        {
            case DateTimeOffset offset:
                value = offset.ToUniversalTime();
                return true;
            case DateTime date:
                value = new DateTimeOffset(
                    date.Kind == DateTimeKind.Unspecified
                        ? DateTime.SpecifyKind(date, DateTimeKind.Utc)
                        : date).ToUniversalTime();
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.String &&
                                          element.TryGetDateTimeOffset(out var parsedElement):
                value = parsedElement.ToUniversalTime();
                return true;
            default:
                var text = Convert.ToString(raw, CultureInfo.InvariantCulture);
                if (DateTimeOffset.TryParse(
                        text,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var parsed))
                {
                    value = parsed;
                    return true;
                }

                return false;
        }
    }

    private static string? ReadString(
        IReadOnlyDictionary<string, object?> row,
        string key)
    {
        if (!row.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        return raw is JsonElement { ValueKind: JsonValueKind.String } element
            ? element.GetString()
            : Convert.ToString(raw, CultureInfo.InvariantCulture);
    }
}
