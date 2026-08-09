using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace AICopilot.AiGatewayService.Runtime;

[AuthorizeRequirement("AiGateway.GetCloudReadonlyStatus")]
public sealed record GetCloudReadonlyStatusQuery : IQuery<Result<CloudReadonlyStatusDto>>;

public sealed class GetCloudReadonlyStatusQueryHandler(
    IOptions<CloudReadonlyOptions> cloudReadonlyOptions,
    IOptions<CloudAiReadOptions> cloudAiReadOptions,
    ICurrentUser currentUser,
    ICloudDelegationGrantStore delegationGrantStore,
    TimeProvider timeProvider)
    : IQueryHandler<GetCloudReadonlyStatusQuery, Result<CloudReadonlyStatusDto>>
{
    public async Task<Result<CloudReadonlyStatusDto>> Handle(
        GetCloudReadonlyStatusQuery request,
        CancellationToken cancellationToken)
    {
        var delegationAvailable = await HasCurrentDelegationAsync(cancellationToken);
        return Result.Success(CloudReadonlyStatusEvaluator.Evaluate(
            cloudReadonlyOptions.Value,
            cloudAiReadOptions.Value,
            delegationAvailable));
    }

    private async Task<bool> HasCurrentDelegationAsync(CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated ||
            !currentUser.Id.HasValue ||
            !Guid.TryParse(currentUser.CloudDelegationId, out var grantId) ||
            grantId == Guid.Empty)
        {
            return false;
        }

        try
        {
            var grant = await delegationGrantStore.ResolveAsync(
                grantId,
                currentUser.Id.Value,
                timeProvider.GetUtcNow().UtcDateTime,
                cancellationToken);
            return grant is { AccessToken.Length: > 0 };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }
}

public static class CloudReadonlyStatusEvaluator
{
    public static CloudReadonlyStatusDto Evaluate(
        CloudReadonlyOptions readonlyOptions,
        CloudAiReadOptions aiReadOptions,
        bool delegationAvailable)
    {
        var baseUrlConfigured = IsHttpBaseUrlConfigured(aiReadOptions.BaseUrl);
        var transportConfigured = aiReadOptions.Enabled;
        var productionReadAllowed = readonlyOptions.Real.AllowProductionRead;
        var mode = readonlyOptions.Mode.ToString();

        return readonlyOptions.Mode switch
        {
            CloudReadonlyDataSourceMode.Simulation when readonlyOptions.Simulation.Enabled =>
                new CloudReadonlyStatusDto(
                    mode,
                    CloudReadonlyRuntimeStatuses.Simulation,
                    baseUrlConfigured,
                    transportConfigured,
                    delegationAvailable,
                    productionReadAllowed,
                    "当前使用 Cloud 只读模拟数据，仅用于演示分析链路。"),

            CloudReadonlyDataSourceMode.Real when !readonlyOptions.Real.Enabled || !aiReadOptions.Enabled || !productionReadAllowed =>
                new CloudReadonlyStatusDto(
                    mode,
                    CloudReadonlyRuntimeStatuses.RealNotAllowed,
                    baseUrlConfigured,
                    transportConfigured,
                    delegationAvailable,
                    productionReadAllowed,
                    "Cloud 正式只读模式未完全放行，需要同时启用 CloudReadonly:Real、AllowProductionRead 和 CloudAiRead。"),

            CloudReadonlyDataSourceMode.Real when !baseUrlConfigured =>
                new CloudReadonlyStatusDto(
                    mode,
                    CloudReadonlyRuntimeStatuses.RealMissingBaseUrl,
                    baseUrlConfigured,
                    transportConfigured,
                    delegationAvailable,
                    productionReadAllowed,
                    "Cloud AiRead 已选择正式模式，但缺少有效 BaseUrl。"),

            CloudReadonlyDataSourceMode.Real when !delegationAvailable =>
                new CloudReadonlyStatusDto(
                    mode,
                    CloudReadonlyRuntimeStatuses.RealMissingDelegation,
                    baseUrlConfigured,
                    transportConfigured,
                    delegationAvailable,
                    productionReadAllowed,
                    "Cloud AiRead 传输已配置，但当前用户缺少有效委托；请重新通过 Cloud 登录。"),

            CloudReadonlyDataSourceMode.Real =>
                new CloudReadonlyStatusDto(
                    mode,
                    CloudReadonlyRuntimeStatuses.RealReady,
                    baseUrlConfigured,
                    transportConfigured,
                    delegationAvailable,
                    productionReadAllowed,
                    "Cloud AiRead 正式只读传输和当前用户委托均有效，可读取授权范围内的数据。"),

            _ => new CloudReadonlyStatusDto(
                mode,
                CloudReadonlyRuntimeStatuses.Disabled,
                baseUrlConfigured,
                transportConfigured,
                delegationAvailable,
                productionReadAllowed,
                "Cloud 只读数据源未启用。可启用 Simulation 演示模式，或配置 Real + Cloud AiRead 读取正式数据。")
        };
    }

    private static bool IsHttpBaseUrlConfigured(string? baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme is "http" or "https";
    }
}
