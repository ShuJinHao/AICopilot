using System.Text.Json;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;
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

    private static readonly Func<JsonElement, string, int, CloudAiReadResult<CloudAiReadDeviceDto>> DevicesDocumentMapper = static (root, path, limit) => CloudAiReadDocumentAdapter.MapDevices(root, path, limit);
    private static readonly Func<JsonElement, string, int, CloudAiReadResult<CloudAiReadProcessDto>> ProcessesDocumentMapper = static (root, path, limit) => CloudAiReadDocumentAdapter.MapProcesses(root, path, limit);
    private static readonly Func<JsonElement, string, int, CloudAiReadResult<CloudAiReadClientReleaseVersionDto>> ClientReleasesDocumentMapper = static (root, path, limit) => CloudAiReadDocumentAdapter.MapClientReleases(root, path, limit);
    private static readonly Func<JsonElement, string, int, CloudAiReadResult<CloudAiReadDeviceClientStateDto>> DeviceClientStatesDocumentMapper = static (root, path, limit) => CloudAiReadDocumentAdapter.MapDeviceClientStates(root, path, limit);
    private static readonly Func<JsonElement, string, int, CloudAiReadResult<CloudAiReadCapacitySummaryDto>> CapacitySummaryDocumentMapper = static (root, path, limit) => CloudAiReadDocumentAdapter.MapCapacitySummary(root, path, limit);
    private static readonly Func<JsonElement, string, int, CloudAiReadResult<CloudAiReadCapacityHourlyDto>> CapacityHourlyDocumentMapper = static (root, path, limit) => CloudAiReadDocumentAdapter.MapCapacityHourly(root, path, limit);
    private static readonly Func<JsonElement, string, int, CloudAiReadResult<CloudAiReadDeviceLogDto>> DeviceLogsDocumentMapper = static (root, path, limit) => CloudAiReadDocumentAdapter.MapDeviceLogs(root, path, limit);
    private static readonly Func<JsonElement, string, int, CloudAiReadResult<CloudAiReadProductionRecordDto>> ProductionRecordsDocumentMapper = static (root, path, limit) => CloudAiReadDocumentAdapter.MapProductionRecords(root, path, limit);

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

    public Task<CloudAiReadResult<CloudAiReadDeviceDto>> GetDevicesAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default)
    {
        return GetMappedAsync(
            DevicesPath,
            query,
            CloudAiReadQueryParameterBuilder.BuildDeviceQueryParameters,
            DevicesDocumentMapper,
            cancellationToken);
    }

    public Task<CloudAiReadResult<CloudAiReadProcessDto>> GetProcessesAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default)
    {
        return GetMappedAsync(
            ProcessesPath,
            query,
            CloudAiReadQueryParameterBuilder.BuildProcessQueryParameters,
            ProcessesDocumentMapper,
            cancellationToken);
    }

    public Task<CloudAiReadResult<CloudAiReadClientReleaseVersionDto>> GetClientReleasesAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default)
    {
        return GetMappedAsync(
            ClientReleasesPath,
            query,
            CloudAiReadQueryParameterBuilder.BuildClientReleaseQueryParameters,
            ClientReleasesDocumentMapper,
            cancellationToken);
    }

    public Task<CloudAiReadResult<CloudAiReadDeviceClientStateDto>> GetDeviceClientStatesAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default)
    {
        return GetMappedAsync(
            DeviceClientStatesPath,
            query,
            CloudAiReadQueryParameterBuilder.BuildDeviceClientStateQueryParameters,
            DeviceClientStatesDocumentMapper,
            cancellationToken);
    }

    public Task<CloudAiReadResult<CloudAiReadCapacitySummaryDto>> GetCapacitySummaryAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default)
    {
        return GetMappedAsync(
            CapacitySummaryPath,
            query,
            CloudAiReadQueryParameterBuilder.BuildCapacityQueryParameters,
            CapacitySummaryDocumentMapper,
            cancellationToken);
    }

    public Task<CloudAiReadResult<CloudAiReadCapacityHourlyDto>> GetCapacityHourlyAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default)
    {
        return GetMappedAsync(
            CapacityHourlyPath,
            query,
            CloudAiReadQueryParameterBuilder.BuildCapacityHourlyQueryParameters,
            CapacityHourlyDocumentMapper,
            cancellationToken);
    }

    public Task<CloudAiReadResult<CloudAiReadDeviceLogDto>> GetDeviceLogsAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default)
    {
        return GetMappedAsync(
            DeviceLogsPath,
            query,
            CloudAiReadQueryParameterBuilder.BuildDeviceLogQueryParameters,
            DeviceLogsDocumentMapper,
            cancellationToken);
    }

    public Task<CloudAiReadResult<CloudAiReadProductionRecordDto>> GetProductionRecordsAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default)
    {
        return GetMappedAsync(
            ProductionRecordsPath,
            query,
            CloudAiReadQueryParameterBuilder.BuildProductionRecordQueryParameters,
            ProductionRecordsDocumentMapper,
            cancellationToken);
    }

    private async Task<CloudAiReadResult<T>> GetMappedAsync<T>(
        string path,
        CloudAiReadQuery query,
        Func<CloudAiReadQuery, Dictionary<string, string?>> buildQueryParameters,
        Func<JsonElement, string, int, CloudAiReadResult<T>> mapDocument,
        CancellationToken cancellationToken)
    {
        query = NormalizeQueryLimit(query);
        using var document = await GetJsonAsync(
            path,
            buildQueryParameters(query),
            cancellationToken);

        return mapDocument(document.RootElement, path, query.Limit);
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
        if (plan.Kind != SemanticQueryKind.Detail)
        {
            return ToUntyped(await GetProcessesAsync(query, cancellationToken));
        }

        if (HasFilter(query, "processId"))
        {
            if (!Guid.TryParse(GetFilterValue(query, "processId"), out var expectedProcessId))
            {
                throw new CloudAiReadException(
                    AppProblemCodes.CloudReadonlyIntentUnsupported,
                    "Cloud readonly intent violates the frozen typed semantic plan contract.");
            }

            var directResult = await GetProcessesAsync(query, cancellationToken);
            if (directResult.IsTruncated ||
                directResult.Items.Count != 1 ||
                directResult.Items[0].ProcessId != expectedProcessId)
            {
                throw new CloudAiReadException(
                    CloudAiReadProblemCodes.MissingRequiredParameter,
                    "Cloud AiRead 工序 ID 直查未返回唯一且身份一致的正式工序。");
            }

            return ToUntyped(directResult);
        }

        var exactFilters = plan.Filters
            .Where(filter => filter.Field.Equals("processCode", StringComparison.OrdinalIgnoreCase) ||
                             filter.Field.Equals("processName", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (exactFilters.Length == 0)
        {
            throw new CloudAiReadException(
                AppProblemCodes.CloudReadonlyIntentUnsupported,
                "Cloud readonly intent violates the frozen typed semantic plan contract.");
        }

        var searchResult = await GetProcessesAsync(
            query with { Limit = CloudAiReadRowLimitPolicy.MaxRows },
            cancellationToken);
        if (searchResult.IsTruncated)
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.MissingRequiredParameter,
                "Cloud AiRead 工序搜索结果已截断，不能据此解析唯一工序；请补充精确工序编码或名称。");
        }

        var matches = searchResult.Items
            .Where(item => exactFilters.All(filter =>
                string.Equals(
                    filter.Field.Equals("processCode", StringComparison.OrdinalIgnoreCase)
                        ? item.ProcessCode.Trim()
                        : item.ProcessName.Trim(),
                    filter.Value.Trim(),
                    StringComparison.OrdinalIgnoreCase)))
            .GroupBy(item => item.ProcessId)
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
            query.Limit,
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
                CloudAiReadRowLimitPolicy.MaxRows),
            cancellationToken);
        if (deviceResult.IsTruncated)
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.MissingRequiredParameter,
                "Cloud AiRead 设备搜索结果已截断，不能据此解析唯一 deviceId；请补充 Cloud 正式设备 ID。");
        }

        var deviceIds = deviceResult.Items
            .Where(item => string.Equals(
                item.DeviceCode.Trim(),
                deviceCode?.Trim(),
                StringComparison.OrdinalIgnoreCase))
            .Select(item => item.DeviceId)
            .Distinct()
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
                .Append(new CloudAiReadFilter("deviceId", "eq", deviceIds[0].ToString("D")))
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

    private static CloudAiReadQuery NormalizeQueryLimit(CloudAiReadQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query with { Limit = CloudAiReadRowLimitPolicy.Normalize(query.Limit) };
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
