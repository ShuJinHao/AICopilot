using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Tools;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed record AgentReasoningModelResult(
    string CompletionStatus,
    string SafeSummary,
    IReadOnlyCollection<string> Findings,
    double Confidence,
    bool NoFurtherToolCalls);

internal sealed record AgentReasoningToolOutput(
    string Status,
    string ResultType,
    Guid ChildRunId,
    string CompletionStatus,
    string TruthClass,
    string SafeSummary,
    IReadOnlyCollection<string> Findings,
    IReadOnlyCollection<string> CitationRefs,
    IReadOnlyCollection<string> EvidenceWarnings,
    string ConflictStatus,
    double Confidence,
    bool NoFurtherToolCalls,
    bool RecoveryUsed,
    int ModelCalls);

internal static class AgentReasoningOutputAuthority
{
    public const int MaxSafeSummaryCharacters = 2_000;
    public const int MaxFindings = 12;
    public const int MaxFindingCharacters = 500;

    public static bool TryNormalize(
        AgentReasoningModelResult? result,
        Guid childRunId,
        bool recoveryUsed,
        int modelCalls,
        AgentReasoningEvidenceProfile evidenceProfile,
        out AgentReasoningToolOutput? output)
    {
        output = null;
        if (result is null ||
            evidenceProfile is null ||
            childRunId == Guid.Empty ||
            modelCalls is < 1 or > 2 ||
            !string.Equals(result.CompletionStatus, "Completed", StringComparison.Ordinal) ||
            !result.NoFurtherToolCalls ||
            string.IsNullOrWhiteSpace(result.SafeSummary) ||
            result.SafeSummary.Length > MaxSafeSummaryCharacters ||
            result.SafeSummary != result.SafeSummary.Trim() ||
            result.Findings is null ||
            result.Findings.Count > MaxFindings ||
            result.Findings.Any(finding =>
                string.IsNullOrWhiteSpace(finding) ||
                finding.Length > MaxFindingCharacters ||
                finding != finding.Trim()) ||
            evidenceProfile.CitationRefs.Count is < 1 or > 8 ||
            !evidenceProfile.CitationRefs.SequenceEqual(
                AgentPlanCanonicalCollections.Strings(evidenceProfile.CitationRefs),
                StringComparer.Ordinal) ||
            evidenceProfile.EvidenceWarnings.Count > 8 ||
            !evidenceProfile.EvidenceWarnings.SequenceEqual(
                AgentPlanCanonicalCollections.Strings(evidenceProfile.EvidenceWarnings),
                StringComparer.Ordinal) ||
            evidenceProfile.ConflictStatus is not ("None" or "PotentialConflict") ||
            double.IsNaN(result.Confidence) ||
            double.IsInfinity(result.Confidence) ||
            result.Confidence is < 0 or > 1)
        {
            return false;
        }

        var safeSummary = ToolExecutionRecordSanitizer
            .Sanitize(result.SafeSummary, MaxSafeSummaryCharacters)?
            .Trim();
        var safeFindings = result.Findings
            .Select(finding => ToolExecutionRecordSanitizer
                .Sanitize(finding, MaxFindingCharacters)?
                .Trim())
            .ToArray();
        if (string.IsNullOrWhiteSpace(safeSummary) ||
            safeFindings.Any(string.IsNullOrWhiteSpace))
        {
            return false;
        }

        output = new AgentReasoningToolOutput(
            "completed",
            "agent-reasoning",
            childRunId,
            "Completed",
            AgentReasoningPolicyAuthority.OutputTruthClass,
            safeSummary,
            safeFindings.Select(finding => finding!).ToArray(),
            evidenceProfile.CitationRefs,
            evidenceProfile.EvidenceWarnings,
            evidenceProfile.ConflictStatus,
            result.Confidence,
            true,
            recoveryUsed,
            modelCalls);
        return true;
    }

    public static bool TryRead(
        string canonicalJson,
        out AgentReasoningToolOutput? output)
    {
        output = null;
        try
        {
            var parsed = JsonSerializer.Deserialize<AgentReasoningToolOutput>(
                canonicalJson,
                CanonicalJson.SerializerOptions);
            if (parsed is null ||
                parsed.Status != "completed" ||
                parsed.ResultType != "agent-reasoning" ||
                parsed.TruthClass != AgentReasoningPolicyAuthority.OutputTruthClass ||
                parsed.CompletionStatus != "Completed" ||
                !parsed.NoFurtherToolCalls ||
                parsed.CitationRefs is null ||
                parsed.EvidenceWarnings is null ||
                string.IsNullOrWhiteSpace(parsed.ConflictStatus))
            {
                return false;
            }

            var modelResult = new AgentReasoningModelResult(
                parsed.CompletionStatus,
                parsed.SafeSummary,
                parsed.Findings,
                parsed.Confidence,
                parsed.NoFurtherToolCalls);
            if (!TryNormalize(
                    modelResult,
                    parsed.ChildRunId,
                    parsed.RecoveryUsed,
                    parsed.ModelCalls,
                    new AgentReasoningEvidenceProfile(
                        parsed.CitationRefs,
                        parsed.EvidenceWarnings,
                        parsed.ConflictStatus),
                    out var normalized) ||
                !string.Equals(
                    CanonicalJson.Serialize(normalized),
                    canonicalJson,
                    StringComparison.Ordinal))
            {
                return false;
            }

            output = normalized;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return false;
        }
    }
}

internal sealed record AgentReasoningEvidenceProfile(
    IReadOnlyCollection<string> CitationRefs,
    IReadOnlyCollection<string> EvidenceWarnings,
    string ConflictStatus);

internal static class AgentReasoningEvidenceProfileAuthority
{
    private static readonly IReadOnlySet<string> NonComparableMetricCodes = new HashSet<string>(
        ["artifactCount", "fileCount", "itemCount", "payloadBytes", "rowCount"],
        StringComparer.Ordinal);

    public static AgentReasoningEvidenceProfile Create(
        IReadOnlyCollection<AgentEvidenceRecord> evidenceRecords)
    {
        if (evidenceRecords.Count is < 1 or > 8)
        {
            throw new AgentToolExecutionException(
                AppProblemCodes.AgentPlanInvalid,
                "AgentReasoningNode requires one to eight authorized Evidence inputs.");
        }

        var documents = evidenceRecords
            .OrderBy(evidence => evidence.NodeId, StringComparer.Ordinal)
            .Select(ReadSealedDocument)
            .ToArray();
        var citationRefs = AgentPlanCanonicalCollections.Strings(
            evidenceRecords.Select(CreateCitationRef));
        var warnings = new List<string>();
        if (documents.Any(document => document.Quality.IsTruncated))
        {
            warnings.Add("source-truncated");
        }

        if (documents.Any(document =>
                document.Quality.Freshness.Contains("stale", StringComparison.OrdinalIgnoreCase) ||
                document.Quality.Freshness.Contains("expired", StringComparison.OrdinalIgnoreCase) ||
                document.Quality.QualityFlags.Any(flag =>
                    flag.Contains("stale", StringComparison.OrdinalIgnoreCase) ||
                    flag.Contains("expired", StringComparison.OrdinalIgnoreCase))))
        {
            warnings.Add("source-stale");
        }

        if (documents.Any(document => document.Quality.MissingRate is > 0))
        {
            warnings.Add("missing-data");
        }

        if (documents.Any(document => document.Quality.Confidence is < 0.5))
        {
            warnings.Add("low-confidence");
        }

        if (documents.Any(document => document.Source.IsSimulation))
        {
            warnings.Add("simulation-evidence");
        }

        var hasMetricConflict = documents
            .SelectMany(document => document.Content.TypedMetrics
                .Where(metric => !NonComparableMetricCodes.Contains(metric.Key))
                .Select(metric => new
                {
                    Key = CanonicalJson.Serialize(new
                    {
                        document.Source.SemanticIntent,
                        document.Source.QueryScope,
                        document.Source.AsOfUtc,
                        MetricCode = metric.Key
                    }),
                    metric.Value
                }))
            .GroupBy(metric => metric.Key, StringComparer.Ordinal)
            .Any(group => group.Select(metric => metric.Value).Distinct().Skip(1).Any());
        var hasDeclaredConflict = documents.Any(document =>
            document.Quality.QualityFlags.Any(flag =>
                flag.Contains("conflict", StringComparison.OrdinalIgnoreCase)));
        var conflictStatus = hasMetricConflict || hasDeclaredConflict
            ? "PotentialConflict"
            : "None";
        if (conflictStatus == "PotentialConflict")
        {
            warnings.Add("metric-conflict");
        }

        return new AgentReasoningEvidenceProfile(
            citationRefs,
            AgentPlanCanonicalCollections.Strings(warnings),
            conflictStatus);
    }

    public static string ToSafeWarning(string warningCode) => warningCode switch
    {
        "source-truncated" => "输入证据包含已截断数据，结论仅适用于已返回范围。",
        "source-stale" => "输入证据包含过期或陈旧数据。",
        "missing-data" => "输入证据存在缺失数据。",
        "low-confidence" => "输入证据置信度较低。",
        "simulation-evidence" => "输入包含 Simulation 证据，不能冒充正式 Cloud 数据。",
        "metric-conflict" => "同一证据时点的同名确定性指标值不一致，结论保留冲突说明。",
        _ => "输入证据存在未分类质量告警。"
    };

    public static string CreateCitationRef(AgentEvidenceRecord evidence) =>
        $"evidence:{CanonicalJson.ComputeSha256(evidence.Id.Value.ToString("D"))[..16]}";

    private static AgentEvidenceEnvelopeDocument ReadSealedDocument(AgentEvidenceRecord evidence)
    {
        try
        {
            var document = JsonSerializer.Deserialize<AgentEvidenceEnvelopeDocument>(
                evidence.CanonicalEnvelopeJson,
                CanonicalJson.SerializerOptions)
                ?? throw new JsonException("Evidence envelope is missing.");
            var sealedEnvelope = AgentEvidenceCanonicalizer.Seal(document);
            if (!sealedEnvelope.IsSuccess ||
                !string.Equals(sealedEnvelope.Value!.CanonicalJson, evidence.CanonicalEnvelopeJson, StringComparison.Ordinal) ||
                !string.Equals(sealedEnvelope.Value.Digest, evidence.EnvelopeDigest, StringComparison.Ordinal))
            {
                throw new JsonException("Evidence envelope seal does not match its durable record.");
            }

            return sealedEnvelope.Value.Document;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new AgentToolExecutionException(
                AppProblemCodes.AgentNodeRunStateConflict,
                "AgentReasoningNode received an unreadable Evidence envelope.");
        }
    }
}

/// <summary>
/// A depth-1 child model run over safe Evidence summaries. It has a fresh runtime
/// session, no user conversation, no tools, no spawn surface, one normal turn and
/// at most one recovery turn. Only a system-owned LlmInference result is returned.
/// </summary>
internal sealed class AgentReasoningNodeExecutor(
    ConfiguredAgentRuntimeFactory agentFactory)
{
    private const int MaxEvidenceItems = 8;
    private const int MaxEvidenceSummaryCharacters = 1_200;
    private const int MaxEvidenceFindingCharacters = 300;

    public async Task<object> ExecuteAsync(AgentToolExecutionContext context)
    {
        var node = context.Plan.Nodes?.ElementAtOrDefault(context.Step.StepIndex - 1);
        var policy = node?.ModelPolicy;
        var inputEvidence = context.InputEvidence?.ToArray() ?? [];
        if (node is null ||
            node.NodeKind != "AgentReasoningNode" ||
            !AgentReasoningPolicyAuthority.Matches(policy) ||
            context.RunAttemptId is null ||
            context.NodeRunId is null ||
            inputEvidence.Length == 0 ||
            inputEvidence.Length > MaxEvidenceItems ||
            inputEvidence.Any(evidence =>
                evidence.TaskId != context.Task.Id ||
                evidence.RunAttemptId != context.RunAttemptId ||
                evidence.IsRevoked))
        {
            throw new AgentToolExecutionException(
                AppProblemCodes.AgentPlanInvalid,
                "AgentReasoningNode is missing its sealed depth-1 model policy or authorized Evidence input.");
        }

        var childRunId = CreateChildRunId(
            context.Task.Id,
            context.RunAttemptId.Value,
            context.NodeRunId.Value,
            node.NodeId);
        var evidenceProfile = AgentReasoningEvidenceProfileAuthority.Create(inputEvidence);
        var safeInput = BuildSafeInput(context, node, inputEvidence, childRunId, evidenceProfile);
        var safeInputJson = CanonicalJson.Serialize(safeInput);
        if (Encoding.UTF8.GetByteCount(safeInputJson) > AgentReasoningPolicyAuthority.MaxInputTokens * 4)
        {
            throw new AgentToolExecutionException(
                AppProblemCodes.AgentRunBudgetExceeded,
                "AgentReasoningNode safe Evidence context exceeds its frozen input budget.");
        }

        await using var scopedAgent = await agentFactory.CreateAgentAsync(
            AgentReasoningPolicyAuthority.TemplateCode,
            new LanguageModelId(policy!.ModelId!.Value),
            configureOptions: AgentReasoningPolicyAuthority.ConfigureOptions,
            cancellationToken: context.CancellationToken);
        if (!AgentReasoningPolicyAuthority.Matches(policy, scopedAgent.ConfigurationSnapshot))
        {
            throw new AgentToolExecutionException(
                AppProblemCodes.ApprovalReconfirmationRequired,
                "AgentReasoningNode prompt or model snapshot changed after plan confirmation.");
        }

        var session = await scopedAgent.Agent.CreateSessionAsync(context.CancellationToken);
        var modelCalls = 0;
        AgentReasoningModelResult? firstResult = null;
        try
        {
            modelCalls++;
            var first = await scopedAgent.Agent.RunStructuredAsync<AgentReasoningModelResult>(
                [new AiChatMessage(AiChatRole.User, safeInputJson)],
                session,
                AgentRuntimeJson.Options,
                options: null,
                cancellationToken: context.CancellationToken);
            firstResult = first.Result;
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new AgentToolExecutionException(
                AppProblemCodes.ChatStreamFailed,
                $"AgentReasoningNode model call failed before a valid completion: {exception.GetType().Name}.",
                modelCalls);
        }

        if (AgentReasoningOutputAuthority.TryNormalize(
                firstResult,
                childRunId,
                recoveryUsed: false,
                modelCalls: modelCalls,
                evidenceProfile: evidenceProfile,
                output: out var completed))
        {
            return completed!;
        }

        try
        {
            modelCalls++;
            var recovery = await scopedAgent.Agent.RunStructuredAsync<AgentReasoningModelResult>(
                [new AiChatMessage(
                    AiChatRole.User,
                    "Recovery turn 1/1. Return only a valid Completed structured result. Tools and further turns are disabled.")],
                session,
                AgentRuntimeJson.Options,
                options: null,
                cancellationToken: context.CancellationToken);
            if (AgentReasoningOutputAuthority.TryNormalize(
                    recovery.Result,
                    childRunId,
                    recoveryUsed: true,
                    modelCalls: modelCalls,
                    evidenceProfile: evidenceProfile,
                    output: out completed))
            {
                return completed!;
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new AgentToolExecutionException(
                AppProblemCodes.ChatStreamFailed,
                $"AgentReasoningNode recovery call failed: {exception.GetType().Name}.",
                modelCalls);
        }

        throw new AgentToolExecutionException(
            AppProblemCodes.ToolOutputSchemaInvalid,
            "AgentReasoningNode exhausted its single recovery turn without valid LlmInference Evidence.",
            modelCalls);
    }

    private static object BuildSafeInput(
        AgentToolExecutionContext context,
        AgentPlanNodeDocument node,
        IReadOnlyCollection<AgentEvidenceRecord> evidenceRecords,
        Guid childRunId,
        AgentReasoningEvidenceProfile evidenceProfile)
    {
        var summaries = evidenceRecords
            .OrderBy(evidence => evidence.NodeId, StringComparer.Ordinal)
            .Select(ReadSafeEvidenceSummary)
            .ToArray();
        var selectedNodeIds = evidenceRecords
            .Select(evidence => evidence.NodeId)
            .ToHashSet(StringComparer.Ordinal);
        return new
        {
            schemaVersion = "agent-reasoning-input:v1",
            childRunId,
            owner = new
            {
                taskId = context.Task.Id.Value,
                runAttemptId = context.RunAttemptId!.Value.Value,
                nodeRunId = context.NodeRunId!.Value.Value,
                nodeId = node.NodeId,
                derivationDepth = AgentReasoningPolicyAuthority.DerivationDepth
            },
            contextPolicy = AgentReasoningPolicyAuthority.ContextPolicy,
            goal = context.Plan.Goal,
            artifactTargets = context.Plan.ArtifactTargets,
            evidenceSelectors = node.EvidenceSelectors,
            missingOptionalSelectors = node.EvidenceSelectors
                .Where(selector => !selectedNodeIds.Contains(selector))
                .OrderBy(selector => selector, StringComparer.Ordinal)
                .ToArray(),
            allowedToolCodes = Array.Empty<string>(),
            outputTruthClass = AgentReasoningPolicyAuthority.OutputTruthClass,
            evidenceProfile.CitationRefs,
            evidenceProfile.EvidenceWarnings,
            evidenceProfile.ConflictStatus,
            evidence = summaries
        };
    }

    private static object ReadSafeEvidenceSummary(AgentEvidenceRecord evidence)
    {
        AgentEvidenceEnvelopeDocument document;
        try
        {
            document = JsonSerializer.Deserialize<AgentEvidenceEnvelopeDocument>(
                evidence.CanonicalEnvelopeJson,
                CanonicalJson.SerializerOptions)
                ?? throw new JsonException("Evidence envelope is missing.");
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new AgentToolExecutionException(
                AppProblemCodes.AgentNodeRunStateConflict,
                "AgentReasoningNode received an unreadable Evidence envelope.");
        }

        return new
        {
            citationRef = AgentReasoningEvidenceProfileAuthority.CreateCitationRef(evidence),
            document.NodeId,
            document.EvidenceKind,
            document.TruthClass,
            source = new
            {
                document.Source.Provider,
                document.Source.SourceMode,
                document.Source.IsSimulation,
                document.Source.AsOfUtc,
                document.Source.SanitizedScope
            },
            quality = new
            {
                document.Quality.RowCount,
                document.Quality.IsTruncated,
                document.Quality.Freshness,
                document.Quality.Confidence,
                document.Quality.QualityFlags
            },
            safeSummary = Bound(document.Content.SafeSummary, MaxEvidenceSummaryCharacters),
            findings = document.Content.Findings
                .Take(AgentReasoningOutputAuthority.MaxFindings)
                .Select(finding => Bound(finding, MaxEvidenceFindingCharacters))
                .ToArray(),
            document.Content.TypedMetrics,
            document.Content.CitationRefs
        };
    }

    private static Guid CreateChildRunId(
        AgentTaskId taskId,
        AgentTaskRunAttemptId runAttemptId,
        AgentNodeRunId nodeRunId,
        string nodeId)
    {
        var canonical = CanonicalJson.Serialize(new
        {
            policy = AgentReasoningPolicyAuthority.PolicyVersion,
            taskId = taskId.Value,
            runAttemptId = runAttemptId.Value,
            nodeRunId = nodeRunId.Value,
            nodeId
        });
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return new Guid(digest.AsSpan(0, 16));
    }

    private static string Bound(string value, int maxCharacters) =>
        value.Length <= maxCharacters ? value : value[..maxCharacters];
}
