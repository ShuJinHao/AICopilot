using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal enum AgentFinalizationCheckpointPhase
{
    PendingApproval = 0,
    Approved = 1
}

internal sealed record AgentFinalizationCheckpointState(
    AgentStep Step,
    ApprovalRequest Approval,
    AgentTaskRunAttempt ActiveAttempt,
    AgentFinalizationCheckpointPhase Phase);

internal static class AgentFinalizationCheckpointStateValidator
{
    public static Result<AgentFinalizationCheckpointState> ValidatePaused(
        AgentTask task,
        ArtifactWorkspace? workspace,
        IReadOnlyCollection<ApprovalRequest> approvals,
        IReadOnlyCollection<AgentTaskRunAttempt> attempts)
    {
        return ValidateCore(
            task,
            workspace,
            approvals,
            attempts,
            resumedClaim: null,
            nowUtc: null);
    }

    public static Result<AgentFinalizationCheckpointState> ValidateResumed(
        AgentTask task,
        ArtifactWorkspace? workspace,
        IReadOnlyCollection<ApprovalRequest> approvals,
        IReadOnlyCollection<AgentTaskRunAttempt> attempts,
        DurableTaskClaim resumedClaim,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(resumedClaim);
        return ValidateCore(
            task,
            workspace,
            approvals,
            attempts,
            resumedClaim,
            nowUtc);
    }

    private static Result<AgentFinalizationCheckpointState> ValidateCore(
        AgentTask task,
        ArtifactWorkspace? workspace,
        IReadOnlyCollection<ApprovalRequest> approvals,
        IReadOnlyCollection<AgentTaskRunAttempt> attempts,
        DurableTaskClaim? resumedClaim,
        DateTimeOffset? nowUtc)
    {
        if (workspace is null ||
            task.WorkspaceId != workspace.Id ||
            workspace.TaskId != task.Id ||
            workspace.Status != ArtifactWorkspaceStatus.Active ||
            workspace.Artifacts.Count == 0 ||
            workspace.Artifacts.Any(artifact =>
                artifact.WorkspaceId != workspace.Id ||
                artifact.TaskId != task.Id ||
                artifact.Status is not (ArtifactStatus.Draft or ArtifactStatus.Reviewing or ArtifactStatus.Approved) ||
                artifact.FinalizedAt is not null ||
                artifact.RelativePath.StartsWith("final/", StringComparison.OrdinalIgnoreCase)))
        {
            return Conflict(
                "Final-output checkpoint requires the exact active workspace with at least one persisted artifact.");
        }

        var topology = LoadExactFinalStep(task);
        if (!topology.IsSuccess ||
            task.Status != AgentTaskStatus.WaitingFinalApproval ||
            task.CompletedAt is not null ||
            task.FinalSummary is not null)
        {
            return Conflict(
                "Final-output checkpoint requires one canonical last step and a waiting-final-approval task.");
        }

        var provenance = ValidateArtifactProvenance(task, workspace);
        if (!provenance.IsSuccess)
        {
            return Conflict(
                "Final-output checkpoint artifacts are not bound to completed artifact-generation steps.");
        }

        var finalStep = topology.Value!;
        if (finalStep.OutputJson is not null ||
            finalStep.ErrorMessage is not null ||
            finalStep.StartedAt is not null ||
            finalStep.FinishedAt is not null)
        {
            return Conflict(
                "Final-output checkpoint step contains stale execution metadata.");
        }

        if (task.ActiveRunAttemptId is null)
        {
            return Conflict(
                "Final-output checkpoint requires an active run attempt.");
        }

        if (resumedClaim is null)
        {
            if (task.RunLeaseId is not null ||
                task.RunLeaseOwner is not null ||
                task.RunLeaseExpiresAt is not null)
            {
                return Conflict(
                    "Paused final-output checkpoint requires a lease-free active run attempt.");
            }
        }
        else if (nowUtc is null ||
                 resumedClaim.Task.Id != task.Id ||
                 resumedClaim.RunAttempt.Id != task.ActiveRunAttemptId.Value ||
                 resumedClaim.TaskFencingToken != task.RunFencingToken ||
                 resumedClaim.LeaseId != task.RunLeaseId ||
                 task.RunLeaseOwner is null ||
                 task.RunLeaseExpiresAt is null ||
                 task.RunLeaseExpiresAt <= nowUtc.Value)
        {
            return Conflict(
                "Resumed final-output checkpoint task lease is stale or inconsistent.");
        }

        var activeAttempt = attempts.SingleOrDefault(attempt => attempt.Id == task.ActiveRunAttemptId.Value);
        var attemptTimeline = ValidateRunAttemptTimeline(
            task,
            attempts,
            resumedClaim?.RunAttempt.Id);
        var activeAttemptStateValid = resumedClaim is null
            ? activeAttempt?.Status == AgentTaskRunAttemptStatus.WaitingApproval &&
              activeAttempt.LeaseId is null &&
              activeAttempt.LeaseOwner is null &&
              activeAttempt.LeaseExpiresAt is null
            : activeAttempt?.Status == AgentTaskRunAttemptStatus.Running &&
              activeAttempt.TaskFencingToken == resumedClaim.TaskFencingToken &&
              activeAttempt.LeaseId == resumedClaim.LeaseId &&
              activeAttempt.LeaseOwner is not null &&
              activeAttempt.LeaseExpiresAt is not null &&
              activeAttempt.LeaseExpiresAt > nowUtc!.Value;
        if (activeAttempt is null ||
            !attemptTimeline.IsSuccess ||
            attemptTimeline.Value![^1].Id != activeAttempt.Id ||
            activeAttempt.TaskId != task.Id ||
            !activeAttemptStateValid ||
            activeAttempt.CompletedAt is not null ||
            activeAttempt.FailureCode is not null ||
            task.RunAttemptCount != activeAttempt.AttemptNo ||
            attemptTimeline.Value![..^1].Any(attempt => !attempt.IsTerminal))
        {
            return Conflict(
                "Final-output checkpoint run-attempt state is missing, leased, or inconsistent.");
        }

        if (approvals.Any(approval => approval.TaskId != task.Id))
        {
            return Conflict("Final-output checkpoint approval set contains a foreign task identity.");
        }

        var finalApprovals = approvals
            .Where(approval => approval.ApprovalType == AgentApprovalType.FinalOutput)
            .ToArray();
        if (finalApprovals.Length != 1 ||
            !string.Equals(
                finalApprovals[0].TargetId,
                workspace.WorkspaceCode,
                StringComparison.Ordinal) ||
            finalApprovals[0].RequestedBy != task.UserId)
        {
            return Conflict(
                "Final-output checkpoint requires exactly one task-bound workspace approval.");
        }

        var finalApproval = finalApprovals[0];
        var approvalProof = ValidateApprovalDecisionProof(finalApproval);
        if (!approvalProof.IsSuccess)
        {
            return Conflict(
                "Final-output approval decision proof is incomplete or inconsistent.");
        }

        if (finalApproval.CreatedAt < task.CreatedAt ||
            finalApproval.CreatedAt < workspace.CreatedAt ||
            task.Steps.Any(step =>
                step.StepType is AgentStepType.ArtifactGeneration or AgentStepType.ChartGeneration &&
                !BuiltInToolRegistrations.IsLifecycleCheckpoint(step.ToolCode) &&
                (step.FinishedAt is null || step.FinishedAt.Value > finalApproval.CreatedAt)))
        {
            return Conflict(
                "Final-output approval was created before the persisted artifact producers completed.");
        }

        var pendingApprovals = approvals
            .Where(approval => approval.Status == AgentApprovalStatus.Pending)
            .ToArray();
        AgentFinalizationCheckpointPhase phase;
        if (finalApproval.Status == AgentApprovalStatus.Pending &&
            finalStep.Status == AgentStepStatus.WaitingApproval &&
            pendingApprovals.Length == 1 &&
            pendingApprovals[0].Id == finalApproval.Id)
        {
            phase = AgentFinalizationCheckpointPhase.PendingApproval;
        }
        else if (finalApproval.Status == AgentApprovalStatus.Approved &&
                 finalStep.Status == AgentStepStatus.Approved &&
                 pendingApprovals.Length == 0)
        {
            phase = AgentFinalizationCheckpointPhase.Approved;
        }
        else
        {
            return Conflict(
                "Final-output approval, checkpoint step, and pending-approval set are inconsistent.");
        }

        if (resumedClaim is not null && phase != AgentFinalizationCheckpointPhase.Approved)
        {
            return Conflict(
                "Only an approved final-output checkpoint may resume under a worker lease.");
        }

        return Result.Success(new AgentFinalizationCheckpointState(
            finalStep,
            finalApproval,
            activeAttempt,
            phase));
    }

    internal static Result<AgentStep> LoadExactFinalStep(AgentTask task)
    {
        var orderedSteps = task.Steps.OrderBy(step => step.StepIndex).ToArray();
        var finalSteps = orderedSteps.Where(step => string.Equals(
                step.ToolCode,
                BuiltInToolRegistrations.FinalizationCheckpointToolCode,
                StringComparison.Ordinal))
            .ToArray();
        if (orderedSteps.Length == 0 ||
            !orderedSteps.Select(step => step.StepIndex)
                .SequenceEqual(Enumerable.Range(1, orderedSteps.Length)) ||
            orderedSteps.Any(step => step.TaskId != task.Id) ||
            finalSteps.Length != 1 ||
            finalSteps[0].StepIndex != orderedSteps[^1].StepIndex ||
            finalSteps[0].StepType != AgentStepType.Finalize ||
            !finalSteps[0].RequiresApproval ||
            orderedSteps[..^1].Any(step => step.StepType == AgentStepType.Finalize) ||
            orderedSteps[..^1].Any(step => step.Status != AgentStepStatus.Completed))
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentFinalizationStateConflict,
                "Final-output checkpoint step topology is incomplete or inconsistent."));
        }

        return Result.Success(finalSteps[0]);
    }

    internal static Result ValidateArtifactProvenance(
        AgentTask task,
        ArtifactWorkspace workspace)
    {
        var artifactSteps = task.Steps
            .Where(step =>
                step.Status == AgentStepStatus.Completed &&
                step.StepType is AgentStepType.ArtifactGeneration or AgentStepType.ChartGeneration &&
                !BuiltInToolRegistrations.IsLifecycleCheckpoint(step.ToolCode))
            .ToArray();
        if (workspace.CreatedAt < task.CreatedAt ||
            workspace.Artifacts.Count == 0 ||
            artifactSteps.Any(step =>
                step.ErrorMessage is not null ||
                step.StartedAt is null ||
                step.FinishedAt is null ||
                step.StartedAt.Value < workspace.CreatedAt ||
                step.FinishedAt.Value < step.StartedAt.Value) ||
            workspace.Artifacts.Any(artifact =>
                artifact.CreatedByStepId is null ||
                artifactSteps.Count(step =>
                    step.Id == artifact.CreatedByStepId.Value &&
                    artifact.CreatedAt >= step.StartedAt!.Value &&
                    artifact.CreatedAt <= step.FinishedAt!.Value &&
                    AgentArtifactOutputContractBinding.MatchesExact(step.OutputJson, artifact)) != 1) ||
            artifactSteps.Any(step =>
                workspace.Artifacts.Count(artifact => artifact.CreatedByStepId == step.Id) != 1))
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentFinalizationStateConflict,
                "Workspace artifacts must map one-to-one to completed artifact-generation steps."));
        }

        return Result.Success();
    }

    internal static Result<AgentTaskRunAttempt[]> ValidateRunAttemptTimeline(
        AgentTask task,
        IReadOnlyCollection<AgentTaskRunAttempt> attempts,
        AgentTaskRunAttemptId? leasedAttemptId = null)
    {
        var ordered = attempts.OrderBy(attempt => attempt.AttemptNo).ToArray();
        if (ordered.Length == 0 ||
            ordered.Length != task.RunAttemptCount ||
            !ordered.Select(attempt => attempt.AttemptNo)
                .SequenceEqual(Enumerable.Range(1, task.RunAttemptCount)) ||
            ordered.Any(attempt =>
                attempt.TaskId != task.Id ||
                attempt.StartedAt < task.CreatedAt ||
                attempt.Id != leasedAttemptId &&
                (attempt.LeaseId is not null ||
                 attempt.LeaseOwner is not null ||
                 attempt.LeaseExpiresAt is not null)))
        {
            return AttemptTimelineConflict();
        }

        for (var index = 1; index < ordered.Length; index++)
        {
            var previous = ordered[index - 1];
            var current = ordered[index];
            if (previous.CompletedAt is null ||
                previous.CompletedAt.Value < previous.StartedAt ||
                previous.CompletedAt.Value > current.StartedAt)
            {
                return AttemptTimelineConflict();
            }
        }

        return Result.Success(ordered);
    }

    internal static Result ValidateApprovalDecisionProof(ApprovalRequest approval)
    {
        var hasDecisionTime = approval.ApprovedAt is not null &&
                              approval.ApprovedAt.Value >= approval.CreatedAt;
        var isValid = approval.Status switch
        {
            AgentApprovalStatus.Pending =>
                approval.ApprovedBy is null &&
                approval.ApprovedAt is null &&
                approval.ApprovalComment is null,
            AgentApprovalStatus.Approved or AgentApprovalStatus.Rejected =>
                approval.ApprovedBy is not null &&
                approval.ApprovedBy.Value != Guid.Empty &&
                hasDecisionTime,
            AgentApprovalStatus.Cancelled or AgentApprovalStatus.Expired =>
                approval.ApprovedBy is null &&
                approval.ApprovalComment is null &&
                hasDecisionTime,
            _ => false
        };

        return isValid
            ? Result.Success()
            : Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentFinalizationStateConflict,
                "Final-output approval decision proof is incomplete or inconsistent."));
    }

    private static Result<AgentFinalizationCheckpointState> Conflict(string detail) =>
        Result.Failure(new ApiProblemDescriptor(
            AppProblemCodes.AgentFinalizationStateConflict,
            detail));

    private static Result<AgentTaskRunAttempt[]> AttemptTimelineConflict() =>
        Result.Failure(new ApiProblemDescriptor(
            AppProblemCodes.AgentFinalizationStateConflict,
            "Final-output run-attempt timeline is incomplete or causally inconsistent."));
}
