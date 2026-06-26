using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.Sessions;
using AICopilot.SharedKernel.Repository;

namespace AICopilot.AiGatewayService.Sessions;

public sealed class MessageTimelineProjectionWriter(
    IRepository<MessageEvent> repository,
    IReadRepository<Session> sessionRepository)
{
    private readonly Dictionary<SessionId, int> nextSequences = [];
    private readonly Dictionary<SessionId, bool> sessionExists = [];

    public async Task StageAgentTaskPlanCreatedAsync(
        AgentTask task,
        ApprovalRequest? approval,
        CancellationToken cancellationToken)
    {
        await StageAsync(
            task.SessionId,
            MessageEventType.AgentTaskPlanCreated,
            task.CreatedAt,
            task.Id,
            approvalRequestId: approval?.Id,
            artifactWorkspaceId: task.WorkspaceId,
            cancellationToken: cancellationToken);
        if (approval is not null)
        {
            await StageApprovalRequestedAsync(task, approval, cancellationToken);
        }
    }

    public Task StageApprovalRequestedAsync(
        AgentTask task,
        ApprovalRequest approval,
        CancellationToken cancellationToken)
    {
        return StageAsync(
            task.SessionId,
            MessageEventType.ApprovalRequested,
            approval.CreatedAt,
            task.Id,
            approvalRequestId: approval.Id,
            artifactWorkspaceId: task.WorkspaceId,
            cancellationToken: cancellationToken);
    }

    public Task StageApprovalDecidedAsync(
        AgentTask task,
        ApprovalRequest approval,
        CancellationToken cancellationToken)
    {
        return StageAsync(
            task.SessionId,
            MessageEventType.ApprovalDecided,
            approval.ApprovedAt ?? DateTimeOffset.UtcNow,
            task.Id,
            approvalRequestId: approval.Id,
            artifactWorkspaceId: task.WorkspaceId,
            cancellationToken: cancellationToken);
    }

    public Task StageStepStartedAsync(
        AgentTask task,
        AgentStep step,
        CancellationToken cancellationToken)
    {
        return StageAsync(
            task.SessionId,
            MessageEventType.AgentTaskStepStarted,
            step.StartedAt ?? DateTimeOffset.UtcNow,
            task.Id,
            agentStepId: step.Id,
            artifactWorkspaceId: task.WorkspaceId,
            cancellationToken: cancellationToken);
    }

    public Task StageStepCompletedAsync(
        AgentTask task,
        AgentStep step,
        CancellationToken cancellationToken)
    {
        return StageAsync(
            task.SessionId,
            MessageEventType.AgentTaskStepCompleted,
            step.FinishedAt ?? DateTimeOffset.UtcNow,
            task.Id,
            agentStepId: step.Id,
            artifactWorkspaceId: task.WorkspaceId,
            cancellationToken: cancellationToken);
    }

    public async Task StageWorkspaceFinalizedAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        CancellationToken cancellationToken)
    {
        foreach (var artifact in workspace.Artifacts.Where(item => item.Status == ArtifactStatus.Final))
        {
            await StageAsync(
                task.SessionId,
                MessageEventType.ArtifactReady,
                artifact.FinalizedAt ?? artifact.UpdatedAt,
                task.Id,
                artifactWorkspaceId: workspace.Id,
                artifactId: artifact.Id,
                cancellationToken: cancellationToken);
        }

        await StageAsync(
            task.SessionId,
            MessageEventType.FinalOutputReady,
            workspace.UpdatedAt,
            task.Id,
            artifactWorkspaceId: workspace.Id,
            cancellationToken: cancellationToken);
    }

    private async Task StageAsync(
        SessionId sessionId,
        MessageEventType eventType,
        DateTimeOffset createdAt,
        AgentTaskId? agentTaskId = null,
        AgentStepId? agentStepId = null,
        ApprovalRequestId? approvalRequestId = null,
        ArtifactWorkspaceId? artifactWorkspaceId = null,
        ArtifactId? artifactId = null,
        CancellationToken cancellationToken = default)
    {
        if (!await HasSessionAsync(sessionId, cancellationToken))
        {
            return;
        }

        repository.Add(MessageEvent.FromProjection(
            sessionId,
            await GetNextSequenceAsync(sessionId, cancellationToken),
            eventType,
            createdAt,
            agentTaskId,
            agentStepId,
            approvalRequestId,
            artifactWorkspaceId,
            artifactId));
    }

    private async Task<bool> HasSessionAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        if (!sessionExists.TryGetValue(sessionId, out var exists))
        {
            exists = await sessionRepository.AnyAsync(new SessionByIdSpec(sessionId), cancellationToken);
            sessionExists[sessionId] = exists;
        }

        return exists;
    }

    private async Task<int> GetNextSequenceAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        if (!nextSequences.TryGetValue(sessionId, out var current))
        {
            var existingEvents = await repository.ListAsync(
                new MessageEventsBySessionSpec(sessionId),
                cancellationToken);
            current = existingEvents.Count == 0 ? 0 : existingEvents.Max(item => item.Sequence);
        }

        nextSequences[sessionId] = current + 1;
        return nextSequences[sessionId];
    }
}
