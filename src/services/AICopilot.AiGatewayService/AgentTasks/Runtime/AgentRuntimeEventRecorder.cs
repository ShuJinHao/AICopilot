using AICopilot.AiGatewayService.Sessions;
using AICopilot.AiGatewayService.Workspaces;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class AgentRuntimeEventRecorder(
    IToolExecutionAuditStore toolExecutionAuditStore,
    AgentAuditRecorder auditRecorder,
    MessageTimelineProjectionWriter? timelineProjectionWriter = null)
{
    public Task StageApprovalRequestedAsync(
        AgentTask task,
        ApprovalRequest approval,
        CancellationToken cancellationToken)
    {
        return timelineProjectionWriter is null
            ? Task.CompletedTask
            : timelineProjectionWriter.StageApprovalRequestedAsync(task, approval, cancellationToken);
    }

    public async Task StageFinalReviewSubmittedAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        ApprovalRequest approval,
        CancellationToken cancellationToken)
    {
        await auditRecorder.RecordFinalReviewSubmittedAsync(
            task,
            workspace,
            approval,
            cancellationToken);
        await StageApprovalRequestedAsync(task, approval, cancellationToken);
    }

    public Task StageStepStartedAsync(
        AgentTask task,
        AgentStep step,
        CancellationToken cancellationToken)
    {
        return timelineProjectionWriter is null
            ? Task.CompletedTask
            : timelineProjectionWriter.StageStepStartedAsync(task, step, cancellationToken);
    }

    public Task StageStepCompletedAsync(
        AgentTask task,
        AgentStep step,
        CancellationToken cancellationToken)
    {
        return timelineProjectionWriter is null
            ? Task.CompletedTask
            : timelineProjectionWriter.StageStepCompletedAsync(task, step, cancellationToken);
    }

    public AgentToolExecutionAuditScope BeginToolExecution(
        AgentTask task,
        AgentStep step,
        ToolRegistration? toolRegistration,
        AgentTaskRunAttempt attempt,
        DateTimeOffset now)
    {
        var record = new ToolExecutionRecord(
            task.Id,
            step.Id,
            step.ToolCode ?? toolRegistration?.ToolCode ?? "unknown",
            AgentToolExecutionAuditBuilder.BuildInputSummary(step, toolRegistration),
            now,
            attempt.Id);
        toolExecutionAuditStore.Add(record);
        return new AgentToolExecutionAuditScope(record);
    }

    public string? MarkToolExecutionSucceeded(
        AgentToolExecutionAuditScope scope,
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentStep step,
        ToolRegistration toolRegistration,
        object output,
        DateTimeOffset now)
    {
        var artifactId = AgentToolExecutionAuditBuilder.ExtractArtifactId(output);
        scope.MarkSucceeded(
            AgentToolExecutionAuditBuilder.BuildOutputSummary(output),
            artifactId,
            AgentToolExecutionAuditBuilder.BuildAuditMetadata(task, workspace, step, toolRegistration, output),
            now);
        return artifactId;
    }

    public Task RecordToolSucceededAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentStep step,
        string? artifactId,
        CancellationToken cancellationToken)
    {
        return auditRecorder.RecordToolAsync(
            task,
            workspace,
            step,
            AuditResults.Succeeded,
            $"Agent step {step.StepIndex} executed.",
            artifactId,
            cancellationToken);
    }

    public async Task RecordToolFailedAsync(
        AgentToolExecutionAuditScope? scope,
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentStep step,
        ToolRegistration? toolRegistration,
        AgentTaskRunAttempt attempt,
        string errorCode,
        string safeMessage,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        scope ??= BeginToolExecution(task, step, toolRegistration, attempt, now);
        if (scope.Status == ToolExecutionStatus.Running)
        {
            scope.MarkFailed(
                errorCode,
                safeMessage,
                AgentToolExecutionAuditBuilder.BuildAuditMetadata(task, workspace, step, toolRegistration),
                now);
        }

        await auditRecorder.RecordToolAsync(
            task,
            workspace,
            step,
            AuditResults.Rejected,
            safeMessage,
            null,
            cancellationToken);
    }

    public async Task RecordToolRejectedAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentStep step,
        AgentTaskRunAttempt attempt,
        string errorCode,
        string safeMessage,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var scope = BeginToolExecution(task, step, null, attempt, now);
        scope.MarkRejected(
            errorCode,
            safeMessage,
            AgentToolExecutionAuditBuilder.BuildAuditMetadata(task, workspace, step, null),
            now);

        await auditRecorder.RecordToolAsync(
            task,
            workspace,
            step,
            AuditResults.Rejected,
            safeMessage,
            null,
            cancellationToken);
    }
}

internal sealed class AgentToolExecutionAuditScope(ToolExecutionRecord record)
{
    public ToolExecutionStatus Status => record.Status;

    public void MarkSucceeded(
        string? outputSummary,
        string? artifactId,
        string metadataJson,
        DateTimeOffset now)
    {
        record.MarkSucceeded(outputSummary, artifactId, metadataJson, now);
    }

    public void MarkFailed(
        string errorCode,
        string errorMessage,
        string metadataJson,
        DateTimeOffset now)
    {
        record.MarkFailed(errorCode, errorMessage, metadataJson, now);
    }

    public void MarkRejected(
        string errorCode,
        string errorMessage,
        string metadataJson,
        DateTimeOffset now)
    {
        record.MarkRejected(errorCode, errorMessage, metadataJson, now);
    }
}
