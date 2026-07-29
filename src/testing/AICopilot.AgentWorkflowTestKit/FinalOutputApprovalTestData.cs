using System.Text;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.AgentWorkflowTestKit;

public sealed record FinalOutputApprovalTestFixture(
    AgentTaskRunQueueItem OriginalQueueItem,
    AgentTaskRunAttempt RunAttempt,
    IReadOnlyCollection<AgentNodeRun> NodeRuns,
    IReadOnlyCollection<AgentEvidenceRecord> Evidence,
    FinalOutputApprovalProof Proof,
    IAgentNodeRunStore NodeRunStore);

public static class FinalOutputApprovalTestData
{
    public static async Task<FinalOutputApprovalTestFixture> CreatePreApprovalAuthorityAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        IReadOnlyDictionary<Guid, byte[]> artifactContents,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(artifactContents);

        if (task.WorkspaceId != workspace.Id ||
            workspace.TaskId != task.Id ||
            task.ActiveRunAttemptId is not null ||
            task.Status is not (AgentTaskStatus.PlanApproved or AgentTaskStatus.Running) ||
            workspace.Artifacts.Count == 0 ||
            workspace.Artifacts.Any(artifact =>
                !artifactContents.TryGetValue(artifact.Id.Value, out var content) ||
                content.LongLength != artifact.FileSize))
        {
            throw new InvalidOperationException(
                "Final-output test authority requires an active workspace, completed producers, and exact source bytes.");
        }

        if (task.Status == AgentTaskStatus.PlanApproved)
        {
            task.Start(nowUtc);
        }

        var attempt = new AgentTaskRunAttempt(
            task.Id,
            task.RunAttemptCount + 1,
            AgentTaskRunTriggerType.Manual,
            "final-output-test-authority",
            nowUtc,
            TimeSpan.FromMinutes(10));
        task.BeginRunAttempt(
            attempt.Id,
            attempt.AttemptNo,
            attempt.LeaseId!.Value,
            attempt.LeaseOwner!,
            attempt.LeaseExpiresAt!.Value,
            nowUtc);
        attempt.BindTaskFencingToken(task.RunFencingToken);

        var queueItem = new AgentTaskRunQueueItem(
            task.Id,
            AgentTaskRunTriggerType.Manual,
            task.UserId,
            nowUtc);
        queueItem.AcquireLease(
            Guid.NewGuid(),
            "final-output-test-authority",
            nowUtc,
            TimeSpan.FromMinutes(10),
            task.RunFencingToken);
        queueItem.MarkStarted(attempt.Id, nowUtc);

        var nodeRuns = new List<AgentNodeRun>();
        var evidence = new List<AgentEvidenceRecord>();
        var producerNodeIds = new List<string>();
        var orderedArtifacts = workspace.Artifacts
            .OrderBy(artifact => artifact.Id.Value)
            .ToArray();
        foreach (var artifact in orderedArtifacts)
        {
            var step = task.Steps.Single(candidate => candidate.Id == artifact.CreatedByStepId);
            if (step.Status != AgentStepStatus.Completed)
            {
                throw new InvalidOperationException(
                    "Final-output test artifacts must be produced by completed steps.");
            }

            var nodeId = $"artifact-{step.StepIndex:D4}";
            producerNodeIds.Add(nodeId);
            var node = CreateNode(
                task,
                attempt,
                queueItem,
                nodeId,
                nodeKind: "ArtifactGenerationNode",
                step.ToolCode,
                dependenciesJson: "[]",
                requiresApproval: false,
                AgentNodeSideEffectClass.DeterministicInternal,
                joinPolicy: null,
                nowUtc);
            node.MakeRunnable(nowUtc);
            node.Claim(
                task.RunFencingToken,
                "final-output-test-authority",
                nowUtc,
                TimeSpan.FromMinutes(10));
            node.Start(task.RunFencingToken, node.NodeFencingToken, nowUtc);

            var sourceBytes = artifactContents[artifact.Id.Value];
            var inlinePayload = CanonicalJson.Serialize(new
            {
                artifactId = artifact.Id.Value,
                artifact.RelativePath,
                artifact.FileSize
            });
            var outputDigest = CanonicalJson.ComputeSha256(inlinePayload);
            var evidenceId = AgentEvidenceRecordId.New();
            var sealedEvidence = AgentEvidenceRecordFactory.Seal(
                evidenceId,
                new AgentEvidenceRecordAuthority(
                    TenantId: null,
                    task.UserId,
                    task.SessionId,
                    task.Id,
                    attempt.Id,
                    node.Id,
                    node.NodeId,
                    task.RunFencingToken,
                    node.NodeFencingToken),
                new AgentEvidenceEnvelopeDraft(
                    AgentEvidenceKind.DataQuery,
                    AgentEvidenceTruthClass.ObservedFact,
                    new AgentEvidenceProducerDocument(
                        node.NodeKind,
                        "test:final-output-authority",
                        node.ToolCode,
                        ToolSchemaHash: null,
                        ModelId: null,
                        ModelVersion: null,
                        PromptVersion: null),
                    new AgentEvidenceSourceDocument(
                        "TestFixture",
                        $"artifact:{artifact.Id.Value:N}",
                        "Test",
                        IsSimulation: true,
                        ObservedAtUtc: nowUtc,
                        AsOfUtc: nowUtc,
                        TimeRange: null,
                        SanitizedScope: [],
                        Provider: null,
                        ProviderOperationCode: null,
                        SemanticIntent: null,
                        QueryScope: []),
                    new AgentEvidenceQualityDocument(
                        RowCount: 1,
                        IsTruncated: false,
                        Freshness: "fixture",
                        MissingRate: 0,
                        Confidence: 1,
                        QualityFlags: []),
                    new AgentEvidencePayloadDocument(
                        AgentPlanContractVersions.InlineEvidencePolicyV1,
                        AgentEvidenceStorageMode.InlineCanonicalJson.ToString(),
                        PayloadRef: null,
                        MediaType: "application/json",
                        ByteLength: Encoding.UTF8.GetByteCount(inlinePayload),
                        Sha256: outputDigest,
                        IsComplete: true,
                        InlineCanonicalJson: inlinePayload),
                    new AgentEvidenceContentDocument(
                        "Test artifact producer completed.",
                        new Dictionary<string, decimal>
                        {
                            ["fileBytes"] = sourceBytes.LongLength
                        },
                        Findings: [],
                        CitationRefs: [],
                        ArtifactRefs: []),
                    new AgentEvidenceLineageDocument(
                        ParentEvidenceIds: [],
                        InputDigest: node.InputDigest,
                        OutputDigest: outputDigest),
                    Prediction: null,
                    nowUtc),
                new AgentEvidenceRecordPayload(
                    AgentEvidenceStorageMode.InlineCanonicalJson,
                    outputDigest,
                    inlinePayload,
                    PayloadRef: null,
                    MediaType: "application/json",
                    ByteLength: Encoding.UTF8.GetByteCount(inlinePayload),
                    PayloadSha256: outputDigest));
            if (!sealedEvidence.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Final-output test Evidence could not be sealed: {string.Join("; ", sealedEvidence.Errors ?? [])}");
            }

            var evidenceRecord = sealedEvidence.Value!;
            if (!AgentEvidenceSetDigestAuthority.TryComputeEffective(
                    [evidenceRecord],
                    out var nodeEvidenceSetDigest) ||
                nodeEvidenceSetDigest is null)
            {
                throw new InvalidOperationException(
                    "Final-output test EvidenceSetDigest could not be computed.");
            }

            node.CompleteCheckpoint(
                task.RunFencingToken,
                node.NodeFencingToken,
                evidenceRecord.Id,
                outputDigest,
                nodeEvidenceSetDigest,
                providerOperationCode: null,
                providerReceiptHash: null,
                nowUtc);
            nodeRuns.Add(node);
            evidence.Add(evidenceRecord);
        }

        var finalStep = AgentFinalizationCheckpointStateValidator.LoadExactFinalStep(task);
        if (!finalStep.IsSuccess)
        {
            throw new InvalidOperationException(
                "Final-output test task is missing its exact finalization step.");
        }

        var finalNode = CreateNode(
            task,
            attempt,
            queueItem,
            "final-output",
            nodeKind: "ArtifactFinalizationNode",
            finalStep.Value!.ToolCode,
            CanonicalJson.Serialize(producerNodeIds),
            requiresApproval: true,
            AgentNodeSideEffectClass.ArtifactWrite,
            joinPolicy: "AllRequired",
            nowUtc);
        nodeRuns.Add(finalNode);
        queueItem.MarkSucceeded(nowUtc);
        task.MarkWorkspaceReady(nowUtc);

        var fileStore = new ToolRegistryGovernanceTestBase.InMemoryArtifactWorkspaceFileStore();
        foreach (var artifact in orderedArtifacts)
        {
            fileStore.AddFile(
                workspace.WorkspaceCode,
                artifact.RelativePath,
                artifactContents[artifact.Id.Value],
                artifact.MimeType);
        }

        var nodeRunStore = new FixedNodeRunStore(nodeRuns, evidence);
        var proofFactory = new FinalOutputApprovalProofFactory(
            new ToolRegistryGovernanceTestBase.InMemoryAgentTaskRunAttemptStore(attempt),
            nodeRunStore,
            fileStore);
        var proof = await proofFactory.CreateAsync(
            task,
            workspace,
            cancellationToken);
        if (!proof.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Final-output test proof could not be created: {string.Join("; ", proof.Errors ?? [])}");
        }

        return new FinalOutputApprovalTestFixture(
            queueItem,
            attempt,
            nodeRuns,
            evidence,
            proof.Value!.Proof,
            nodeRunStore);
    }

    private static AgentNodeRun CreateNode(
        AgentTask task,
        AgentTaskRunAttempt attempt,
        AgentTaskRunQueueItem queueItem,
        string nodeId,
        string nodeKind,
        string? toolCode,
        string dependenciesJson,
        bool requiresApproval,
        AgentNodeSideEffectClass sideEffectClass,
        string? joinPolicy,
        DateTimeOffset nowUtc)
    {
        var node = new AgentNodeRun(
            task.Id,
            attempt.Id,
            queueItem.Id,
            planDigest: new string('a', 64),
            executionSnapshotDigest: new string('b', 64),
            nodeId,
            nodeKind,
            toolCode,
            dependenciesJson,
            inputJson: "{}",
            inputDigest: CanonicalJson.ComputeSha256("{}"),
            outputSchemaRef: "test://final-output",
            isRequired: true,
            requiresApproval,
            sideEffectClass,
            idempotencyKeyHash: CanonicalJson.ComputeSha256($"{task.Id.Value:N}:{nodeId}"),
            maxAttempts: 1,
            timeoutSeconds: 60,
            new AgentNodeBudgetLimits(
                MaxToolCalls: 1,
                MaxModelCalls: 0,
                MaxInputTokens: 0,
                MaxOutputTokens: 0,
                MaxCostAmount: 0,
                MaxArtifactCount: 1,
                MaxArtifactBytes: 1_048_576),
            joinPolicy,
            nowUtc);
        node.BindTaskClaim(queueItem.Id, task.RunFencingToken, nowUtc);
        return node;
    }

    private sealed class FixedNodeRunStore(
        IReadOnlyCollection<AgentNodeRun> nodes,
        IReadOnlyCollection<AgentEvidenceRecord> evidence)
        : IAgentNodeRunStore
    {
        public Task<IReadOnlyCollection<AgentNodeRun>> EnsureMaterializedAsync(
            DurableTaskClaim claim,
            AgentRunBudgetLimits taskBudget,
            IReadOnlyCollection<AgentNodeRunSeed> seeds,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyCollection<AgentNodeRun>> ListByAttemptAsync(
            AgentTaskRunAttemptId runAttemptId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<AgentNodeRun>>(
                nodes.Where(node => node.RunAttemptId == runAttemptId).ToArray());

        public Task<IReadOnlyCollection<AgentEvidenceRecord>> ListEvidenceByAttemptAsync(
            AgentTaskRunAttemptId runAttemptId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<AgentEvidenceRecord>>(
                evidence.Where(item => item.RunAttemptId == runAttemptId).ToArray());

        public Task<IReadOnlyCollection<AgentRunUsageLedgerEntry>> ListUsageByAttemptAsync(
            AgentTaskRunAttemptId runAttemptId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<AgentRunUsageLedgerEntry>>([]);

        public Task<AgentFencedWriteResult> TryReleaseApprovalAsync(
            AgentNodeRunId nodeRunId,
            AgentTaskRunAttemptId runAttemptId,
            long taskFencingToken,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
