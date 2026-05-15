using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.AgentTasks;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

[AuthorizeRequirement("AiGateway.GetAgentTask")]
public sealed record GetAgentTaskQuery(Guid Id) : IQuery<Result<AgentTaskDto>>;

[AuthorizeRequirement("AiGateway.GetAgentTask")]
public sealed record GetListAgentTasksBySessionQuery(Guid SessionId) : IQuery<Result<IReadOnlyCollection<AgentTaskDto>>>;

public sealed class GetAgentTaskQueryHandler(
    IRepository<AgentTask> repository,
    IReadRepository<ArtifactWorkspace> workspaceRepository,
    IReadRepository<ApprovalRequest> approvalRepository,
    ICurrentUser currentUser)
    : IQueryHandler<GetAgentTaskQuery, Result<AgentTaskDto>>
{
    public async Task<Result<AgentTaskDto>> Handle(GetAgentTaskQuery request, CancellationToken cancellationToken)
    {
        var taskResult = await AgentTaskCommandLoader.LoadTaskAsync(repository, currentUser, request.Id, cancellationToken);
        if (!taskResult.IsSuccess)
        {
            return Result.From(taskResult);
        }

        return Result.Success(await AgentTaskDtoComposer.MapAsync(
            taskResult.Value!,
            workspaceRepository,
            approvalRepository,
            cancellationToken));
    }
}

public sealed class GetListAgentTasksBySessionQueryHandler(
    IReadRepository<AgentTask> repository,
    IReadRepository<ArtifactWorkspace> workspaceRepository,
    IReadRepository<ApprovalRequest> approvalRepository,
    ICurrentUser currentUser)
    : IQueryHandler<GetListAgentTasksBySessionQuery, Result<IReadOnlyCollection<AgentTaskDto>>>
{
    public async Task<Result<IReadOnlyCollection<AgentTaskDto>>> Handle(
        GetListAgentTasksBySessionQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } userId)
        {
            return Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.Unauthorized,
                "Current user id is missing or invalid."));
        }

        if (request.SessionId == Guid.Empty)
        {
            return Result.Invalid("SessionId is required.");
        }

        var tasks = await repository.ListAsync(
            new AgentTasksBySessionForUserSpec(new SessionId(request.SessionId), userId, includeSteps: true),
            cancellationToken);
        var dtos = new List<AgentTaskDto>();
        foreach (var task in tasks)
        {
            dtos.Add(await AgentTaskDtoComposer.MapAsync(
                task,
                workspaceRepository,
                approvalRepository,
                cancellationToken));
        }

        return Result.Success<IReadOnlyCollection<AgentTaskDto>>(
            dtos.ToArray());
    }
}
