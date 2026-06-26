using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.CloudReadiness;

public sealed class GetCloudReadonlySandboxAgentTrialStatusQueryHandler(
    CloudReadonlySandboxAgentTrialService trialService)
    : IQueryHandler<GetCloudReadonlySandboxAgentTrialStatusQuery, Result<CloudReadonlySandboxAgentTrialStatusDto>>
{
    public Task<Result<CloudReadonlySandboxAgentTrialStatusDto>> Handle(
        GetCloudReadonlySandboxAgentTrialStatusQuery request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success(trialService.BuildStatus()));
    }
}

public sealed class RunCloudReadonlySandboxAgentTrialCommandHandler(
    CloudReadonlySandboxAgentTrialService trialService,
    CloudReadonlySandboxControlledTrialService controlledTrialService,
    ICloudReadonlySandboxAgentTrialHistoryStore historyStore)
    : ICommandHandler<RunCloudReadonlySandboxAgentTrialCommand, Result<CloudReadonlySandboxAgentTrialResultDto>>
{
    public async Task<Result<CloudReadonlySandboxAgentTrialResultDto>> Handle(
        RunCloudReadonlySandboxAgentTrialCommand request,
        CancellationToken cancellationToken)
    {
        var result = string.Equals(
                request.TrialMode,
                CloudReadonlySandboxControlledTrialMarkers.TrialMode,
                StringComparison.OrdinalIgnoreCase)
            ? await controlledTrialService.RunIntentAsync(
                request.IntentId ?? request.ScenarioId,
                request.ArtifactTypes,
                request.MaxRows,
                request.TimeoutMs,
                cancellationToken)
            : await trialService.RunScenarioAsync(
                request.ScenarioId,
                request.ArtifactTypes,
                request.MaxRows,
                request.TimeoutMs,
                cancellationToken);
        if (result.IsSuccess && result.Value is not null)
        {
            historyStore.Save(result.Value);
        }

        return result;
    }
}
