using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed record AgentFinalizationNodeExecutionResult(
    ArtifactFileSetStage Stage,
    string DurableOutputJson);

internal sealed class AgentFinalizationNodeExecutor(
    IArtifactWorkspaceFileStore fileStore,
    IArtifactWorkspaceFileSetStore fileSetStore,
    NodeRunClaimCoordinator nodeRunClaimCoordinator,
    NodeCheckpointCoordinator nodeCheckpointCoordinator,
    IOptions<AgentRunQueueOptions>? runQueueOptions = null)
{
    public async Task<Result<AgentFinalizationNodeExecutionResult>> ExecuteAsync(
        DurableTaskClaim taskClaim,
        AgentNodeRunClaim nodeClaim,
        AgentPlanNodeDocument nodeContract,
        ArtifactWorkspace workspace,
        AgentStep finalStep,
        ApprovalRequest approval,
        IReadOnlyCollection<AgentEvidenceRecord> parentEvidence,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken)
    {
        if (taskClaim.Task.Status != AgentTaskStatus.WaitingFinalApproval ||
            taskClaim.Task.WorkspaceId != workspace.Id ||
            workspace.TaskId != taskClaim.Task.Id ||
            workspace.Status != ArtifactWorkspaceStatus.Active ||
            finalStep.Status != AgentStepStatus.Approved ||
            finalStep.StepType != AgentStepType.Finalize ||
            finalStep.Id != taskClaim.Task.Steps.OrderBy(step => step.StepIndex).Last().Id ||
            approval.TaskId != taskClaim.Task.Id ||
            approval.ApprovalType != AgentApprovalType.FinalOutput ||
            approval.Status != AgentApprovalStatus.Approved ||
            !string.Equals(approval.TargetId, workspace.WorkspaceCode, StringComparison.Ordinal) ||
            nodeClaim.NodeRun.SideEffectClass != AgentNodeSideEffectClass.ArtifactWrite ||
            !string.Equals(nodeClaim.NodeRun.NodeId, nodeContract.NodeId, StringComparison.Ordinal) ||
            !string.Equals(
                nodeClaim.NodeRun.ToolCode,
                BuiltInToolRegistrations.FinalizationCheckpointToolCode,
                StringComparison.Ordinal) ||
            !string.Equals(
                finalStep.ToolCode,
                BuiltInToolRegistrations.FinalizationCheckpointToolCode,
                StringComparison.Ordinal))
        {
            return Conflict("Final-output NodeRun authority does not match the approved task checkpoint.");
        }

        var artifacts = workspace.Artifacts
            .OrderBy(artifact => artifact.Id.Value)
            .ToArray();
        if (artifacts.Length == 0 ||
            artifacts.Any(artifact =>
                artifact.Status is not (ArtifactStatus.Draft or ArtifactStatus.Reviewing or ArtifactStatus.Approved) ||
                artifact.FinalizedAt is not null))
        {
            return Conflict("Final-output NodeRun requires non-final persisted workspace artifacts.");
        }

        var staged = new List<(Artifact Artifact, string SourcePath, ArtifactFileSetWriteRequest Write)>();
        long totalBytes = 0;
        foreach (var artifact in artifacts)
        {
            var sourcePath = ArtifactPathGuard.NormalizeRelativePath(artifact.RelativePath);
            var source = await fileStore.OpenReadAsync(
                workspace.WorkspaceCode,
                sourcePath,
                artifact.MimeType,
                cancellationToken);
            if (source is null)
            {
                return Conflict($"Artifact '{artifact.Name}' source file is missing.");
            }

            byte[] content;
            await using (source.Stream)
            {
                using var buffer = new MemoryStream();
                await source.Stream.CopyToAsync(buffer, cancellationToken);
                content = buffer.ToArray();
            }

            if (source.FileSize != content.LongLength ||
                artifact.FileSize != content.LongLength ||
                !string.Equals(source.MimeType, artifact.MimeType, StringComparison.OrdinalIgnoreCase))
            {
                return Conflict($"Artifact '{artifact.Name}' metadata does not match its persisted source bytes.");
            }

            totalBytes = checked(totalBytes + content.LongLength);
            staged.Add((
                artifact,
                sourcePath,
                CreateFinalStageWriteRequest(sourcePath, content, artifact.MimeType)));
        }

        if (HasCaseInsensitivePathCollision(staged.Select(item => item.Write.RelativePath)))
        {
            return Conflict("Final artifact paths conflict after canonical path normalization.");
        }

        var approvalAuthority = ValidateApprovalAuthority(
            taskClaim,
            nodeClaim,
            workspace,
            finalStep,
            approval,
            parentEvidence,
            staged);
        if (!approvalAuthority.IsSuccess)
        {
            return Result.From(approvalAuthority);
        }

        if (staged.Count > nodeClaim.NodeRun.ReservedArtifactCount ||
            totalBytes > nodeClaim.NodeRun.ReservedArtifactBytes)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentRunBudgetExceeded,
                "Final artifact file set exceeds the immutable NodeRun artifact budget."));
        }

        var leaseDuration = (runQueueOptions?.Value ?? new AgentRunQueueOptions()).LeaseDuration;
        var renewed = await nodeRunClaimCoordinator.RenewTaskAndNodeLeaseAsync(
            nodeClaim,
            leaseDuration,
            leaseDuration,
            DateTimeOffset.UtcNow,
            cancellationToken);
        if (!renewed.IsSuccess)
        {
            return Result.From(renewed);
        }

        ArtifactFileSetStage stage;
        try
        {
            stage = await fileSetStore.StageAsync(
                workspace.WorkspaceCode,
                "FinalizeArtifacts",
                "final/.committed",
                staged.Select(item => item.Write).ToArray(),
                cancellationToken,
                new ArtifactFileSetAuthority(
                    taskClaim.Task.Id.Value,
                    nodeClaim.NodeRun.Id.Value,
                    taskClaim.TaskFencingToken,
                    nodeClaim.NodeFencingToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            return Conflict("Final artifact file set could not be staged under the active NodeRun authority.");
        }

        var completedAtUtc = DateTimeOffset.UtcNow;
        var durableOutputJson = CanonicalJson.Serialize(new
        {
            status = "finalized",
            resultType = "finalization-checkpoint"
        });
        var bindings = staged.Select(item =>
        {
            var finalPath = $"{stage.PublishedReference}/{item.Write.RelativePath}";
            var published = stage.Files.Single(file => string.Equals(
                file.RelativePath,
                finalPath,
                StringComparison.Ordinal));
            return new AgentFinalizationArtifactBinding(
                item.Artifact.Id,
                item.SourcePath,
                published.RelativePath,
                published.FileSize,
                published.MimeType,
                published.Sha256);
        }).ToArray();
        var normalized = BuildCheckpoint(
            taskClaim,
            nodeClaim,
            nodeContract,
            workspace,
            stage,
            parentEvidence,
            startedAtUtc,
            completedAtUtc);
        if (!normalized.IsSuccess)
        {
            await fileSetStore.RollbackBestEffortAsync(stage, CancellationToken.None);
            return Result.From(normalized);
        }

        var receiptHash = CanonicalJson.ComputeSha256(CanonicalJson.Serialize(new
        {
            stage.CommitId,
            stage.ManifestDigest,
            taskClaim.TaskFencingToken,
            nodeClaim.NodeFencingToken
        }));
        var checkpoint = normalized.Value!;
        try
        {
            await fileSetStore.ExecuteAsync(
                stage,
                async commitCancellationToken =>
                {
                    var committed = await nodeCheckpointCoordinator.CommitSuccessAsync(
                        new AgentNodeSuccessCheckpoint(
                            taskClaim.Task.Id,
                            taskClaim.RunAttempt.Id,
                            nodeClaim.NodeRun.Id,
                            taskClaim.TaskFencingToken,
                            nodeClaim.NodeFencingToken,
                            checkpoint.Evidence,
                            checkpoint.Usage,
                            stage.ManifestDigest,
                            BuiltInToolRegistrations.FinalizationCheckpointToolCode,
                            receiptHash,
                            completedAtUtc,
                            new AgentNodeFinalizationMutation(
                                workspace.Id,
                                approval.Id,
                                finalStep.Id,
                                approval.FinalOutputProofDigest!,
                                approval.FinalOutputDecisionProofDigest!,
                                approvalAuthority.Value!,
                                approval.FinalOutputManifestDigest!,
                                approval.FinalOutputArtifactBindingDigest!,
                                stage,
                                bindings,
                                durableOutputJson,
                                "产物已确认并输出到 final 目录。")),
                        commitCancellationToken);
                    if (!committed.IsSuccess)
                    {
                        var problem = committed.Errors?
                            .OfType<ApiProblemDescriptor>()
                            .FirstOrDefault() ?? new ApiProblemDescriptor(
                                AppProblemCodes.AgentNodeRunStateConflict,
                                "Final-output NodeRun checkpoint was rejected.");
                        throw new FinalizationCheckpointRejectedException(problem);
                    }

                    return true;
                },
                cancellationToken);
        }
        catch (PersistenceCommitOutcomeUnknownException)
        {
            throw;
        }
        catch (FinalizationCheckpointRejectedException exception)
        {
            return Result.Failure(exception.Problem);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        return Result.Success(new AgentFinalizationNodeExecutionResult(stage, durableOutputJson));
    }

    internal static ArtifactFileSetWriteRequest CreateFinalStageWriteRequest(
        string sourcePath,
        byte[] content,
        string mimeType)
    {
        return new ArtifactFileSetWriteRequest(
            ArtifactPathGuard.NormalizeRelativePath(sourcePath),
            content,
            mimeType);
    }

    internal static bool HasCaseInsensitivePathCollision(IEnumerable<string> relativePaths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return relativePaths.Any(path => !seen.Add(ArtifactPathGuard.NormalizeRelativePath(path)));
    }

    private static Result<string> ValidateApprovalAuthority(
        DurableTaskClaim taskClaim,
        AgentNodeRunClaim nodeClaim,
        ArtifactWorkspace workspace,
        AgentStep finalStep,
        ApprovalRequest approval,
        IReadOnlyCollection<AgentEvidenceRecord> parentEvidence,
        IReadOnlyCollection<(Artifact Artifact, string SourcePath, ArtifactFileSetWriteRequest Write)> staged)
    {
        if (!approval.HasValidFinalOutputProof() ||
            !approval.HasValidFinalOutputDecisionProof())
        {
            return Conflict<string>(
                "Final-output NodeRun requires immutable approval and decision proofs.");
        }

        var proof = approval.GetFinalOutputProof();
        if (proof.WorkspaceId != workspace.Id ||
            !string.Equals(proof.WorkspaceCode, workspace.WorkspaceCode, StringComparison.Ordinal) ||
            proof.FinalStepId != finalStep.Id ||
            proof.ActiveRunAttemptId != taskClaim.RunAttempt.Id ||
            proof.FinalNodeRunId != nodeClaim.NodeRun.Id ||
            proof.TaskFencingToken != taskClaim.TaskFencingToken ||
            proof.TaskFencingToken != nodeClaim.TaskFencingToken ||
            proof.NodeFencingToken != nodeClaim.NodeFencingToken)
        {
            return Conflict<string>(
                "Final-output NodeRun task, workspace, step, attempt, node, or fence differs from approval.");
        }

        if (!AgentEvidenceSetDigestAuthority.TryComputeEffective(
                parentEvidence,
                out var evidenceSetDigest) ||
            evidenceSetDigest is null ||
            !string.Equals(
                proof.EvidenceSetDigest,
                evidenceSetDigest,
                StringComparison.Ordinal))
        {
            return Conflict<string>(
                "Final-output NodeRun Evidence set differs from the approved Evidence set.");
        }

        FinalOutputApprovalArtifactBinding[] expectedBindings;
        try
        {
            expectedBindings = JsonSerializer.Deserialize<FinalOutputApprovalArtifactBinding[]>(
                                   proof.ArtifactBindingsJson,
                                   CanonicalJson.SerializerOptions)
                               ?? [];
        }
        catch (JsonException)
        {
            return Conflict<string>(
                "Final-output approval artifact bindings are unreadable.");
        }

        var actualBindings = staged
            .OrderBy(item => item.Artifact.Id.Value)
            .Select(item => new FinalOutputApprovalArtifactBinding(
                item.Artifact.Id.Value,
                item.Artifact.CreatedByStepId!.Value.Value,
                item.Artifact.Version,
                item.SourcePath,
                item.Write.Content.LongLength,
                item.Write.MimeType,
                Convert.ToHexString(SHA256.HashData(item.Write.Content)).ToLowerInvariant()))
            .ToArray();
        var bindingJson = CanonicalJson.Serialize(actualBindings);
        var bindingDigest = CanonicalJson.ComputeSha256(bindingJson);
        if (!actualBindings.SequenceEqual(expectedBindings) ||
            !string.Equals(
                bindingDigest,
                proof.ArtifactBindingDigest,
                StringComparison.Ordinal))
        {
            return Conflict<string>(
                "Final-output source file bytes or artifact bindings drifted after approval.");
        }

        var manifestDigest = CanonicalJson.ComputeSha256(CanonicalJson.Serialize(new
        {
            version = "final-output-source-manifest-v1",
            taskId = taskClaim.Task.Id.Value,
            workspaceId = workspace.Id.Value,
            workspaceCode = workspace.WorkspaceCode,
            finalStepId = finalStep.Id.Value,
            activeRunAttemptId = taskClaim.RunAttempt.Id.Value,
            finalNodeRunId = nodeClaim.NodeRun.Id.Value,
            taskFencingToken = taskClaim.TaskFencingToken,
            nodeFencingToken = nodeClaim.NodeFencingToken,
            evidenceSetDigest,
            artifactBindingDigest = bindingDigest,
            artifacts = actualBindings
        }));
        return string.Equals(
            manifestDigest,
            proof.ManifestDigest,
            StringComparison.Ordinal)
            ? Result.Success(evidenceSetDigest)
            : Conflict<string>(
                "Final-output source manifest differs from the approved manifest.");
    }

    private static Result<AgentNormalizedNodeCheckpoint> BuildCheckpoint(
        DurableTaskClaim taskClaim,
        AgentNodeRunClaim nodeClaim,
        AgentPlanNodeDocument nodeContract,
        ArtifactWorkspace workspace,
        ArtifactFileSetStage stage,
        IReadOnlyCollection<AgentEvidenceRecord> parentEvidence,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc)
    {
        var payloadRef = $"artifact-fileset:{stage.CommitId:N}";
        var payloadBytes = Encoding.UTF8.GetByteCount(stage.ManifestJson);
        if (!parentEvidence
                .Select(evidence => evidence.NodeId)
                .SequenceEqual(nodeContract.EvidenceSelectors, StringComparer.Ordinal))
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentNodeRunStateConflict,
                "Final-output NodeRun inputs do not match the frozen Evidence selectors."));
        }

        var parentIds = AgentEvidenceSetDigestAuthority.OrderedIds(parentEvidence);
        if (nodeContract.EvidenceSelectors.Count != parentIds.Length)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentNodeRunStateConflict,
                "Final-output NodeRun is missing authoritative parent Evidence."));
        }

        if (!AgentEvidenceSetDigestAuthority.TryComputeEffective(
                parentEvidence,
                out var evidenceSetDigest) ||
            evidenceSetDigest is null)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentNodeRunStateConflict,
                "Final-output NodeRun could not bind the authoritative input Evidence set."));
        }

        var artifactRefs = workspace.Artifacts
            .Select(artifact => artifact.Id.Value.ToString("D"))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var evidenceId = AgentEvidenceRecordId.New();
        var authority = AgentEvidenceRecordAuthority.From(taskClaim, nodeClaim, nodeContract.NodeId);
        var envelopeDraft = new AgentEvidenceEnvelopeDraft(
            AgentEvidenceKind.ArtifactReference,
            AgentEvidenceTruthClass.ObservedFact,
            new AgentEvidenceProducerDocument(
                nodeContract.NodeKind,
                "artifact-workspace-finalization:v1",
                BuiltInToolRegistrations.FinalizationCheckpointToolCode,
                nodeClaim.NodeRun.ExecutionSnapshotDigest,
                ModelId: null,
                ModelVersion: null,
                PromptVersion: null),
            new AgentEvidenceSourceDocument(
                "ArtifactWorkspace",
                payloadRef,
                "CommittedFinalFileSet",
                workspace.Artifacts.All(artifact => artifact.IsSimulation),
                completedAtUtc,
                completedAtUtc,
                TimeRange: null,
                SanitizedScope: ["artifact-file-set", "final-output"],
                Provider: "ArtifactWorkspace",
                ProviderOperationCode: BuiltInToolRegistrations.FinalizationCheckpointToolCode,
                SemanticIntent: null,
                QueryScope: []),
            AgentArtifactFileSetEvidenceDocuments.CreateQuality(
                stage.Files.Count,
                humanApproved: true),
            AgentArtifactFileSetEvidenceDocuments.CreatePayload(
                payloadRef,
                payloadBytes,
                stage.ManifestDigest),
            AgentArtifactFileSetEvidenceDocuments.CreateContent(
                "Human-approved final artifact file set was committed and manifest-verified.",
                stage.Files.Count,
                stage.Files.Sum(file => (decimal)file.FileSize),
                artifactRefs),
            new AgentEvidenceLineageDocument(
                parentIds,
                nodeClaim.NodeRun.InputDigest,
                stage.ManifestDigest,
                evidenceSetDigest),
            Prediction: null,
            completedAtUtc);
        var evidenceResult = AgentEvidenceRecordFactory.Seal(
            evidenceId,
            authority,
            envelopeDraft,
            new AgentEvidenceRecordPayload(
                AgentEvidenceStorageMode.ArtifactReference,
                stage.ManifestDigest,
                InlinePayloadJson: null,
                payloadRef,
                "application/vnd.aicopilot.artifact-file-set+json",
                payloadBytes,
                stage.ManifestDigest));
        return AgentNormalizedNodeCheckpointFactory.Create(
            evidenceResult,
            authority,
            taskClaim.RunAttempt.BudgetCostCurrency,
            new AgentRunUsageDraft(
                ModelCalls: 0,
                ToolCalls: 1,
                Math.Min(
                Math.Max(0, (long)(completedAtUtc - startedAtUtc).TotalMilliseconds),
                nodeClaim.NodeRun.ReservedElapsedMilliseconds),
                stage.Files.Count,
                stage.Files.Sum(file => file.FileSize),
                CanonicalJson.ComputeSha256(CanonicalJson.Serialize(new
                {
                    taskClaim.TaskFencingToken,
                    nodeClaim.NodeFencingToken,
                    stage.CommitId,
                    stage.ManifestDigest
                })),
                completedAtUtc),
            stage.ManifestDigest);
    }

    private static Result<AgentFinalizationNodeExecutionResult> Conflict(string detail) =>
        Result.Failure(new ApiProblemDescriptor(
            AppProblemCodes.AgentFinalizationStateConflict,
            detail));

    private static Result<T> Conflict<T>(string detail) =>
        Result.Failure(new ApiProblemDescriptor(
            AppProblemCodes.AgentFinalizationStateConflict,
            detail));

    private sealed class FinalizationCheckpointRejectedException(ApiProblemDescriptor problem) : Exception
    {
        public ApiProblemDescriptor Problem { get; } = problem;
    }
}
