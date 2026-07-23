using AICopilot.AiGatewayService.Models;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;
using MediatR;

namespace AICopilot.AiGatewayService.AgentTasks;

[AuthorizeRequirement("AiGateway.PlanAgentTask")]
public sealed record PlanAgentTaskCommand(
    Guid SessionId,
    string Goal,
    AgentTaskType TaskType,
    Guid? ModelId,
    IReadOnlyCollection<Guid>? UploadIds = null,
    IReadOnlyCollection<Guid>? KnowledgeBaseIds = null,
    IReadOnlyCollection<Guid>? DataSourceIds = null,
    IReadOnlyCollection<string>? BusinessDomains = null,
    string? QueryMode = null,
    bool RequiresDataApproval = false,
    IReadOnlyCollection<string>? ArtifactTargets = null,
    AgentPluginSelectionMode? PluginSelectionMode = null,
    IReadOnlyCollection<Guid>? SelectedPluginIds = null,
    AgentCapabilitySelectionMode? CapabilitySelectionMode = null,
    IReadOnlyCollection<string>? RequestedCapabilityCodes = null)
    : ICommand<Result<AgentTaskDto>>, IStreamRequest<ChatChunk>;

[AuthorizeRequirement("AiGateway.ApproveAgentTaskPlan")]
public sealed record ApproveAgentTaskPlanCommand(Guid Id) : ICommand<Result<AgentTaskDto>>;

[AuthorizeRequirement("AiGateway.ApproveAgentTaskPlan")]
public sealed record RejectAgentTaskPlanCommand(Guid Id, string? Reason = null) : ICommand<Result<AgentTaskDto>>;

[AuthorizeRequirement("AiGateway.RunAgentTask")]
public sealed record RunAgentTaskCommand(Guid Id) : ICommand<Result<AgentTaskDto>>;

[AuthorizeRequirement("AiGateway.RunAgentTask")]
public sealed record RetryAgentTaskCommand(Guid Id) : ICommand<Result<AgentTaskDto>>;

[AuthorizeRequirement("AiGateway.CancelAgentTask")]
public sealed record CancelAgentTaskCommand(Guid Id) : ICommand<Result<AgentTaskDto>>;

internal sealed class PlanAgentTaskCommandHandler(
    PlanAgentTaskCoordinator planCoordinator)
    : ICommandHandler<PlanAgentTaskCommand, Result<AgentTaskDto>>
{
    public Task<Result<AgentTaskDto>> Handle(
        PlanAgentTaskCommand request,
        CancellationToken cancellationToken) =>
        planCoordinator.PlanAsync(request, cancellationToken);
}
