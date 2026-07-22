using System.Text;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed record AgentNormalizedNodeCheckpoint(
    AgentEvidenceRecord Evidence,
    AgentRunUsageLedgerEntry Usage,
    string OutputDigest);

internal static class AgentEvidenceNormalizer
{
    public static Result<AgentNormalizedNodeCheckpoint> Normalize(
        DurableTaskClaim taskClaim,
        AgentNodeRunClaim nodeClaim,
        AgentPlanNodeDocument nodeContract,
        ToolRegistration tool,
        AgentStep step,
        AgentToolExecutionResult executionResult,
        AgentTaskRunState state,
        ArtifactWorkspace workspace,
        IReadOnlyCollection<AgentEvidenceRecord> parentEvidence,
        TimeSpan elapsed,
        DateTimeOffset nowUtc)
    {
        var payloadJson = AgentTaskRunStateCheckpointCodec.CaptureEvidencePayload(
            state,
            executionResult.DurableOutput.CanonicalJson);
        var payloadBytes = Encoding.UTF8.GetByteCount(payloadJson);
        if (payloadBytes > AgentPlanContractVersions.MaxInlineEvidenceCanonicalBytes)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.EvidencePayloadTooLarge,
                $"Inline Evidence canonical payload is {payloadBytes} UTF-8 bytes; maximum is {AgentPlanContractVersions.MaxInlineEvidenceCanonicalBytes}."));
        }

        var payloadDigest = CanonicalJson.ComputeSha256(payloadJson);
        var parentIds = parentEvidence
            .Where(evidence => nodeContract.DependsOn.Contains(evidence.NodeId, StringComparer.Ordinal))
            .Select(evidence => evidence.Id.Value)
            .Distinct()
            .Order()
            .ToArray();
        var evidenceKind = ResolveEvidenceKind(nodeContract.NodeKind, parentIds.Length);
        var truthClass = evidenceKind switch
        {
            AgentEvidenceKind.DataQuery or AgentEvidenceKind.RagCitation or AgentEvidenceKind.UploadedFile =>
                AgentEvidenceTruthClass.ObservedFact,
            AgentEvidenceKind.DerivedMetric => AgentEvidenceTruthClass.DerivedFact,
            AgentEvidenceKind.LlmInference => AgentEvidenceTruthClass.LlmInference,
            AgentEvidenceKind.ModelPrediction => AgentEvidenceTruthClass.ModelPrediction,
            _ => AgentEvidenceTruthClass.Recommendation
        };
        if (truthClass == AgentEvidenceTruthClass.DerivedFact && parentIds.Length == 0)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentPlanInvalid,
                "DerivedFact node checkpoint requires parent Evidence."));
        }

        var provider = string.Equals(nodeContract.NodeKind, "CloudReadNode", StringComparison.Ordinal)
            ? "CloudAiRead"
            : nodeContract.NodeKind == "GovernedDataReadNode"
                ? state.CloudReadonlySourceMode is "CloudReadOnly"
                    ? "GovernedTextToSql"
                    : "GovernedDirectDb"
                : tool.ProviderType.ToString();
        var semanticIntent = nodeContract.Input?.SemanticIntent;
        var providerOperationCode = provider == "CloudAiRead" &&
                                    semanticIntent?.StartsWith("Analysis.", StringComparison.Ordinal) == true
            ? $"CloudAiRead.{semanticIntent["Analysis.".Length..]}"
            : tool.ToolCode;
        var evidenceId = AgentEvidenceRecordId.New();
        var artifactRefs = workspace.Artifacts
            .Where(artifact => artifact.CreatedByStepId is not null)
            .Select(artifact => artifact.Id.Value.ToString("D"))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var allowedConsumerScope = new[]
        {
            $"session:{taskClaim.Task.SessionId.Value:D}",
            $"task:{taskClaim.Task.Id.Value:D}",
            $"user:{taskClaim.Task.UserId:D}"
        }.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var document = new AgentEvidenceEnvelopeDocument(
            AgentPlanContractVersions.EvidenceV1,
            evidenceId.Value,
            TenantId: null,
            taskClaim.Task.UserId,
            taskClaim.Task.SessionId.Value,
            taskClaim.Task.Id.Value,
            taskClaim.RunAttempt.Id.Value,
            nodeContract.NodeId,
            evidenceKind.ToString(),
            truthClass.ToString(),
            new AgentEvidenceProducerDocument(
                nodeContract.NodeKind,
                $"built-in:{tool.ToolCode}",
                tool.ToolCode,
                CanonicalJson.ComputeSha256(CanonicalJson.Canonicalize(tool.OutputSchemaJson)),
                taskClaim.Task.ModelId?.Value,
                ModelVersion: null,
                taskClaim.Task.PlanJson.Length > 0 ? "snapshot-bound" : null),
            new AgentEvidenceSourceDocument(
                nodeContract.NodeKind,
                $"opaque:{payloadDigest[..16]}",
                state.CloudReadonlySourceMode ?? tool.ProviderType.ToString(),
                state.CloudReadonlyIsSimulation,
                nowUtc,
                nowUtc,
                TimeRange: null,
                nodeContract.Input?.RequestedScope.OrderBy(value => value, StringComparer.Ordinal).ToArray() ?? [],
                provider,
                providerOperationCode,
                semanticIntent,
                nodeContract.Input?.RequestedScope.OrderBy(value => value, StringComparer.Ordinal).ToArray() ?? []),
            new AgentEvidenceQualityDocument(
                state.CloudReadonlyRowCount,
                state.CloudReadonlyIsTruncated,
                "checkpoint-current",
                MissingRate: null,
                Confidence: null,
                QualityFlags: []),
            new AgentEvidencePayloadDocument(
                AgentPlanContractVersions.InlineEvidencePolicyV1,
                AgentEvidenceStorageMode.InlineCanonicalJson.ToString(),
                PayloadRef: null,
                "application/json",
                payloadBytes,
                payloadDigest,
                IsComplete: true,
                payloadJson),
            new AgentEvidenceContentDocument(
                $"Node '{nodeContract.NodeId}' produced validated {evidenceKind} evidence.",
                new Dictionary<string, decimal>
                {
                    ["rowCount"] = state.CloudReadonlyRowCount
                },
                Findings: [],
                CitationRefs: [],
                artifactRefs),
            new AgentEvidenceLineageDocument(
                parentIds,
                nodeClaim.NodeRun.InputDigest,
                payloadDigest),
            new AgentEvidenceGovernanceDocument(
                "Internal",
                "Redacted",
                allowedConsumerScope,
                "TaskLifetime"),
            Prediction: null,
            nowUtc,
            Digest: string.Empty);
        var sealedEnvelope = AgentEvidenceCanonicalizer.Seal(document);
        if (!sealedEnvelope.IsSuccess)
        {
            return Result.From(sealedEnvelope);
        }

        var canonical = sealedEnvelope.Value!;
        var evidence = new AgentEvidenceRecord(
            evidenceId,
            tenantId: null,
            taskClaim.Task.UserId,
            taskClaim.Task.SessionId,
            taskClaim.Task.Id,
            taskClaim.RunAttempt.Id,
            nodeClaim.NodeRun.Id,
            nodeContract.NodeId,
            evidenceKind,
            truthClass,
            AgentEvidenceStorageMode.InlineCanonicalJson,
            canonical.CanonicalJson,
            canonical.Digest,
            payloadDigest,
            payloadJson,
            payloadRef: null,
            "application/json",
            payloadBytes,
            payloadDigest,
            CanonicalJson.Serialize(allowedConsumerScope),
            taskClaim.TaskFencingToken,
            nodeClaim.NodeFencingToken,
            nowUtc: nowUtc);
        var producedArtifacts = workspace.Artifacts
            .Where(artifact => artifact.CreatedByStepId == step.Id)
            .ToArray();
        var usage = new AgentRunUsageLedgerEntry(
            taskClaim.Task.Id,
            taskClaim.RunAttempt.Id,
            nodeClaim.NodeRun.Id,
            taskClaim.TaskFencingToken,
            nodeClaim.NodeFencingToken,
            inputTokens: 0,
            outputTokens: 0,
            modelCalls: 0,
            toolCalls: 1,
            elapsedMilliseconds: Math.Min(
                Math.Max(0, (long)elapsed.TotalMilliseconds),
                nodeClaim.NodeRun.ReservedElapsedMilliseconds),
            costAmount: 0m,
            artifactCount: producedArtifacts.Length,
            artifactBytes: producedArtifacts.Sum(artifact => artifact.FileSize),
            costCurrency: taskClaim.RunAttempt.BudgetCostCurrency,
            correlationHash: CanonicalJson.ComputeSha256(CanonicalJson.Serialize(new
            {
                taskClaim.TaskFencingToken,
                nodeClaim.NodeFencingToken,
                nodeId = nodeContract.NodeId,
                payloadDigest
            })),
            nowUtc);
        return Result.Success(new AgentNormalizedNodeCheckpoint(evidence, usage, payloadDigest));
    }

    private static AgentEvidenceKind ResolveEvidenceKind(string nodeKind, int parentCount)
    {
        return nodeKind switch
        {
            "CloudReadNode" or "GovernedDataReadNode" => AgentEvidenceKind.DataQuery,
            "KnowledgeRetrievalNode" => AgentEvidenceKind.RagCitation,
            "FileAnalysisNode" => AgentEvidenceKind.UploadedFile,
            "DeterministicComputeNode" when parentCount > 0 => AgentEvidenceKind.DerivedMetric,
            "PolicyValidationNode" => AgentEvidenceKind.PolicyDecision,
            _ => AgentEvidenceKind.DerivedMetric
        };
    }
}
