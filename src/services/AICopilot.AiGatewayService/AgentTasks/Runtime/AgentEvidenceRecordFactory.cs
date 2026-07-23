using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed record AgentEvidenceRecordAuthority(
    Guid? TenantId,
    Guid UserId,
    SessionId SessionId,
    AgentTaskId TaskId,
    AgentTaskRunAttemptId RunAttemptId,
    AgentNodeRunId NodeRunId,
    string NodeId,
    long TaskFencingToken,
    long NodeFencingToken)
{
    public static AgentEvidenceRecordAuthority From(
        DurableTaskClaim taskClaim,
        AgentNodeRunClaim nodeClaim,
        string nodeId)
    {
        return new AgentEvidenceRecordAuthority(
            TenantId: null,
            taskClaim.Task.UserId,
            taskClaim.Task.SessionId,
            taskClaim.Task.Id,
            taskClaim.RunAttempt.Id,
            nodeClaim.NodeRun.Id,
            nodeId,
            taskClaim.TaskFencingToken,
            nodeClaim.NodeFencingToken);
    }

    public string[] CreateAllowedConsumerScope()
    {
        return
        [
            $"session:{SessionId.Value:D}",
            $"task:{TaskId.Value:D}",
            $"user:{UserId:D}"
        ];
    }
}

internal sealed record AgentEvidenceEnvelopeDraft(
    AgentEvidenceKind EvidenceKind,
    AgentEvidenceTruthClass TruthClass,
    AgentEvidenceProducerDocument Producer,
    AgentEvidenceSourceDocument Source,
    AgentEvidenceQualityDocument Quality,
    AgentEvidencePayloadDocument Payload,
    AgentEvidenceContentDocument Content,
    AgentEvidenceLineageDocument Lineage,
    AgentEvidencePredictionDocument? Prediction,
    DateTimeOffset CreatedAtUtc);

internal sealed record AgentEvidenceRecordPayload(
    AgentEvidenceStorageMode StorageMode,
    string OutputDigest,
    string? InlinePayloadJson,
    string? PayloadRef,
    string MediaType,
    int ByteLength,
    string PayloadSha256);

internal sealed record AgentRunUsageDraft(
    int ModelCalls,
    int ToolCalls,
    long ElapsedMilliseconds,
    int ArtifactCount,
    long ArtifactBytes,
    string CorrelationHash,
    DateTimeOffset CreatedAtUtc);

internal static class AgentEvidenceRecordFactory
{
    public static Result<AgentEvidenceRecord> Seal(
        AgentEvidenceRecordId evidenceId,
        AgentEvidenceRecordAuthority authority,
        AgentEvidenceEnvelopeDraft draft,
        AgentEvidenceRecordPayload payload)
    {
        var allowedConsumerScope = authority.CreateAllowedConsumerScope()
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var document = new AgentEvidenceEnvelopeDocument(
            AgentPlanContractVersions.EvidenceV1,
            evidenceId.Value,
            authority.TenantId,
            authority.UserId,
            authority.SessionId.Value,
            authority.TaskId.Value,
            authority.RunAttemptId.Value,
            authority.NodeId,
            draft.EvidenceKind.ToString(),
            draft.TruthClass.ToString(),
            draft.Producer,
            draft.Source,
            draft.Quality,
            draft.Payload,
            draft.Content,
            draft.Lineage,
            new AgentEvidenceGovernanceDocument(
                "Internal",
                "Redacted",
                allowedConsumerScope,
                "TaskLifetime"),
            draft.Prediction,
            draft.CreatedAtUtc,
            Digest: string.Empty);
        var sealedEnvelope = AgentEvidenceCanonicalizer.Seal(document);
        if (!sealedEnvelope.IsSuccess)
        {
            return Result.From(sealedEnvelope);
        }

        var canonical = sealedEnvelope.Value!;
        return Result.Success(new AgentEvidenceRecord(
            evidenceId,
            authority.TenantId,
            authority.UserId,
            authority.SessionId,
            authority.TaskId,
            authority.RunAttemptId,
            authority.NodeRunId,
            authority.NodeId,
            draft.EvidenceKind,
            draft.TruthClass,
            payload.StorageMode,
            canonical.CanonicalJson,
            canonical.Digest,
            payload.OutputDigest,
            payload.InlinePayloadJson,
            payload.PayloadRef,
            payload.MediaType,
            payload.ByteLength,
            payload.PayloadSha256,
            CanonicalJson.Serialize(allowedConsumerScope),
            authority.TaskFencingToken,
            authority.NodeFencingToken,
            draft.CreatedAtUtc));
    }
}

internal static class AgentNormalizedNodeCheckpointFactory
{
    public static Result<AgentNormalizedNodeCheckpoint> Create(
        Result<AgentEvidenceRecord> evidenceResult,
        AgentEvidenceRecordAuthority authority,
        string costCurrency,
        AgentRunUsageDraft usage,
        string outputDigest)
    {
        if (!evidenceResult.IsSuccess)
        {
            return Result.From(evidenceResult);
        }

        var ledgerEntry = new AgentRunUsageLedgerEntry(
            authority.TaskId,
            authority.RunAttemptId,
            authority.NodeRunId,
            authority.TaskFencingToken,
            authority.NodeFencingToken,
            inputTokens: 0,
            outputTokens: 0,
            usage.ModelCalls,
            usage.ToolCalls,
            usage.ElapsedMilliseconds,
            costAmount: 0m,
            usage.ArtifactCount,
            usage.ArtifactBytes,
            costCurrency,
            usage.CorrelationHash,
            usage.CreatedAtUtc);
        return Result.Success(new AgentNormalizedNodeCheckpoint(
            evidenceResult.Value!,
            ledgerEntry,
            outputDigest));
    }
}

internal static class AgentArtifactFileSetEvidenceDocuments
{
    public static AgentEvidenceQualityDocument CreateQuality(int fileCount, bool humanApproved)
    {
        var flags = humanApproved
            ? new[] { "file-set-manifest-verified", "human-approved-final-output" }
            : new[] { "file-set-manifest-verified" };
        return new AgentEvidenceQualityDocument(
            RowCount: fileCount,
            IsTruncated: false,
            Freshness: "manifest-verified",
            MissingRate: 0,
            Confidence: 1,
            QualityFlags: flags);
    }

    public static AgentEvidencePayloadDocument CreatePayload(
        string payloadRef,
        int payloadBytes,
        string manifestDigest)
    {
        return new AgentEvidencePayloadDocument(
            AgentPlanContractVersions.ArtifactReferenceEvidencePolicyV1,
            AgentEvidenceStorageMode.ArtifactReference.ToString(),
            payloadRef,
            "application/vnd.aicopilot.artifact-file-set+json",
            payloadBytes,
            manifestDigest,
            IsComplete: true,
            InlineCanonicalJson: null);
    }

    public static AgentEvidenceContentDocument CreateContent(
        string safeSummary,
        int fileCount,
        decimal totalFileBytes,
        IReadOnlyCollection<string> artifactRefs)
    {
        return new AgentEvidenceContentDocument(
            safeSummary,
            new Dictionary<string, decimal>
            {
                ["fileCount"] = fileCount,
                ["payloadBytes"] = totalFileBytes
            },
            Findings: [],
            CitationRefs: [],
            artifactRefs);
    }
}
