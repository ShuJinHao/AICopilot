using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Specifications.Artifacts;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed record AgentTaskAuditSummaryDto(
    Guid Id,
    Guid TaskId,
    string? WorkspaceCode,
    string ActionCode,
    string TargetType,
    string TargetName,
    string Result,
    string Summary,
    DateTime CreatedAt,
    IReadOnlyDictionary<string, string> Metadata);

[AuthorizeRequirement("AiGateway.GetAgentTask")]
public sealed record GetAgentTaskAuditSummaryQuery(Guid Id)
    : IQuery<Result<IReadOnlyCollection<AgentTaskAuditSummaryDto>>>;

public sealed class GetAgentTaskAuditSummaryQueryHandler(
    IRepository<AgentTask> taskRepository,
    IReadRepository<ArtifactWorkspace> workspaceRepository,
    IAuditLogQueryService auditLogQueryService,
    ICurrentUser currentUser)
    : IQueryHandler<GetAgentTaskAuditSummaryQuery, Result<IReadOnlyCollection<AgentTaskAuditSummaryDto>>>
{
    private const int MaxSummaryItems = 200;

    public async Task<Result<IReadOnlyCollection<AgentTaskAuditSummaryDto>>> Handle(
        GetAgentTaskAuditSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var taskResult = await AgentTaskCommandLoader.LoadTaskAsync(
            taskRepository,
            currentUser,
            request.Id,
            cancellationToken);
        if (!taskResult.IsSuccess)
        {
            return Result.From(taskResult);
        }

        var task = taskResult.Value!;
        var workspace = await LoadWorkspaceAsync(task, cancellationToken);
        var taskId = task.Id.Value.ToString();
        var workspaceCode = workspace?.WorkspaceCode;
        var auditLogs = await auditLogQueryService.GetListAsync(
            page: 1,
            pageSize: MaxSummaryItems,
            actionGroup: AuditActionGroups.AiGateway,
            actionCode: null,
            targetType: null,
            targetName: null,
            operatorUserName: null,
            result: null,
            from: null,
            to: null,
            cancellationToken);

        var items = auditLogs.Items
            .Where(item => BelongsToTask(item, taskId, workspaceCode))
            .Select(item => new AgentTaskAuditSummaryDto(
                item.Id,
                task.Id,
                ResolveWorkspaceCode(item.Metadata, workspaceCode),
                item.ActionCode,
                item.TargetType,
                item.TargetName ?? string.Empty,
                item.Result,
                item.Summary,
                item.CreatedAt,
                item.Metadata))
            .ToArray();

        return Result.Success<IReadOnlyCollection<AgentTaskAuditSummaryDto>>(items);
    }

    private async Task<ArtifactWorkspace?> LoadWorkspaceAsync(AgentTask task, CancellationToken cancellationToken)
    {
        if (task.WorkspaceId is null)
        {
            return null;
        }

        return await workspaceRepository.FirstOrDefaultAsync(
            new ArtifactWorkspaceByIdSpec(task.WorkspaceId.Value, includeArtifacts: false),
            cancellationToken);
    }

    private static bool BelongsToTask(
        AuditLogSummaryDto item,
        string taskId,
        string? workspaceCode)
    {
        if (string.Equals(item.TargetId, taskId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (item.Metadata.TryGetValue("taskId", out var metadataTaskId) &&
            string.Equals(metadataTaskId, taskId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(workspaceCode) &&
               item.Metadata.TryGetValue("workspaceCode", out var metadataWorkspaceCode) &&
               string.Equals(metadataWorkspaceCode, workspaceCode, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveWorkspaceCode(
        IReadOnlyDictionary<string, string> metadata,
        string? fallback)
    {
        return metadata.TryGetValue("workspaceCode", out var workspaceCode) &&
               !string.IsNullOrWhiteSpace(workspaceCode)
            ? workspaceCode
            : fallback;
    }
}
