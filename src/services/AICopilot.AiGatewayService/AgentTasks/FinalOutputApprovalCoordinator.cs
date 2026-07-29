using AICopilot.AiGatewayService.Sessions;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.AgentTasks;
using AICopilot.Core.AiGateway.Specifications.Approvals;
using AICopilot.Core.AiGateway.Specifications.Artifacts;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed record FinalOutputApprovalPreparationOutcome(
    ApprovalRequest Approval,
    AgentTask Task,
    ArtifactWorkspace Workspace,
    AgentTaskRunAttempt ActiveAttempt,
    bool IsCreated);

public sealed class FinalOutputApprovalCoordinator(
    IFinalOutputApprovalStore finalOutputApprovalStore,
    FinalOutputApprovalProofFactory proofFactory,
    IRepository<ApprovalRequest> approvalRepository,
    IReadRepository<ArtifactWorkspace> workspaceRepository,
    IAgentTaskRunQueueStore queueStore,
    AgentAuditRecorder auditRecorder,
    IAuditLogWriter auditLogWriter,
    MessageTimelineProjectionWriter? timelineProjectionWriter = null)
{
    public async Task<Result<FinalOutputApprovalPreparationOutcome>> EnsurePendingAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        Guid requestedBy,
        CancellationToken cancellationToken)
    {
        var existingApprovals = await approvalRepository.ListAsync(
            new ApprovalRequestsByTaskSpec(task.Id),
            cancellationToken);
        var existingFinalApprovals = existingApprovals
            .Where(candidate => candidate.ApprovalType == AgentApprovalType.FinalOutput)
            .ToArray();
        if (existingFinalApprovals.Length > 1)
        {
            await RecordPreparationConflictAsync(task, workspace, cancellationToken);
            return Failure<FinalOutputApprovalPreparationOutcome>(
                AppProblemCodes.AgentFinalizationStateConflict,
                "Final-output approval history is ambiguous.");
        }

        if (existingFinalApprovals.Length == 1 &&
            existingFinalApprovals[0].Status == AgentApprovalStatus.Rejected)
        {
            return existingFinalApprovals[0].HasValidFinalOutputProof() &&
                   existingFinalApprovals[0].HasValidFinalOutputDecisionProof()
                ? Failure<FinalOutputApprovalPreparationOutcome>(
                    AppProblemCodes.AgentApprovalRejected,
                    "Final output approval was rejected.")
                : Failure<FinalOutputApprovalPreparationOutcome>(
                    AppProblemCodes.AgentFinalizationStateConflict,
                    "Rejected final-output approval proof is missing or inconsistent.");
        }

        var proof = await proofFactory.CreateAsync(task, workspace, cancellationToken);
        if (!proof.IsSuccess)
        {
            await RecordPreparationConflictAsync(task, workspace, cancellationToken);
            return Result.From(proof);
        }

        var prepared = await finalOutputApprovalStore.PrepareAsync(
            new FinalOutputApprovalPreparation(
                task.Id,
                requestedBy,
                proof.Value!.Proof,
                DateTimeOffset.UtcNow),
            cancellationToken);
        if (prepared.Status is not (
                FinalOutputApprovalCommandStatus.Created or
                FinalOutputApprovalCommandStatus.ExistingPending))
        {
            await RecordCommandOutcomeAsync(
                prepared,
                isApproved: null,
                cancellationToken);
            return MapPreparationFailure(prepared.Status);
        }

        var approval = prepared.Approval!;
        var preparedTask = prepared.Task!;
        var preparedWorkspace = prepared.Workspace!;
        if (prepared.StateChanged)
        {
            await auditRecorder.RecordFinalReviewSubmittedAsync(
                preparedTask,
                preparedWorkspace,
                approval,
                cancellationToken);
            if (timelineProjectionWriter is not null)
            {
                await timelineProjectionWriter.StageApprovalRequestedAsync(
                    preparedTask,
                    approval,
                    cancellationToken);
            }

            await approvalRepository.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(new FinalOutputApprovalPreparationOutcome(
            approval,
            preparedTask,
            preparedWorkspace,
            prepared.RunAttempt!,
            prepared.Status == FinalOutputApprovalCommandStatus.Created));
    }

    public async Task<Result<FinalOutputApprovalCommandResult>> DecideAsync(
        ApprovalRequest approval,
        AgentTask task,
        Guid decidedBy,
        bool isApproved,
        string? comment,
        CancellationToken cancellationToken)
    {
        FinalOutputApprovalProof? currentProof = null;
        if (approval.Status == AgentApprovalStatus.Pending &&
            approval.HasValidFinalOutputProof() &&
            task.WorkspaceId is not null)
        {
            var workspace = await workspaceRepository.FirstOrDefaultAsync(
                new ArtifactWorkspaceByIdSpec(task.WorkspaceId.Value, includeArtifacts: true),
                cancellationToken);
            if (workspace is not null)
            {
                var verified = await proofFactory.VerifyAsync(
                    task,
                    workspace,
                    approval.GetFinalOutputProof(),
                    allowApprovedCheckpoint: false,
                    cancellationToken);
                currentProof = verified.IsSuccess ? verified.Value!.Proof : null;
            }
        }

        var decided = await finalOutputApprovalStore.DecideAsync(
            new FinalOutputApprovalDecision(
                approval.Id,
                decidedBy,
                isApproved,
                comment,
                currentProof,
                DateTimeOffset.UtcNow),
            cancellationToken);
        await RecordCommandOutcomeAsync(decided, isApproved, cancellationToken);

        return decided.Status switch
        {
            FinalOutputApprovalCommandStatus.Approved or
            FinalOutputApprovalCommandStatus.Rejected or
            FinalOutputApprovalCommandStatus.DuplicateDecision =>
                Result.Success(decided),
            FinalOutputApprovalCommandStatus.DecisionConflict =>
                Failure(
                    AppProblemCodes.AgentApprovalStateConflict,
                    "Final-output approval already has a different immutable decision."),
            FinalOutputApprovalCommandStatus.ApprovalRejected =>
                Failure(
                    AppProblemCodes.AgentApprovalRejected,
                    "Final output approval was rejected."),
            FinalOutputApprovalCommandStatus.NotFound => Result.NotFound(),
            _ => Failure(
                AppProblemCodes.AgentFinalizationStateConflict,
                "Final-output approval proof or checkpoint state is inconsistent.")
        };
    }

    public async Task<Result> ValidateLegacyResumeAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        CancellationToken cancellationToken)
    {
        if (task.Status == AgentTaskStatus.Completed &&
            workspace.Status == ArtifactWorkspaceStatus.Finalized)
        {
            return Result.Success();
        }

        var approvals = await approvalRepository.ListAsync(
            new ApprovalRequestsByTaskSpec(task.Id),
            cancellationToken);
        var finalApprovals = approvals
            .Where(candidate => candidate.ApprovalType == AgentApprovalType.FinalOutput)
            .ToArray();
        if (finalApprovals.Length != 1 ||
            !finalApprovals[0].HasValidFinalOutputProof())
        {
            return Failure(
                AppProblemCodes.AgentFinalizationStateConflict,
                "Legacy finalize requires one proof-bound final-output approval.");
        }

        var approval = finalApprovals[0];
        if (approval.Status == AgentApprovalStatus.Rejected)
        {
            return Failure(
                AppProblemCodes.AgentApprovalRejected,
                "Final output approval was rejected.");
        }

        if (approval.Status != AgentApprovalStatus.Approved)
        {
            return Failure(
                AppProblemCodes.AgentFinalizationStateConflict,
                "Final output approval is still pending.");
        }

        var verified = await proofFactory.VerifyAsync(
            task,
            workspace,
            approval.GetFinalOutputProof(),
            allowApprovedCheckpoint: true,
            cancellationToken);
        if (!verified.IsSuccess)
        {
            return Result.From(verified);
        }

        var queueItems = await queueStore.ListByTaskAsync(task.Id, cancellationToken);
        var approvalResume = queueItems.Where(item =>
                item.TriggerType == AgentTaskRunTriggerType.ApprovalResume &&
                item.SourceApprovalRequestId == approval.Id)
            .ToArray();
        if (approvalResume.Length != 1)
        {
            return Failure(
                AppProblemCodes.AgentFinalizationStateConflict,
                "Approved final output is missing its unique durable resume queue item.");
        }

        return Failure(
            AppProblemCodes.AgentFinalizationStateConflict,
            "Legacy synchronous finalize is closed; the proof-bound durable worker owns final publication.");
    }

    internal async Task<Result> VerifyCheckpointAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        ApprovalRequest approval,
        bool allowApprovedCheckpoint,
        CancellationToken cancellationToken)
    {
        if (!approval.HasValidFinalOutputProof())
        {
            return Failure(
                AppProblemCodes.AgentFinalizationStateConflict,
                "Final-output checkpoint approval proof is missing or invalid.");
        }

        var verified = await proofFactory.VerifyAsync(
            task,
            workspace,
            approval.GetFinalOutputProof(),
            allowApprovedCheckpoint,
            cancellationToken);
        return Result.From(verified);
    }

    private async Task RecordCommandOutcomeAsync(
        FinalOutputApprovalCommandResult command,
        bool? isApproved,
        CancellationToken cancellationToken)
    {
        if (command.Approval is not null && command.Task is not null)
        {
            var auditResult = command.Status switch
            {
                FinalOutputApprovalCommandStatus.Approved => AuditResults.Succeeded,
                FinalOutputApprovalCommandStatus.Rejected => AuditResults.Rejected,
                FinalOutputApprovalCommandStatus.DuplicateDecision => AuditResults.Succeeded,
                _ => AuditResults.Failed
            };
            var summary = command.Status switch
            {
                FinalOutputApprovalCommandStatus.Approved =>
                    "Final-output approval decision committed with one durable resume queue item.",
                FinalOutputApprovalCommandStatus.Rejected =>
                    "Final-output rejection committed and terminal task state recorded.",
                FinalOutputApprovalCommandStatus.DuplicateDecision =>
                    "Idempotent final-output decision returned the existing immutable result.",
                FinalOutputApprovalCommandStatus.DecisionConflict =>
                    "Conflicting final-output decision was rejected.",
                _ => "Final-output approval proof or checkpoint conflict was rejected."
            };
            await auditRecorder.RecordApprovalDecisionAsync(
                command.Approval,
                command.Task,
                auditResult,
                summary,
                cancellationToken);
            if (command.StateChanged && timelineProjectionWriter is not null)
            {
                await timelineProjectionWriter.StageApprovalDecidedAsync(
                    command.Task,
                    command.Approval,
                    cancellationToken);
            }

            await approvalRepository.SaveChangesAsync(cancellationToken);
            return;
        }

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "Agent.ApprovalDecision",
                "ApprovalRequest",
                command.Approval?.Id.Value.ToString(),
                "FinalOutput",
                AuditResults.Failed,
                "Final-output approval request could not be resolved to an authoritative checkpoint.",
                Metadata: new Dictionary<string, string>
                {
                    ["decision"] = isApproved.HasValue
                        ? isApproved.Value ? "Approved" : "Rejected"
                        : "Prepare",
                    ["status"] = command.Status.ToString()
                }),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);
    }

    private async Task RecordPreparationConflictAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        CancellationToken cancellationToken)
    {
        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "Agent.FinalReviewSubmitted",
                "AgentTask",
                task.Id.Value.ToString(),
                task.TaskCode,
                AuditResults.Failed,
                "Final-output approval proof could not be created from authoritative state.",
                Metadata: new Dictionary<string, string>
                {
                    ["taskId"] = task.Id.Value.ToString(),
                    ["workspaceCode"] = workspace.WorkspaceCode,
                    ["status"] = "FinalizationConflict"
                }),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);
    }

    private static Result<FinalOutputApprovalPreparationOutcome> MapPreparationFailure(
        FinalOutputApprovalCommandStatus status) =>
        status switch
        {
            FinalOutputApprovalCommandStatus.ApprovalRejected =>
                Failure<FinalOutputApprovalPreparationOutcome>(
                    AppProblemCodes.AgentApprovalRejected,
                    "Final output approval was rejected."),
            FinalOutputApprovalCommandStatus.NotFound =>
                Result.NotFound(),
            _ => Failure<FinalOutputApprovalPreparationOutcome>(
                AppProblemCodes.AgentFinalizationStateConflict,
                "Final-output approval proof or checkpoint state is inconsistent.")
        };

    private static Result Failure(string code, string detail) =>
        Result.Failure(new ApiProblemDescriptor(code, detail));

    private static Result<T> Failure<T>(string code, string detail) =>
        Result.Failure(new ApiProblemDescriptor(code, detail));
}
