using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.CloudReadiness;

public sealed class GetCloudReadonlyReadinessQueryHandler(
    CloudReadonlyReadinessService readinessService)
    : IQueryHandler<GetCloudReadonlyReadinessQuery, Result<CloudReadonlyReadinessDto>>
{
    public Task<Result<CloudReadonlyReadinessDto>> Handle(
        GetCloudReadonlyReadinessQuery request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success(readinessService.BuildCurrent()));
    }
}

public sealed class GetCloudReadonlyReadinessHistoryQueryHandler(
    ICloudReadonlyReadinessHistoryStore historyStore)
    : IQueryHandler<GetCloudReadonlyReadinessHistoryQuery, Result<IReadOnlyCollection<CloudReadonlyReadinessDto>>>
{
    public Task<Result<IReadOnlyCollection<CloudReadonlyReadinessDto>>> Handle(
        GetCloudReadonlyReadinessHistoryQuery request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success(historyStore.List()));
    }
}

public sealed class GetCloudReadonlySandboxStatusQueryHandler(
    CloudReadonlyReadinessService readinessService,
    ICloudReadonlyReadinessHistoryStore historyStore)
    : IQueryHandler<GetCloudReadonlySandboxStatusQuery, Result<CloudReadonlySandboxStatusDto>>
{
    public Task<Result<CloudReadonlySandboxStatusDto>> Handle(
        GetCloudReadonlySandboxStatusQuery request,
        CancellationToken cancellationToken)
    {
        var latestSmoke = historyStore.List()
            .FirstOrDefault(report => report.Mode == CloudReadonlyReadinessModes.RealSandboxSmoke);
        return Task.FromResult(Result.Success(readinessService.BuildSandboxStatus(latestSmoke)));
    }
}

public sealed class GetCloudReadonlySandboxSmokeHistoryQueryHandler(
    CloudReadonlyReadinessService readinessService,
    ICloudReadonlyReadinessHistoryStore historyStore)
    : IQueryHandler<GetCloudReadonlySandboxSmokeHistoryQuery, Result<IReadOnlyCollection<CloudReadonlySandboxStatusDto>>>
{
    public Task<Result<IReadOnlyCollection<CloudReadonlySandboxStatusDto>>> Handle(
        GetCloudReadonlySandboxSmokeHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var history = historyStore.List()
            .Where(report => report.Mode == CloudReadonlyReadinessModes.RealSandboxSmoke)
            .Select(readinessService.BuildSandboxStatus)
            .ToArray();
        IReadOnlyCollection<CloudReadonlySandboxStatusDto> result = history;
        return Task.FromResult(Result.Success(result));
    }
}

public sealed class RunCloudReadonlyReadinessCheckCommandHandler(
    CloudReadonlyReadinessService readinessService,
    ICloudReadonlyReadinessHistoryStore historyStore)
    : ICommandHandler<RunCloudReadonlyReadinessCheckCommand, Result<CloudReadonlyReadinessDto>>
{
    public async Task<Result<CloudReadonlyReadinessDto>> Handle(
        RunCloudReadonlyReadinessCheckCommand request,
        CancellationToken cancellationToken)
    {
        var normalizedMode = CloudReadonlyReadinessService.NormalizeMode(request.Mode);
        if (normalizedMode is null)
        {
            return Result.Invalid("CloudReadonly readiness mode must be DryRun, FakeEndpoint, or RealSandboxSmoke.");
        }

        var result = await readinessService.RunAsync(
            normalizedMode,
            request.EndpointCodes,
            request.MaxRows,
            request.TimeoutMs,
            cancellationToken);
        historyStore.Save(result);

        return Result.Success(result);
    }
}
