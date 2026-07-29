using System.Security.Cryptography;
using System.Text.Json;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed record FinalOutputApprovalProofSnapshot(
    FinalOutputApprovalProof Proof,
    AgentTaskRunAttempt ActiveAttempt,
    AgentNodeRun FinalNodeRun,
    IReadOnlyCollection<AgentEvidenceRecord> ParentEvidence);

public sealed class FinalOutputApprovalProofFactory(
    IAgentTaskRunAttemptStore runAttemptStore,
    IAgentNodeRunStore nodeRunStore,
    IArtifactWorkspaceFileStore fileStore)
{
    internal async Task<Result<FinalOutputApprovalProofSnapshot>> CreateAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        CancellationToken cancellationToken)
    {
        return await CreateCoreAsync(
            task,
            workspace,
            allowApprovedCheckpoint: false,
            cancellationToken);
    }

    private async Task<Result<FinalOutputApprovalProofSnapshot>> CreateCoreAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        bool allowApprovedCheckpoint,
        CancellationToken cancellationToken)
    {
        if (task.ActiveRunAttemptId is null ||
            task.WorkspaceId != workspace.Id ||
            workspace.TaskId != task.Id ||
            workspace.Status != ArtifactWorkspaceStatus.Active ||
            task.RunFencingToken <= 0)
        {
            return Conflict("Final-output approval requires the exact active task, workspace, and run attempt.");
        }

        var topology = AgentFinalizationCheckpointStateValidator.LoadExactFinalStep(task);
        var provenance = AgentFinalizationCheckpointStateValidator.ValidateArtifactProvenance(task, workspace);
        if (!topology.IsSuccess || !provenance.IsSuccess)
        {
            return Conflict("Final-output approval requires the canonical final step and artifact provenance.");
        }

        var finalStep = topology.Value!;
        if (finalStep.Status != AgentStepStatus.WaitingApproval &&
            (!allowApprovedCheckpoint || finalStep.Status != AgentStepStatus.Approved) ||
            finalStep.OutputJson is not null ||
            finalStep.ErrorMessage is not null ||
            finalStep.StartedAt is not null ||
            finalStep.FinishedAt is not null)
        {
            return Conflict("Final-output approval requires an untouched approval-waiting final step.");
        }

        var attempt = await runAttemptStore.FirstByIdAsync(
            task.ActiveRunAttemptId.Value,
            cancellationToken);
        if (attempt is null ||
            attempt.TaskId != task.Id ||
            attempt.TaskFencingToken != task.RunFencingToken ||
            attempt.Status is not (AgentTaskRunAttemptStatus.Running or AgentTaskRunAttemptStatus.WaitingApproval) ||
            attempt.CompletedAt is not null)
        {
            return Conflict("Final-output approval run-attempt authority is missing or stale.");
        }

        var nodes = await nodeRunStore.ListByAttemptAsync(attempt.Id, cancellationToken);
        var finalNodes = nodes.Where(node =>
                string.Equals(
                    node.ToolCode,
                    BuiltInToolRegistrations.FinalizationCheckpointToolCode,
                    StringComparison.Ordinal))
            .ToArray();
        if (finalNodes.Length != 1)
        {
            return Conflict("Final-output approval requires one durable finalization NodeRun.");
        }

        var finalNode = finalNodes[0];
        if (finalNode.TaskId != task.Id ||
            finalNode.RunAttemptId != attempt.Id ||
            finalNode.TaskFencingToken != task.RunFencingToken ||
            finalNode.Status != AgentNodeRunStatus.WaitingApproval ||
            !finalNode.RequiresApproval ||
            finalNode.SideEffectClass != AgentNodeSideEffectClass.ArtifactWrite ||
            finalNode.LeaseId is not null ||
            finalNode.LeaseOwner is not null ||
            finalNode.LeaseExpiresAt is not null ||
            finalNode.NodeFencingToken < 0)
        {
            return Conflict("Final-output approval NodeRun authority is missing, leased, or stale.");
        }

        var evidence = await nodeRunStore.ListEvidenceByAttemptAsync(attempt.Id, cancellationToken);
        var parentEvidence = SelectParentEvidence(
            task,
            attempt,
            finalNode,
            nodes,
            evidence,
            DateTimeOffset.UtcNow);
        if (!parentEvidence.IsSuccess ||
            !AgentEvidenceSetDigestAuthority.TryComputeEffective(
                parentEvidence.Value!,
                out var evidenceSetDigest) ||
            evidenceSetDigest is null)
        {
            return Conflict("Final-output approval could not bind the authoritative Evidence set.");
        }

        var bindings = new List<FinalOutputApprovalArtifactBinding>(workspace.Artifacts.Count);
        foreach (var artifact in workspace.Artifacts.OrderBy(item => item.Id.Value))
        {
            if (artifact.CreatedByStepId is null)
            {
                return Conflict("Final-output approval artifact producer binding is missing.");
            }

            var sourcePath = ArtifactPathGuard.NormalizeRelativePath(artifact.RelativePath);
            var source = await fileStore.OpenReadAsync(
                workspace.WorkspaceCode,
                sourcePath,
                artifact.MimeType,
                cancellationToken);
            if (source is null)
            {
                return Conflict("Final-output approval source file is missing.");
            }

            string sha256;
            await using (source.Stream)
            {
                sha256 = Convert.ToHexString(
                        await SHA256.HashDataAsync(source.Stream, cancellationToken))
                    .ToLowerInvariant();
            }

            if (source.FileSize != artifact.FileSize ||
                !string.Equals(source.MimeType, artifact.MimeType, StringComparison.OrdinalIgnoreCase))
            {
                return Conflict("Final-output approval source bytes do not match artifact metadata.");
            }

            bindings.Add(new FinalOutputApprovalArtifactBinding(
                artifact.Id.Value,
                artifact.CreatedByStepId.Value.Value,
                artifact.Version,
                sourcePath,
                artifact.FileSize,
                artifact.MimeType,
                sha256));
        }

        if (bindings.Count == 0 ||
            bindings.Select(binding => binding.SourceRelativePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != bindings.Count)
        {
            return Conflict("Final-output approval artifact binding set is empty or ambiguous.");
        }

        var bindingsJson = CanonicalJson.Serialize(bindings);
        var bindingDigest = CanonicalJson.ComputeSha256(bindingsJson);
        var manifestDigest = CanonicalJson.ComputeSha256(CanonicalJson.Serialize(new
        {
            version = "final-output-source-manifest-v1",
            taskId = task.Id.Value,
            workspaceId = workspace.Id.Value,
            workspaceCode = workspace.WorkspaceCode,
            finalStepId = finalStep.Id.Value,
            activeRunAttemptId = attempt.Id.Value,
            finalNodeRunId = finalNode.Id.Value,
            taskFencingToken = task.RunFencingToken,
            nodeFencingToken = checked(finalNode.NodeFencingToken + 1),
            evidenceSetDigest,
            artifactBindingDigest = bindingDigest,
            artifacts = bindings
        }));
        var proof = new FinalOutputApprovalProof(
            workspace.Id,
            workspace.WorkspaceCode,
            finalStep.Id,
            attempt.Id,
            finalNode.Id,
            task.RunFencingToken,
            checked(finalNode.NodeFencingToken + 1),
            evidenceSetDigest,
            manifestDigest,
            bindingsJson,
            bindingDigest);
        proof.Validate();
        return Result.Success(new FinalOutputApprovalProofSnapshot(
            proof,
            attempt,
            finalNode,
            parentEvidence.Value!));
    }

    internal async Task<Result<FinalOutputApprovalProofSnapshot>> VerifyAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        FinalOutputApprovalProof expected,
        bool allowApprovedCheckpoint,
        CancellationToken cancellationToken)
    {
        var current = await CreateCoreAsync(
            task,
            workspace,
            allowApprovedCheckpoint,
            cancellationToken);
        return current.IsSuccess && current.Value!.Proof == expected
            ? current
            : Conflict("Final-output approval proof no longer matches current authoritative state.");
    }

    private static Result<IReadOnlyCollection<AgentEvidenceRecord>> SelectParentEvidence(
        AgentTask task,
        AgentTaskRunAttempt attempt,
        AgentNodeRun finalNode,
        IReadOnlyCollection<AgentNodeRun> nodes,
        IReadOnlyCollection<AgentEvidenceRecord> evidence,
        DateTimeOffset nowUtc)
    {
        string[] dependencies;
        try
        {
            dependencies = JsonSerializer.Deserialize<string[]>(
                               finalNode.DependenciesJson,
                               CanonicalJson.SerializerOptions)
                           ?? [];
        }
        catch (JsonException)
        {
            return EvidenceConflict();
        }

        if (dependencies.Length == 0 ||
            dependencies.Distinct(StringComparer.Ordinal).Count() != dependencies.Length)
        {
            return EvidenceConflict();
        }

        var producers = nodes
            .Where(node => dependencies.Contains(node.NodeId, StringComparer.Ordinal))
            .GroupBy(node => node.NodeId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var selected = new List<AgentEvidenceRecord>(dependencies.Length);
        foreach (var dependency in dependencies)
        {
            if (!producers.TryGetValue(dependency, out var matches) || matches.Length != 1)
            {
                return EvidenceConflict();
            }

            var producer = matches[0];
            var evidenceMatches = evidence
                .Where(item => string.Equals(item.NodeId, dependency, StringComparison.Ordinal))
                .ToArray();
            if (evidenceMatches.Length == 0 &&
                finalNode.JoinPolicy == "OptionalBestEffort" &&
                !producer.IsRequired &&
                producer.Status is AgentNodeRunStatus.Failed or AgentNodeRunStatus.Cancelled)
            {
                continue;
            }

            if (evidenceMatches.Length != 1 ||
                !AgentEvidenceAccessChecker.ValidateDurable(
                        evidenceMatches[0],
                        task,
                        attempt.Id.Value,
                        producer,
                        nowUtc)
                    .IsSuccess)
            {
                return EvidenceConflict();
            }

            selected.Add(evidenceMatches[0]);
        }

        return Result.Success<IReadOnlyCollection<AgentEvidenceRecord>>(selected);
    }

    private static Result<IReadOnlyCollection<AgentEvidenceRecord>> EvidenceConflict() =>
        Result.Failure(new ApiProblemDescriptor(
            AppProblemCodes.AgentFinalizationStateConflict,
            "Final-output approval Evidence lineage is incomplete or inconsistent."));

    private static Result<FinalOutputApprovalProofSnapshot> Conflict(string detail) =>
        Result.Failure(new ApiProblemDescriptor(
            AppProblemCodes.AgentFinalizationStateConflict,
            detail));
}
