using System.Text;
using AICopilot.AiGatewayService.Sessions;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Logging;
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
    IRepository<ArtifactWorkspace> workspaceRepository,
    AgentAuditRecorder auditRecorder,
    ILogger<AgentFinalizationNodeExecutor> logger,
    IOptions<AgentRunQueueOptions>? runQueueOptions = null,
    MessageTimelineProjectionWriter? timelineProjectionWriter = null)
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
                new ArtifactFileSetWriteRequest(
                    $"{artifact.Id.Value:N}/{Path.GetFileName(sourcePath)}",
                    content,
                    artifact.MimeType)));
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
        catch (Exception)
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
            var finalPath = $"{stage.PublishedReference}/{item.Artifact.Id.Value:N}/{Path.GetFileName(item.SourcePath)}";
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
        catch (Exception)
        {
            return Conflict("Final-output NodeRun checkpoint could not be committed.");
        }

        await StageProjectionsBestEffortAsync(
            taskClaim.Task,
            workspace,
            finalStep,
            cancellationToken);
        return Result.Success(new AgentFinalizationNodeExecutionResult(stage, durableOutputJson));
    }

    private async Task StageProjectionsBestEffortAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentStep finalStep,
        CancellationToken cancellationToken)
    {
        try
        {
            await auditRecorder.RecordToolAsync(
                task,
                workspace,
                finalStep,
                AuditResults.Succeeded,
                "Final-output checkpoint committed.",
                artifactId: null,
                cancellationToken: cancellationToken);
            await auditRecorder.RecordWorkspaceFinalizedAsync(
                task,
                workspace,
                AuditResults.Succeeded,
                "Workspace artifacts finalized.",
                cancellationToken);
            if (timelineProjectionWriter is not null)
            {
                await timelineProjectionWriter.StageStepCompletedAsync(task, finalStep, cancellationToken);
                await timelineProjectionWriter.StageWorkspaceFinalizedAsync(task, workspace, cancellationToken);
            }

            await workspaceRepository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Final-output authority committed but best-effort projections were deferred. ErrorType={ErrorType}; OriginalMessage=hidden_by_security_policy",
                exception.GetType().Name);
        }
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
        var parentIds = parentEvidence
            .Where(evidence => nodeContract.DependsOn.Contains(evidence.NodeId, StringComparer.Ordinal))
            .Select(evidence => evidence.Id.Value)
            .Distinct()
            .Order()
            .ToArray();
        if (nodeContract.DependsOn.Count != parentIds.Length)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentNodeRunStateConflict,
                "Final-output NodeRun is missing authoritative parent Evidence."));
        }

        var artifactRefs = workspace.Artifacts
            .Select(artifact => artifact.Id.Value.ToString("D"))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var allowedConsumerScope = new[]
        {
            $"session:{taskClaim.Task.SessionId.Value:D}",
            $"task:{taskClaim.Task.Id.Value:D}",
            $"user:{taskClaim.Task.UserId:D}"
        }.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var evidenceId = AgentEvidenceRecordId.New();
        var document = new AgentEvidenceEnvelopeDocument(
            AgentPlanContractVersions.EvidenceV1,
            evidenceId.Value,
            TenantId: null,
            taskClaim.Task.UserId,
            taskClaim.Task.SessionId.Value,
            taskClaim.Task.Id.Value,
            taskClaim.RunAttempt.Id.Value,
            nodeContract.NodeId,
            AgentEvidenceKind.ArtifactReference.ToString(),
            AgentEvidenceTruthClass.ObservedFact.ToString(),
            new AgentEvidenceProducerDocument(
                nodeContract.NodeKind,
                "artifact-workspace-finalization:v1",
                BuiltInToolRegistrations.FinalizationCheckpointToolCode,
                nodeClaim.NodeRun.ExecutionSnapshotDigest,
                taskClaim.Task.ModelId?.Value,
                ModelVersion: null,
                PromptVersion: "snapshot-bound"),
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
            new AgentEvidenceQualityDocument(
                RowCount: stage.Files.Count,
                IsTruncated: false,
                Freshness: "manifest-verified",
                MissingRate: 0,
                Confidence: 1,
                QualityFlags: ["file-set-manifest-verified", "human-approved-final-output"]),
            new AgentEvidencePayloadDocument(
                AgentPlanContractVersions.ArtifactReferenceEvidencePolicyV1,
                AgentEvidenceStorageMode.ArtifactReference.ToString(),
                payloadRef,
                "application/vnd.aicopilot.artifact-file-set+json",
                payloadBytes,
                stage.ManifestDigest,
                IsComplete: true,
                InlineCanonicalJson: null),
            new AgentEvidenceContentDocument(
                "Human-approved final artifact file set was committed and manifest-verified.",
                new Dictionary<string, decimal>
                {
                    ["fileCount"] = stage.Files.Count,
                    ["payloadBytes"] = stage.Files.Sum(file => (decimal)file.FileSize)
                },
                Findings: [],
                CitationRefs: [],
                artifactRefs),
            new AgentEvidenceLineageDocument(
                parentIds,
                nodeClaim.NodeRun.InputDigest,
                stage.ManifestDigest),
            new AgentEvidenceGovernanceDocument(
                "Internal",
                "Redacted",
                allowedConsumerScope,
                "TaskLifetime"),
            Prediction: null,
            completedAtUtc,
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
            AgentEvidenceKind.ArtifactReference,
            AgentEvidenceTruthClass.ObservedFact,
            AgentEvidenceStorageMode.ArtifactReference,
            canonical.CanonicalJson,
            canonical.Digest,
            stage.ManifestDigest,
            inlinePayloadJson: null,
            payloadRef,
            "application/vnd.aicopilot.artifact-file-set+json",
            payloadBytes,
            stage.ManifestDigest,
            CanonicalJson.Serialize(allowedConsumerScope),
            taskClaim.TaskFencingToken,
            nodeClaim.NodeFencingToken,
            completedAtUtc);
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
                Math.Max(0, (long)(completedAtUtc - startedAtUtc).TotalMilliseconds),
                nodeClaim.NodeRun.ReservedElapsedMilliseconds),
            costAmount: 0m,
            artifactCount: stage.Files.Count,
            artifactBytes: stage.Files.Sum(file => file.FileSize),
            costCurrency: taskClaim.RunAttempt.BudgetCostCurrency,
            correlationHash: CanonicalJson.ComputeSha256(CanonicalJson.Serialize(new
            {
                taskClaim.TaskFencingToken,
                nodeClaim.NodeFencingToken,
                stage.CommitId,
                stage.ManifestDigest
            })),
            completedAtUtc);
        return Result.Success(new AgentNormalizedNodeCheckpoint(evidence, usage, stage.ManifestDigest));
    }

    private static Result<AgentFinalizationNodeExecutionResult> Conflict(string detail) =>
        Result.Failure(new ApiProblemDescriptor(
            AppProblemCodes.AgentFinalizationStateConflict,
            detail));

    private sealed class FinalizationCheckpointRejectedException(ApiProblemDescriptor problem) : Exception
    {
        public ApiProblemDescriptor Problem { get; } = problem;
    }
}
