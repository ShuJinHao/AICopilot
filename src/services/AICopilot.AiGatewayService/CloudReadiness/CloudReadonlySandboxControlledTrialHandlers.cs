using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;
using MediatR;

namespace AICopilot.AiGatewayService.CloudReadiness;

public sealed class GetCloudReadonlySandboxControlledTrialStatusQueryHandler(
    CloudReadonlySandboxControlledTrialService controlledTrialService)
    : IQueryHandler<GetCloudReadonlySandboxControlledTrialStatusQuery, Result<CloudReadonlySandboxControlledTrialStatusDto>>
{
    public Task<Result<CloudReadonlySandboxControlledTrialStatusDto>> Handle(
        GetCloudReadonlySandboxControlledTrialStatusQuery request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success(controlledTrialService.BuildStatus()));
    }
}

public sealed class CreateCloudReadonlySandboxControlledPlanCommandHandler(
    CloudReadonlySandboxControlledTrialService controlledTrialService,
    ISender sender)
    : ICommandHandler<CreateCloudReadonlySandboxControlledPlanCommand, Result<CloudReadonlySandboxControlledPlanDto>>
{
    public async Task<Result<CloudReadonlySandboxControlledPlanDto>> Handle(
        CreateCloudReadonlySandboxControlledPlanCommand request,
        CancellationToken cancellationToken)
    {
        var intentResult = controlledTrialService.CreateIntent(
            request.Goal,
            request.ArtifactTypes,
            request.TimeRange,
            request.MaxRows);
        if (!intentResult.IsSuccess || intentResult.Value is null)
        {
            return Result.From(intentResult);
        }

        var taskResult = await sender.Send(
            new PlanAgentTaskCommand(
                request.SessionId,
                request.Goal,
                AgentTaskType.CloudDataReport,
                request.ModelId,
                ArtifactTypes: intentResult.Value.ArtifactTypes,
                BusinessDomains: intentResult.Value.EndpointCodes,
                QueryMode: CloudReadonlySandboxAgentTrialMarkers.SourceMode,
                RequiresDataApproval: true,
                PlannerMode: request.PlannerMode ?? "Auto",
                IsCloudSandboxControlledTrial: true,
                CloudSandboxGoalIntent: intentResult.Value),
            cancellationToken);
        if (!taskResult.IsSuccess || taskResult.Value is null)
        {
            return Result.From(taskResult);
        }

        return Result.Success(new CloudReadonlySandboxControlledPlanDto(taskResult.Value, intentResult.Value));
    }
}
