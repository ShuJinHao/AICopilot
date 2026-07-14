using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed record AgentApprovalRequestDto(
    Guid Id,
    Guid TaskId,
    string? WorkspaceCode,
    string Type,
    string TargetId,
    string TargetName,
    string RiskLevel,
    string Status,
    string? Reason,
    DateTimeOffset RequestedAt,
    DateTimeOffset? DecidedAt,
    Guid? DecidedBy);

public sealed record AgentApprovalDecisionRequest(string? Comment = null);

[AuthorizeRequirement("AiGateway.GetAgentTask")]
public sealed record GetPendingAgentApprovalsQuery : IQuery<Result<IReadOnlyCollection<AgentApprovalRequestDto>>>;

[AuthorizeRequirement("AiGateway.GetAgentTask")]
public sealed record GetAgentTaskApprovalsQuery(Guid TaskId) : IQuery<Result<IReadOnlyCollection<AgentApprovalRequestDto>>>;

[ResourceAuthorizationOwner(typeof(AgentApprovalDecisionCoordinator))]
public sealed record ApproveAgentApprovalCommand(Guid Id, string? Comment = null) : ICommand<Result<AgentApprovalRequestDto>>;

[ResourceAuthorizationOwner(typeof(AgentApprovalDecisionCoordinator))]
public sealed record RejectAgentApprovalCommand(Guid Id, string? Comment = null) : ICommand<Result<AgentApprovalRequestDto>>;
