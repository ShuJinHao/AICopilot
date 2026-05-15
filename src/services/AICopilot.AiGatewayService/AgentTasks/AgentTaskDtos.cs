using AICopilot.Core.AiGateway.Aggregates.AgentTasks;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed record AgentStepPlanDto(
    string Title,
    string Description,
    AgentStepType StepType,
    string? ToolCode,
    bool RequiresApproval);

public sealed record AgentStepDto(
    Guid Id,
    int StepIndex,
    string Title,
    string Description,
    string StepType,
    string Status,
    string? ToolCode,
    bool RequiresApproval,
    string? ErrorMessage);

public sealed record AgentTaskDto(
    Guid Id,
    string TaskCode,
    Guid SessionId,
    string Title,
    string Goal,
    string TaskType,
    string Status,
    string RiskLevel,
    Guid? ModelId,
    Guid? WorkspaceId,
    string PlanJson,
    string? FinalSummary,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyCollection<AgentStepDto> Steps,
    string? WorkspaceCode = null,
    int PendingApprovalCount = 0,
    string? LastFailureReason = null,
    bool CanRetry = false);

internal static class AgentTaskDtoMapper
{
    public static AgentTaskDto Map(
        AgentTask task,
        string? workspaceCode = null,
        int pendingApprovalCount = 0)
    {
        return new AgentTaskDto(
            task.Id,
            task.TaskCode,
            task.SessionId,
            task.Title,
            task.Goal,
            task.TaskType.ToString(),
            task.Status.ToString(),
            task.RiskLevel.ToString(),
            task.ModelId?.Value,
            task.WorkspaceId?.Value,
            task.PlanJson,
            task.FinalSummary,
            task.CreatedAt,
            task.UpdatedAt,
            task.CompletedAt,
            task.Steps
                .OrderBy(step => step.StepIndex)
                .Select(MapStep)
                .ToArray(),
            workspaceCode,
            pendingApprovalCount,
            ResolveLastFailureReason(task),
            task.Status is AgentTaskStatus.Approved
                or AgentTaskStatus.Running
                or AgentTaskStatus.GeneratingArtifacts
                or AgentTaskStatus.WaitingToolApproval);
    }

    private static AgentStepDto MapStep(AgentStep step)
    {
        return new AgentStepDto(
            step.Id,
            step.StepIndex,
            step.Title,
            step.Description,
            step.StepType.ToString(),
            step.Status.ToString(),
            step.ToolCode,
            step.RequiresApproval,
            step.ErrorMessage);
    }

    private static string? ResolveLastFailureReason(AgentTask task)
    {
        if (task.Status is AgentTaskStatus.Failed or AgentTaskStatus.Rejected)
        {
            return task.FinalSummary;
        }

        return task.Steps
            .OrderByDescending(step => step.FinishedAt)
            .FirstOrDefault(step => step.Status == AgentStepStatus.Failed)
            ?.ErrorMessage;
    }
}
