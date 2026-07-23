using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class ArtifactFileSetOutcomeAuthorityProbe(
    IArtifactFileSetOperationStore operationStore,
    IArtifactWorkspaceFileSetStore fileSetStore)
    : IAgentOutcomeAuthorityProbe
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public bool CanProbe(AgentOutcomeReconciliationClaim claim) =>
        claim.NodeRun.SideEffectClass == AgentNodeSideEffectClass.ArtifactWrite;

    public async Task<AgentOutcomeAuthorityProbeResult> ProbeAsync(
        AgentOutcomeReconciliationClaim claim,
        CancellationToken cancellationToken)
    {
        var operations = await operationStore.ListByNodeFenceAsync(
            claim.NodeRun.Id,
            claim.TaskFencingToken,
            claim.NodeFencingToken,
            cancellationToken);
        var pending = await fileSetStore.GetPendingAsync(
            maximumEntries: 1024,
            createdBeforeUtc: DateTimeOffset.MaxValue,
            cancellationToken);
        var relatedPending = pending.Stages.Where(stage => Matches(stage.Authority, claim)).ToArray();

        if (operations.Count > 1 || relatedPending.Length > 1)
        {
            return Conflict(
                claim,
                "artifact_fileset_authority_ambiguous",
                "More than one file-set authority record exists for one node fence.");
        }

        if (operations.Count == 0)
        {
            if (relatedPending.Length == 1 || pending.HasUnreadableEntries)
            {
                return new AgentOutcomeAuthorityProbeResult(
                    AgentOutcomeReconciliationResolution.StillUnknown,
                    "artifact_fileset_reconciliation_pending",
                    "Artifact file-set journal still requires database-marker reconciliation.",
                    claim.NodeRun.ProviderReceiptHash,
                    EvidenceDigest: null,
                    NextCheckAtUtc: DateTimeOffset.UtcNow.AddMinutes(2));
            }

            if (claim.NodeRun.ReconciliationPolicy?.StartsWith(
                    "cancellation-",
                    StringComparison.Ordinal) == true)
            {
                return new AgentOutcomeAuthorityProbeResult(
                    AgentOutcomeReconciliationResolution.ConfirmedCancelled,
                    "artifact_fileset_cancelled_before_commit",
                    "Cancellation was confirmed before the fenced artifact file set committed.",
                    ProviderReceiptHash: null,
                    EvidenceDigest: null,
                    AllowNodeRetry: false);
            }

            return new AgentOutcomeAuthorityProbeResult(
                AgentOutcomeReconciliationResolution.ConfirmedNotOccurred,
                "artifact_fileset_not_committed",
                "No committed metadata or pending journal exists for the fenced artifact file set.",
                ProviderReceiptHash: null,
                EvidenceDigest: null,
                AllowNodeRetry: claim.NodeRun.AttemptNo < claim.NodeRun.MaxAttempts,
                RetryAtUtc: DateTimeOffset.UtcNow.AddSeconds(5));
        }

        var snapshot = operations.Single();
        var operation = snapshot.Operation;
        if (operation.Status != ArtifactFileSetOperationStatus.Completed ||
            string.IsNullOrWhiteSpace(operation.PublishedReference) ||
            !string.Equals(operation.ManifestDigest, operation.PublishedManifestDigest, StringComparison.Ordinal))
        {
            return Conflict(
                claim,
                "artifact_fileset_database_checkpoint_incomplete",
                "Artifact file-set database metadata is present but not a complete authoritative checkpoint.");
        }

        var stage = relatedPending.SingleOrDefault(candidate => candidate.CommitId == operation.CommitId)
                    ?? RestoreStage(operation, snapshot.Workspace.WorkspaceCode);
        if (stage is null ||
            !Matches(stage.Authority, claim) ||
            stage.CommitId != operation.CommitId ||
            !string.Equals(stage.ManifestDigest, operation.ManifestDigest, StringComparison.Ordinal) ||
            !string.Equals(stage.PublishedReference, operation.PublishedReference, StringComparison.Ordinal))
        {
            return Conflict(
                claim,
                "artifact_fileset_manifest_conflict",
                "Artifact file-set manifest identity conflicts with its fenced database checkpoint.");
        }

        if (!await fileSetStore.VerifyPublishedAsync(stage, cancellationToken))
        {
            return Conflict(
                claim,
                "artifact_fileset_integrity_conflict",
                "Artifact file-set bytes do not match the authoritative manifest.");
        }

        if (TryBuildDurableOutput(
                claim.NodeRun.ToolCode,
                snapshot,
                stage) is null)
        {
            return Conflict(
                claim,
                "artifact_fileset_output_not_reconstructable",
                "The file side effect is verified, but the complete node output cannot be reconstructed authoritatively.");
        }

        return BuildConfirmedSuccess(claim, snapshot, stage);
    }

    private static AgentOutcomeAuthorityProbeResult BuildConfirmedSuccess(
        AgentOutcomeReconciliationClaim claim,
        ArtifactFileSetOutcomeAuthoritySnapshot snapshot,
        ArtifactFileSetStage stage)
    {
        var now = DateTimeOffset.UtcNow;
        var task = snapshot.Task;
        var operation = snapshot.Operation;
        var payloadRef = $"artifact-fileset:{operation.CommitId:N}";
        var payloadBytes = Encoding.UTF8.GetByteCount(operation.ManifestJson);
        var receiptHash = Hash(CanonicalJson.Serialize(new
        {
            operation.CommitId,
            operation.ManifestDigest,
            operation.PublishedManifestDigest,
            operation.TaskFencingToken,
            operation.NodeFencingToken
        }));
        var allowedConsumerScope = new[]
        {
            $"session:{task.SessionId.Value:D}",
            $"task:{task.Id.Value:D}",
            $"user:{task.UserId:D}"
        }.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var artifactRefs = snapshot.Workspace.Artifacts
            .Where(artifact => artifact.RelativePath.StartsWith(
                stage.PublishedReference + "/",
                StringComparison.Ordinal))
            .Select(artifact => artifact.Id.Value.ToString("D"))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var evidenceId = AgentEvidenceRecordId.New();
        var document = new AgentEvidenceEnvelopeDocument(
            AgentPlanContractVersions.EvidenceV1,
            evidenceId.Value,
            TenantId: null,
            task.UserId,
            task.SessionId.Value,
            task.Id.Value,
            claim.RunAttemptId.Value,
            claim.NodeRun.NodeId,
            AgentEvidenceKind.ArtifactReference.ToString(),
            AgentEvidenceTruthClass.ObservedFact.ToString(),
            new AgentEvidenceProducerDocument(
                claim.NodeRun.NodeKind,
                "artifact-workspace-fileset:v1",
                claim.NodeRun.ToolCode,
                claim.NodeRun.ExecutionSnapshotDigest,
                task.ModelId?.Value,
                ModelVersion: null,
                PromptVersion: "snapshot-bound"),
            new AgentEvidenceSourceDocument(
                "ArtifactWorkspace",
                payloadRef,
                "CommittedFileSet",
                IsSimulation: false,
                now,
                now,
                TimeRange: null,
                SanitizedScope: ["artifact-file-set"],
                Provider: "ArtifactWorkspace",
                ProviderOperationCode: claim.NodeRun.ProviderOperationCode,
                SemanticIntent: null,
                QueryScope: []),
            AgentArtifactFileSetEvidenceDocuments.CreateQuality(
                stage.Files.Count,
                humanApproved: false),
            AgentArtifactFileSetEvidenceDocuments.CreatePayload(
                payloadRef,
                payloadBytes,
                operation.ManifestDigest),
            AgentArtifactFileSetEvidenceDocuments.CreateContent(
                "Artifact file set was committed and its manifest and file digests were verified.",
                stage.Files.Count,
                stage.Files.Sum(file => (decimal)file.FileSize),
                artifactRefs),
            new AgentEvidenceLineageDocument(
                ParentEvidenceIds: [],
                claim.NodeRun.InputDigest,
                operation.ManifestDigest),
            new AgentEvidenceGovernanceDocument(
                "Internal",
                "Redacted",
                allowedConsumerScope,
                "TaskLifetime"),
            Prediction: null,
            now,
            Digest: string.Empty);
        var sealedEnvelope = AgentEvidenceCanonicalizer.Seal(document);
        if (!sealedEnvelope.IsSuccess)
        {
            return Conflict(
                claim,
                "artifact_fileset_evidence_invalid",
                "Verified artifact file set could not be sealed as Evidence v1.");
        }

        var canonical = sealedEnvelope.Value!;
        var evidence = new AgentEvidenceRecord(
            evidenceId,
            tenantId: null,
            task.UserId,
            task.SessionId,
            task.Id,
            claim.RunAttemptId,
            claim.NodeRun.Id,
            claim.NodeRun.NodeId,
            AgentEvidenceKind.ArtifactReference,
            AgentEvidenceTruthClass.ObservedFact,
            AgentEvidenceStorageMode.ArtifactReference,
            canonical.CanonicalJson,
            canonical.Digest,
            operation.ManifestDigest,
            inlinePayloadJson: null,
            payloadRef,
            "application/vnd.aicopilot.artifact-file-set+json",
            payloadBytes,
            operation.ManifestDigest,
            CanonicalJson.Serialize(allowedConsumerScope),
            claim.TaskFencingToken,
            claim.NodeFencingToken,
            now);
        var usage = new AgentRunUsageLedgerEntry(
            task.Id,
            claim.RunAttemptId,
            claim.NodeRun.Id,
            claim.TaskFencingToken,
            claim.NodeFencingToken,
            inputTokens: 0,
            outputTokens: 0,
            modelCalls: 0,
            toolCalls: 1,
            elapsedMilliseconds: 0,
            costAmount: 0,
            artifactCount: stage.Files.Count,
            artifactBytes: stage.Files.Sum(file => file.FileSize),
            costCurrency: "CNY",
            correlationHash: Hash(CanonicalJson.Serialize(new
            {
                claim.TaskFencingToken,
                claim.NodeFencingToken,
                operation.CommitId,
                operation.ManifestDigest
            })),
            now);
        return new AgentOutcomeAuthorityProbeResult(
            AgentOutcomeReconciliationResolution.ConfirmedSucceeded,
            "artifact_fileset_commit_verified",
            "Artifact file-set checkpoint is authoritative and complete.",
            receiptHash,
            canonical.Digest,
            evidence,
            usage,
            operation.ManifestDigest);
    }

    internal static ArtifactFileSetStage? RestoreStage(
        ArtifactFileSetOperation operation,
        string workspaceCode)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<ArtifactFileSetManifestDocument>(
                operation.ManifestJson,
                JsonOptions);
            if (manifest is null ||
                !string.Equals(manifest.Version, "artifact-file-set-v1", StringComparison.Ordinal) ||
                manifest.CommitId != operation.CommitId ||
                !string.Equals(manifest.WorkspaceCode, workspaceCode, StringComparison.Ordinal) ||
                !string.Equals(manifest.OperationKind, operation.OperationKind, StringComparison.Ordinal) ||
                !string.Equals(manifest.PublishedReference, operation.PublishedReference, StringComparison.Ordinal) ||
                manifest.Files is null || manifest.Files.Count == 0 || manifest.Authority is null)
            {
                return null;
            }

            return new ArtifactFileSetStage(
                operation.CommitId,
                workspaceCode,
                operation.OperationKind,
                operation.StagingReference,
                operation.PublishedReference!,
                operation.ManifestJson,
                operation.ManifestDigest,
                manifest.Files,
                operation.CreatedAtUtc,
                manifest.Authority);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string? TryBuildDurableOutput(
        string? toolCode,
        ArtifactFileSetOutcomeAuthoritySnapshot snapshot,
        ArtifactFileSetStage stage)
    {
        var artifacts = snapshot.Workspace.Artifacts
            .Where(artifact => artifact.RelativePath.StartsWith(
                stage.PublishedReference + "/",
                StringComparison.Ordinal))
            .ToArray();
        if (string.Equals(
                toolCode,
                "finalize_artifacts",
                StringComparison.Ordinal) &&
            snapshot.Workspace.Status == ArtifactWorkspaceStatus.Finalized &&
            artifacts.Length == stage.Files.Count &&
            artifacts.All(artifact => artifact.Status == ArtifactStatus.Final))
        {
            return CanonicalJson.Serialize(new
            {
                status = "finalized",
                resultType = "finalization-checkpoint"
            });
        }

        var artifactType = toolCode switch
        {
            "generate_business_chart" or "generate_chart_data" => "chart",
            "generate_markdown_report" => "markdown",
            "generate_html_report" => "html",
            "generate_pdf" => "pdf",
            "generate_pptx" => "pptx",
            "generate_xlsx" => "xlsx",
            _ => null
        };
        if (artifactType is null)
        {
            return null;
        }

        if (artifacts.Length != 1)
        {
            return null;
        }

        return CanonicalJson.Serialize(new
        {
            status = "completed",
            resultType = "artifact",
            artifactType,
            artifactId = artifacts[0].Id.Value
        });
    }

    private static bool Matches(
        ArtifactFileSetAuthority? authority,
        AgentOutcomeReconciliationClaim claim)
    {
        return authority is not null &&
               authority.TaskId == claim.TaskId.Value &&
               authority.NodeRunId == claim.NodeRun.Id.Value &&
               authority.TaskFencingToken == claim.TaskFencingToken &&
               authority.NodeFencingToken == claim.NodeFencingToken;
    }

    private static AgentOutcomeAuthorityProbeResult Conflict(
        AgentOutcomeReconciliationClaim claim,
        string reasonCode,
        string safeMessage)
    {
        return new AgentOutcomeAuthorityProbeResult(
            AgentOutcomeReconciliationResolution.ConflictingEvidence,
            reasonCode,
            safeMessage,
            claim.NodeRun.ProviderReceiptHash,
            EvidenceDigest: null,
            NextCheckAtUtc: DateTimeOffset.UtcNow.AddHours(6));
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record ArtifactFileSetManifestDocument(
        string Version,
        Guid CommitId,
        string WorkspaceCode,
        string OperationKind,
        string PublishedReference,
        ArtifactFileSetAuthority? Authority,
        IReadOnlyList<ArtifactFileSetPublishedFile> Files);
}
