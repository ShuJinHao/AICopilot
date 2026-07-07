using AICopilot.AiGatewayService.Tools;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
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

[AuthorizeRequirement("AiGateway.GetAgentTask")]
public sealed record GetAgentTaskToolExecutionsQuery(
    Guid Id,
    int PageIndex = 1,
    int PageSize = 20,
    string? Status = null,
    string? ToolCode = null) : IQuery<Result<ToolExecutionRecordPageDto>>;

[AuthorizeRequirement("AiGateway.GetAgentTask")]
public sealed record GetAgentTaskRunAttemptsQuery(
    Guid Id,
    int PageIndex = 1,
    int PageSize = 20) : IQuery<Result<AgentTaskRunAttemptPageDto>>;

[AuthorizeRequirement("AiGateway.GetAgentTask")]
public sealed record GetAgentTaskRunQueueQuery(
    Guid Id,
    int PageIndex = 1,
    int PageSize = 20) : IQuery<Result<AgentTaskRunQueuePageDto>>;

public sealed class GetAgentTaskAuditSummaryQueryHandler(
    AgentTaskAuditQueryCoordinator auditQueryCoordinator)
    : IQueryHandler<GetAgentTaskAuditSummaryQuery, Result<IReadOnlyCollection<AgentTaskAuditSummaryDto>>>
{
    public Task<Result<IReadOnlyCollection<AgentTaskAuditSummaryDto>>> Handle(
        GetAgentTaskAuditSummaryQuery request,
        CancellationToken cancellationToken) =>
        auditQueryCoordinator.GetSummaryAsync(request, cancellationToken);
}

public sealed class GetAgentTaskToolExecutionsQueryHandler(
    AgentTaskToolExecutionQueryCoordinator toolExecutionQueryCoordinator)
    : IQueryHandler<GetAgentTaskToolExecutionsQuery, Result<ToolExecutionRecordPageDto>>
{
    public Task<Result<ToolExecutionRecordPageDto>> Handle(
        GetAgentTaskToolExecutionsQuery request,
        CancellationToken cancellationToken) =>
        toolExecutionQueryCoordinator.GetAsync(request, cancellationToken);
}

public sealed class GetAgentTaskRunAttemptsQueryHandler(
    AgentTaskAuditQueryCoordinator auditQueryCoordinator)
    : IQueryHandler<GetAgentTaskRunAttemptsQuery, Result<AgentTaskRunAttemptPageDto>>
{
    public Task<Result<AgentTaskRunAttemptPageDto>> Handle(
        GetAgentTaskRunAttemptsQuery request,
        CancellationToken cancellationToken) =>
        auditQueryCoordinator.GetRunAttemptsAsync(request, cancellationToken);
}

public sealed class GetAgentTaskRunQueueQueryHandler(
    AgentTaskAuditQueryCoordinator auditQueryCoordinator)
    : IQueryHandler<GetAgentTaskRunQueueQuery, Result<AgentTaskRunQueuePageDto>>
{
    public Task<Result<AgentTaskRunQueuePageDto>> Handle(
        GetAgentTaskRunQueueQuery request,
        CancellationToken cancellationToken) =>
        auditQueryCoordinator.GetRunQueueAsync(request, cancellationToken);
}
