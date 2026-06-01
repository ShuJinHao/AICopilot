using System.Text.Json;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AICopilot.Infrastructure.CloudRead;

public sealed class CloudAiReadClient(
    HttpClient httpClient,
    IOptions<CloudAiReadOptions> options,
    ILogger<CloudAiReadClient> logger) : ICloudAiReadClient
{
    private const string DevicesPath = "/api/v1/ai/read/devices";
    private const string CapacitySummaryPath = "/api/v1/ai/read/capacity/summary";
    private const string DeviceLogsPath = "/api/v1/ai/read/device-logs";

    private readonly CloudAiReadHttpTransport httpTransport = new(httpClient, logger);

    public bool IsEnabled => options.Value.Enabled;

    public async Task<JsonDocument> SendJsonAsync(
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, string?>? query = null,
        CancellationToken cancellationToken = default)
    {
        var configuredOptions = EnsureConfigured();
        var decision = CloudAiReadEndpointPolicy.Evaluate(
            method,
            path,
            configuredOptions.ExplicitPostQueryPaths);
        if (!decision.IsAllowed)
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.RequestBlocked,
                decision.Reason ?? "Cloud AiRead request was blocked by the allowlist policy.");
        }

        return await httpTransport.SendJsonAsync(
            method,
            path,
            query,
            configuredOptions,
            cancellationToken);
    }

    public async Task<CloudAiReadResult<CloudAiReadDeviceDto>> GetDevicesAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default)
    {
        using var document = await SendJsonAsync(
            HttpMethod.Get,
            DevicesPath,
            CloudAiReadQueryParameterBuilder.BuildDeviceQueryParameters(query),
            cancellationToken);

        return CloudAiReadDocumentAdapter.MapDevices(document.RootElement, DevicesPath, query.Limit);
    }

    public async Task<CloudAiReadResult<CloudAiReadCapacitySummaryDto>> GetCapacitySummaryAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default)
    {
        using var document = await SendJsonAsync(
            HttpMethod.Get,
            CapacitySummaryPath,
            CloudAiReadQueryParameterBuilder.BuildCapacityQueryParameters(query),
            cancellationToken);

        return CloudAiReadDocumentAdapter.MapCapacitySummary(document.RootElement, CapacitySummaryPath, query.Limit);
    }

    public async Task<CloudAiReadResult<CloudAiReadDeviceLogDto>> GetDeviceLogsAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default)
    {
        using var document = await SendJsonAsync(
            HttpMethod.Get,
            DeviceLogsPath,
            CloudAiReadQueryParameterBuilder.BuildDeviceLogQueryParameters(query),
            cancellationToken);

        return CloudAiReadDocumentAdapter.MapDeviceLogs(document.RootElement, DeviceLogsPath, query.Limit);
    }

    public async Task<CloudAiReadResult<CloudAiReadPassStationRecordDto>> GetPassStationRecordsAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default)
    {
        var typeKey = string.IsNullOrWhiteSpace(query.PassStationTypeKey)
            ? options.Value.DefaultPassStationTypeKey
            : query.PassStationTypeKey.Trim();
        var path = $"/api/v1/ai/read/pass-stations/{Uri.EscapeDataString(typeKey)}";
        using var document = await SendJsonAsync(
            HttpMethod.Get,
            path,
            CloudAiReadQueryParameterBuilder.BuildPassStationQueryParameters(query),
            cancellationToken);

        return CloudAiReadDocumentAdapter.MapPassStationRecords(document.RootElement, path, query.Limit);
    }

    public async Task<CloudAiReadResult<object>> QuerySemanticAsync(
        SemanticQueryPlan plan,
        CancellationToken cancellationToken = default)
    {
        var query = CloudAiReadQuery.FromSemanticPlan(plan, options.Value.DefaultPassStationTypeKey);
        return plan.Target switch
        {
            SemanticQueryTarget.Device => ToUntyped(await GetDevicesAsync(query, cancellationToken)),
            SemanticQueryTarget.Capacity => ToUntyped(await GetCapacitySummaryAsync(query, cancellationToken)),
            SemanticQueryTarget.DeviceLog => ToUntyped(await GetDeviceLogsAsync(query, cancellationToken)),
            SemanticQueryTarget.ProductionData => ToUntyped(await GetPassStationRecordsAsync(query, cancellationToken)),
            _ => throw new NotSupportedException($"Cloud AiRead does not support semantic target '{plan.Target}'.")
        };
    }

    private static CloudAiReadResult<object> ToUntyped<T>(CloudAiReadResult<T> result)
    {
        return new CloudAiReadResult<object>(
            result.SourcePath,
            result.SourceLabel,
            result.QueriedAtUtc,
            result.Limit,
            result.IsTruncated,
            result.Items.Cast<object>().ToArray(),
            result.Rows);
    }

    private CloudAiReadOptions EnsureConfigured()
    {
        var configuredOptions = options.Value;
        if (!configuredOptions.Enabled)
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.NotConfigured,
                "Cloud AiRead is not enabled.");
        }

        configuredOptions.EnsureValid();
        return configuredOptions;
    }
}
