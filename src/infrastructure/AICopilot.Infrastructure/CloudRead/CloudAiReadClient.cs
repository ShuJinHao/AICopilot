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
    private const string ProcessesPath = "/api/v1/ai/read/processes";
    private const string ClientReleasesPath = "/api/v1/ai/read/client-releases";
    private const string DeviceClientStatesPath = "/api/v1/ai/read/device-client-states";
    private const string CapacitySummaryPath = "/api/v1/ai/read/capacity/summary";
    private const string CapacityHourlyPath = "/api/v1/ai/read/capacity/hourly";
    private const string DeviceLogsPath = "/api/v1/ai/read/device-logs";
    private const string ProductionRecordsPath = "/api/v1/ai/read/production-records";

    private readonly CloudAiReadHttpTransport httpTransport = new(httpClient, logger);

    public bool IsEnabled => options.Value.Enabled;

    private async Task<JsonDocument> GetJsonAsync(
        string path,
        IReadOnlyDictionary<string, string?>? query = null,
        CancellationToken cancellationToken = default)
    {
        var configuredOptions = EnsureConfigured();
        var decision = CloudAiReadEndpointPolicy.Evaluate(HttpMethod.Get, path);
        if (!decision.IsAllowed)
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.RequestBlocked,
                decision.Reason ?? "Cloud AiRead request was blocked by the allowlist policy.");
        }

        return await httpTransport.GetJsonAsync(
            path,
            query,
            configuredOptions,
            cancellationToken);
    }

    public async Task<CloudAiReadResult<CloudAiReadDeviceDto>> GetDevicesAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default)
    {
        using var document = await GetJsonAsync(
            DevicesPath,
            CloudAiReadQueryParameterBuilder.BuildDeviceQueryParameters(query),
            cancellationToken);

        return CloudAiReadDocumentAdapter.MapDevices(document.RootElement, DevicesPath, query.Limit);
    }

    public async Task<CloudAiReadResult<CloudAiReadProcessDto>> GetProcessesAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default)
    {
        using var document = await GetJsonAsync(
            ProcessesPath,
            CloudAiReadQueryParameterBuilder.BuildProcessQueryParameters(query),
            cancellationToken);

        return CloudAiReadDocumentAdapter.MapProcesses(document.RootElement, ProcessesPath, query.Limit);
    }

    public async Task<CloudAiReadResult<CloudAiReadClientReleaseVersionDto>> GetClientReleasesAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default)
    {
        using var document = await GetJsonAsync(
            ClientReleasesPath,
            CloudAiReadQueryParameterBuilder.BuildClientReleaseQueryParameters(query),
            cancellationToken);

        return CloudAiReadDocumentAdapter.MapClientReleases(document.RootElement, ClientReleasesPath, query.Limit);
    }

    public async Task<CloudAiReadResult<CloudAiReadDeviceClientStateDto>> GetDeviceClientStatesAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default)
    {
        using var document = await GetJsonAsync(
            DeviceClientStatesPath,
            CloudAiReadQueryParameterBuilder.BuildDeviceClientStateQueryParameters(query),
            cancellationToken);

        return CloudAiReadDocumentAdapter.MapDeviceClientStates(document.RootElement, DeviceClientStatesPath, query.Limit);
    }

    public async Task<CloudAiReadResult<CloudAiReadCapacitySummaryDto>> GetCapacitySummaryAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default)
    {
        using var document = await GetJsonAsync(
            CapacitySummaryPath,
            CloudAiReadQueryParameterBuilder.BuildCapacityQueryParameters(query),
            cancellationToken);

        return CloudAiReadDocumentAdapter.MapCapacitySummary(document.RootElement, CapacitySummaryPath, query.Limit);
    }

    public async Task<CloudAiReadResult<CloudAiReadCapacityHourlyDto>> GetCapacityHourlyAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default)
    {
        using var document = await GetJsonAsync(
            CapacityHourlyPath,
            CloudAiReadQueryParameterBuilder.BuildCapacityHourlyQueryParameters(query),
            cancellationToken);

        return CloudAiReadDocumentAdapter.MapCapacityHourly(document.RootElement, CapacityHourlyPath, query.Limit);
    }

    public async Task<CloudAiReadResult<CloudAiReadDeviceLogDto>> GetDeviceLogsAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default)
    {
        using var document = await GetJsonAsync(
            DeviceLogsPath,
            CloudAiReadQueryParameterBuilder.BuildDeviceLogQueryParameters(query),
            cancellationToken);

        return CloudAiReadDocumentAdapter.MapDeviceLogs(document.RootElement, DeviceLogsPath, query.Limit);
    }

    public async Task<CloudAiReadResult<CloudAiReadProductionRecordDto>> GetProductionRecordsAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default)
    {
        using var document = await GetJsonAsync(
            ProductionRecordsPath,
            CloudAiReadQueryParameterBuilder.BuildProductionRecordQueryParameters(query),
            cancellationToken);

        return CloudAiReadDocumentAdapter.MapProductionRecords(document.RootElement, ProductionRecordsPath, query.Limit);
    }

    public async Task<CloudAiReadResult<object>> QuerySemanticAsync(
        SemanticQueryPlan plan,
        CancellationToken cancellationToken = default)
    {
        var query = await PrepareSemanticQueryAsync(plan, cancellationToken);
        return plan.Target switch
        {
            SemanticQueryTarget.Device when plan.Kind == SemanticQueryKind.Status =>
                ToUntyped(await GetDeviceClientStatesAsync(query, cancellationToken)),
            SemanticQueryTarget.Device => ToUntyped(await GetDevicesAsync(query, cancellationToken)),
            SemanticQueryTarget.Process => await QueryProcessesAsync(plan, query, cancellationToken),
            SemanticQueryTarget.ClientRelease => ToUntyped(await GetClientReleasesAsync(query, cancellationToken)),
            SemanticQueryTarget.Capacity when ShouldUseCapacityHourly(plan) => ToUntyped(await GetCapacityHourlyAsync(query, cancellationToken)),
            SemanticQueryTarget.Capacity => ToUntyped(await GetCapacitySummaryAsync(query, cancellationToken)),
            SemanticQueryTarget.DeviceLog => ToUntyped(await GetDeviceLogsAsync(query, cancellationToken)),
            SemanticQueryTarget.ProductionData => ToUntyped(await GetProductionRecordsAsync(query, cancellationToken)),
            _ => throw new NotSupportedException($"Cloud AiRead does not support semantic target '{plan.Target}'.")
        };
    }

    private async Task<CloudAiReadResult<object>> QueryProcessesAsync(
        SemanticQueryPlan plan,
        CloudAiReadQuery query,
        CancellationToken cancellationToken)
    {
        if (plan.Kind != SemanticQueryKind.Detail || HasFilter(query, "processId"))
        {
            return ToUntyped(await GetProcessesAsync(query, cancellationToken));
        }

        var searchResult = await GetProcessesAsync(query with { Limit = 100 }, cancellationToken);
        if (searchResult.IsTruncated)
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.MissingRequiredParameter,
                "Cloud AiRead 工序搜索结果已截断，不能据此解析唯一工序；请补充精确工序编码或名称。");
        }

        var exactFilters = plan.Filters
            .Where(filter => filter.Field.Equals("processCode", StringComparison.OrdinalIgnoreCase) ||
                             filter.Field.Equals("processName", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var matches = searchResult.Items
            .Where(item => exactFilters.All(filter =>
                string.Equals(
                    filter.Field.Equals("processCode", StringComparison.OrdinalIgnoreCase)
                        ? item.ProcessCode?.Trim()
                        : item.ProcessName?.Trim(),
                    filter.Value.Trim(),
                    StringComparison.OrdinalIgnoreCase)))
            .GroupBy(
                item => item.ProcessId ?? $"{item.ProcessCode}|{item.ProcessName}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        if (matches.Length != 1)
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.MissingRequiredParameter,
                "Cloud AiRead 工序查询无法唯一命中正式工序，请补充精确工序编码或名称。");
        }

        var match = matches[0];
        return new CloudAiReadResult<object>(
            searchResult.SourcePath,
            searchResult.SourceLabel,
            searchResult.QueriedAtUtc,
            plan.Limit,
            IsTruncated: false,
            [match],
            [new Dictionary<string, object?>
            {
                ["processId"] = match.ProcessId,
                ["processCode"] = match.ProcessCode,
                ["processName"] = match.ProcessName
            }],
            searchResult.ProviderSource,
            searchResult.QueryScope,
            RowCount: 1,
            NextCursor: searchResult.NextCursor);
    }

    private async Task<CloudAiReadQuery> PrepareSemanticQueryAsync(
        SemanticQueryPlan plan,
        CancellationToken cancellationToken)
    {
        var query = ApplySemanticDefaults(plan, CloudAiReadQuery.FromSemanticPlan(plan));
        if (plan.Target == SemanticQueryTarget.Device ||
            plan.Target is SemanticQueryTarget.Recipe or SemanticQueryTarget.Process or SemanticQueryTarget.ClientRelease)
        {
            return query;
        }

        if (HasFilter(query, "deviceId") || !HasFilter(query, "deviceCode"))
        {
            return query;
        }

        var deviceCode = GetFilterValue(query, "deviceCode");
        var deviceResult = await GetDevicesAsync(
            new CloudAiReadQuery(
                null,
                [new CloudAiReadFilter("deviceCode", "eq", deviceCode!)],
                null,
                "deviceCode",
                false,
                100),
            cancellationToken);
        if (deviceResult.IsTruncated)
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.MissingRequiredParameter,
                "Cloud AiRead 设备搜索结果已截断，不能据此解析唯一 deviceId；请补充 Cloud 正式设备 ID。");
        }

        var deviceIds = deviceResult.Items
            .Where(item => string.Equals(
                item.DeviceCode?.Trim(),
                deviceCode?.Trim(),
                StringComparison.OrdinalIgnoreCase))
            .Select(item => item.DeviceId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (deviceIds.Length != 1)
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.MissingRequiredParameter,
                "Cloud AiRead 查询无法通过设备编码唯一解析 deviceId，请补充 Cloud 正式设备 ID。");
        }

        return query.WithFilters(
            query.Filters
                .Where(filter => !filter.Field.Equals("deviceCode", StringComparison.OrdinalIgnoreCase))
                .Append(new CloudAiReadFilter("deviceId", "eq", deviceIds[0]!))
                .ToArray());
    }

    private static CloudAiReadQuery ApplySemanticDefaults(SemanticQueryPlan plan, CloudAiReadQuery query)
    {
        if (plan.Target == SemanticQueryTarget.DeviceLog &&
            plan.Kind == SemanticQueryKind.Latest &&
            query.TimeRange is null &&
            !HasFilter(query, "preset"))
        {
            return AddFilter(query, "preset", "last_24h");
        }

        if (plan.Target == SemanticQueryTarget.ProductionData &&
            plan.Kind == SemanticQueryKind.Latest &&
            query.TimeRange is null &&
            !HasFilter(query, "preset"))
        {
            return AddFilter(query, "preset", "last_24h");
        }

        if (plan.Target == SemanticQueryTarget.Capacity && ShouldUseCapacityHourly(plan))
        {
            if (!HasFilter(query, "date") && query.TimeRange?.Start is { } start && query.TimeRange.End is { } end &&
                start.UtcDateTime.Date == end.UtcDateTime.Date)
            {
                return AddFilter(query, "date", start.UtcDateTime.ToString("yyyy-MM-dd"));
            }

            var preset = InferCapacityHourlyPreset(plan.QueryText);
            if (!string.IsNullOrWhiteSpace(preset) && !HasFilter(query, "preset"))
            {
                return AddFilter(query, "preset", preset);
            }
        }

        return query;
    }

    private static bool ShouldUseCapacityHourly(SemanticQueryPlan plan)
    {
        return plan.Target == SemanticQueryTarget.Capacity &&
               (ContainsTerm(plan.QueryText, "小时") ||
                ContainsTerm(plan.QueryText, "每小时") ||
                ContainsTerm(plan.QueryText, "按小时") ||
                ContainsEnglishTerm(plan.QueryText, "hourly"));
    }

    private static string? InferCapacityHourlyPreset(string? queryText)
    {
        if (ContainsTerm(queryText, "最近24小时") || ContainsTerm(queryText, "近24小时") ||
            ContainsTerm(queryText, "last 24h") || ContainsTerm(queryText, "last_24h"))
        {
            return "last_24h";
        }

        if (ContainsTerm(queryText, "今天") || ContainsEnglishTerm(queryText, "today"))
        {
            return "today";
        }

        if (ContainsTerm(queryText, "昨天") || ContainsEnglishTerm(queryText, "yesterday"))
        {
            return "yesterday";
        }

        return null;
    }

    private static CloudAiReadQuery AddFilter(CloudAiReadQuery query, string field, string value)
    {
        return query.WithFilters(
            query.Filters
                .Where(filter => !field.Equals(filter.Field, StringComparison.OrdinalIgnoreCase))
                .Append(new CloudAiReadFilter(field, "eq", value))
                .ToArray());
    }

    private static bool HasFilter(CloudAiReadQuery query, string field)
    {
        return query.Filters.Any(filter =>
            field.Equals(filter.Field, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(filter.Value));
    }

    private static string? GetFilterValue(CloudAiReadQuery query, string field)
    {
        return query.Filters.FirstOrDefault(filter =>
            field.Equals(filter.Field, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    private static bool ContainsTerm(string? text, string term)
    {
        return !string.IsNullOrWhiteSpace(text) &&
               text.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsEnglishTerm(string? text, string term)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Split([' ', ',', '.', ';', ':', '，', '。', '；', '：', '/', '\\', '-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Any(part => part.Equals(term, StringComparison.OrdinalIgnoreCase));
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
            result.Rows,
            result.ProviderSource,
            result.QueryScope,
            result.RowCount,
            result.NextCursor);
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
