using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace AICopilot.AiGatewayService.Runtime;

[AuthorizeRequirement("AiGateway.GetRuntimeSettings")]
public sealed record GetCloudReadonlyStatusQuery : IQuery<Result<CloudReadonlyStatusDto>>;

public sealed class GetCloudReadonlyStatusQueryHandler(
    IOptions<CloudReadonlyOptions> cloudReadonlyOptions,
    IOptions<CloudAiReadOptions> cloudAiReadOptions)
    : IQueryHandler<GetCloudReadonlyStatusQuery, Result<CloudReadonlyStatusDto>>
{
    public Task<Result<CloudReadonlyStatusDto>> Handle(
        GetCloudReadonlyStatusQuery request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success(CloudReadonlyStatusEvaluator.Evaluate(
            cloudReadonlyOptions.Value,
            cloudAiReadOptions.Value)));
    }
}

public static class CloudReadonlyStatusEvaluator
{
    public static CloudReadonlyStatusDto Evaluate(
        CloudReadonlyOptions readonlyOptions,
        CloudAiReadOptions aiReadOptions)
    {
        var baseUrlConfigured = IsHttpBaseUrlConfigured(aiReadOptions.BaseUrl);
        var tokenConfigured = !string.IsNullOrWhiteSpace(aiReadOptions.ServiceAccountToken);
        var productionReadAllowed = readonlyOptions.Real.AllowProductionRead;
        var mode = readonlyOptions.Mode.ToString();

        return readonlyOptions.Mode switch
        {
            CloudReadonlyDataSourceMode.Simulation when readonlyOptions.Simulation.Enabled =>
                new CloudReadonlyStatusDto(
                    mode,
                    CloudReadonlyRuntimeStatuses.Simulation,
                    baseUrlConfigured,
                    tokenConfigured,
                    productionReadAllowed,
                    "当前使用 Cloud 只读模拟数据，仅用于演示分析链路。"),

            CloudReadonlyDataSourceMode.Real when !readonlyOptions.Real.Enabled || !aiReadOptions.Enabled || !productionReadAllowed =>
                new CloudReadonlyStatusDto(
                    mode,
                    CloudReadonlyRuntimeStatuses.RealNotAllowed,
                    baseUrlConfigured,
                    tokenConfigured,
                    productionReadAllowed,
                    "Cloud 正式只读模式未完全放行，需要同时启用 CloudReadonly:Real、AllowProductionRead 和 CloudAiRead。"),

            CloudReadonlyDataSourceMode.Real when !baseUrlConfigured =>
                new CloudReadonlyStatusDto(
                    mode,
                    CloudReadonlyRuntimeStatuses.RealMissingBaseUrl,
                    baseUrlConfigured,
                    tokenConfigured,
                    productionReadAllowed,
                    "Cloud AiRead 已选择正式模式，但缺少有效 BaseUrl。"),

            CloudReadonlyDataSourceMode.Real when !tokenConfigured =>
                new CloudReadonlyStatusDto(
                    mode,
                    CloudReadonlyRuntimeStatuses.RealMissingToken,
                    baseUrlConfigured,
                    tokenConfigured,
                    productionReadAllowed,
                    "Cloud AiRead 已选择正式模式，但缺少 ServiceAccountToken。"),

            CloudReadonlyDataSourceMode.Real =>
                new CloudReadonlyStatusDto(
                    mode,
                    CloudReadonlyRuntimeStatuses.RealReady,
                    baseUrlConfigured,
                    tokenConfigured,
                    productionReadAllowed,
                    "Cloud AiRead 正式只读数据源已配置，可读取和分析数据，但不会写入或修改 Cloud。"),

            _ => new CloudReadonlyStatusDto(
                mode,
                CloudReadonlyRuntimeStatuses.Disabled,
                baseUrlConfigured,
                tokenConfigured,
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
