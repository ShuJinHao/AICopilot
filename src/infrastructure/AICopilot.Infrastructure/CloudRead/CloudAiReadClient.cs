using System.Text.Json;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AICopilot.Infrastructure.CloudRead;

public sealed class CloudAiReadClient(
    HttpClient httpClient,
    IOptions<CloudAiReadOptions> options,
    ICloudDelegationAccessTokenProvider delegationAccessTokenProvider,
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
    private const string DevicePlcsPath = "/api/v1/ai/read/device-plcs";
    private const string DataSchemasPath = "/api/v1/ai/read/data-schemas";

    private static readonly Func<JsonElement, string, int, CloudAiReadResult<CloudAiReadDeviceDto>> DevicesDocumentMapper = static (root, path, limit) => CloudAiReadDocumentAdapter.MapDevices(root, path, limit);
    private static readonly Func<JsonElement, string, int, CloudAiReadResult<CloudAiReadProcessDto>> ProcessesDocumentMapper = static (root, path, limit) => CloudAiReadDocumentAdapter.MapProcesses(root, path, limit);
    private static readonly Func<JsonElement, string, int, CloudAiReadResult<CloudAiReadClientReleaseVersionDto>> ClientReleasesDocumentMapper = static (root, path, limit) => CloudAiReadDocumentAdapter.MapClientReleases(root, path, limit);
    private static readonly Func<JsonElement, string, int, CloudAiReadResult<CloudAiReadDeviceClientStateDto>> DeviceClientStatesDocumentMapper = static (root, path, limit) => CloudAiReadDocumentAdapter.MapDeviceClientStates(root, path, limit);
    private static readonly Func<JsonElement, string, int, CloudAiReadResult<CloudAiReadCapacitySummaryDto>> CapacitySummaryDocumentMapper = static (root, path, limit) => CloudAiReadDocumentAdapter.MapCapacitySummary(root, path, limit);
    private static readonly Func<JsonElement, string, int, CloudAiReadResult<CloudAiReadCapacityHourlyDto>> CapacityHourlyDocumentMapper = static (root, path, limit) => CloudAiReadDocumentAdapter.MapCapacityHourly(root, path, limit);
    private static readonly Func<JsonElement, string, int, CloudAiReadResult<CloudAiReadDeviceLogDto>> DeviceLogsDocumentMapper = static (root, path, limit) => CloudAiReadDocumentAdapter.MapDeviceLogs(root, path, limit);
    private static readonly Func<JsonElement, string, int, CloudAiReadResult<CloudAiReadProductionRecordDto>> ProductionRecordsDocumentMapper = static (root, path, limit) => CloudAiReadDocumentAdapter.MapProductionRecords(root, path, limit);
    private static readonly Func<JsonElement, string, int, CloudAiReadResult<CloudAiReadDevicePlcDto>> DevicePlcsDocumentMapper = static (root, path, limit) => CloudAiReadDocumentAdapter.MapDevicePlcs(root, path, limit);
    private static readonly Func<JsonElement, string, int, CloudAiReadResult<CloudAiReadDataSchemaDto>> DataSchemasDocumentMapper = static (root, path, limit) => CloudAiReadDocumentAdapter.MapDataSchemas(root, path, limit);

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

        var delegatedAccessToken = await delegationAccessTokenProvider.GetCurrentTokenAsync(
            cancellationToken);
        return await httpTransport.GetJsonAsync(
            path,
            query,
            configuredOptions,
            delegatedAccessToken,
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

    public Task<CloudAiReadResult<CloudAiReadDevicePlcDto>> GetDevicePlcsAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default)
    {
        return GetMappedAsync(
            DevicePlcsPath,
            query,
            CloudAiReadQueryParameterBuilder.BuildDevicePlcQueryParameters,
            DevicePlcsDocumentMapper,
            cancellationToken);
    }

    public Task<CloudAiReadResult<CloudAiReadDataSchemaDto>> GetDataSchemasAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default)
    {
        return GetMappedAsync(
            DataSchemasPath,
            query,
            CloudAiReadQueryParameterBuilder.BuildDataSchemaQueryParameters,
            DataSchemasDocumentMapper,
            cancellationToken);
    }

    public async Task<SemanticQueryPlan> SealProductionScopeAsync(
        SemanticQueryPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Target != SemanticQueryTarget.ProductionData)
        {
            return plan;
        }

        EnsureConfigured();
        var originalQuery = CloudAiReadQuery.FromSemanticPlan(plan);
        var resolvedProcessFilter = await ResolveProductionProcessFilterAsync(
            originalQuery,
            cancellationToken);
        var deviceFilters = originalQuery.Filters
            .Where(filter => filter.Field.Equals("deviceId", StringComparison.OrdinalIgnoreCase) ||
                             filter.Field.Equals("deviceCode", StringComparison.OrdinalIgnoreCase) ||
                             filter.Field.Equals("deviceName", StringComparison.OrdinalIgnoreCase))
            .Concat(resolvedProcessFilter is null ? [] : [resolvedProcessFilter])
            .ToArray();
        if (deviceFilters.Length == 0)
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.MissingRequiredParameter,
                "生产数据查询必须先唯一确定 Cloud 设备插件；请补充设备或工序范围。");
        }

        var deviceResult = await GetDevicesAsync(
            new CloudAiReadQuery(
                null,
                deviceFilters,
                null,
                null,
                false,
                CloudAiReadRowLimitPolicy.MaxRows),
            cancellationToken);
        if (deviceResult.IsTruncated)
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.MissingRequiredParameter,
                "Cloud 设备候选结果已截断，无法封印唯一设备范围。");
        }

        var devices = deviceResult.Items
            .Where(device => MatchesDeviceFilters(device, deviceFilters))
            .ToArray();
        if (devices.Length != 1)
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.MissingRequiredParameter,
                "Cloud 工序或设备条件没有唯一命中一个设备插件，请明确选择设备。");
        }

        var device = devices[0];
        var deviceIdFilter = new CloudAiReadFilter("deviceId", "eq", device.DeviceId.ToString("D"));
        var plcResult = await GetDevicePlcsAsync(
            new CloudAiReadQuery(
                null,
                [deviceIdFilter],
                null,
                null,
                false,
                CloudAiReadRowLimitPolicy.MaxRows),
            cancellationToken);
        if (plcResult.IsTruncated)
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.MissingRequiredParameter,
                "Cloud PLC 候选结果已截断，无法封印唯一 PLC 范围。");
        }
        if (plcResult.Items.Count == 0)
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.Unavailable,
                "Cloud 当前没有返回该设备插件的 PLC 权威快照。");
        }

        var requestedPlcCode = GetFilterValue(originalQuery, "plcCode");
        var requestedPlcName = GetFilterValue(originalQuery, "plcName");
        var plcs = plcResult.Items
            .Where(item => item.DeviceId == device.DeviceId)
            .Where(item => string.IsNullOrWhiteSpace(requestedPlcCode) ||
                           string.Equals(item.PlcCode.Trim(), requestedPlcCode.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(item => string.IsNullOrWhiteSpace(requestedPlcName) ||
                           string.Equals(item.PlcName.Trim(), requestedPlcName.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (plcs.Length != 1)
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.MissingRequiredParameter,
                "Cloud 设备插件内的 PLC 范围不唯一，请明确选择 PLC。");
        }

        var plc = plcs[0];
        if (!plc.IsAuthoritative ||
            !string.Equals(plc.Freshness, "Current", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(plc.PluginVersion))
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.Unavailable,
                "Cloud 返回的 PLC 快照非权威、已过期或缺少实际插件版本，本次查询已停止。");
        }

        var schemaResult = await GetDataSchemasAsync(
            new CloudAiReadQuery(
                null,
                [
                    deviceIdFilter,
                    new CloudAiReadFilter("plcCode", "eq", plc.PlcCode)
                ],
                null,
                null,
                false,
                CloudAiReadRowLimitPolicy.MaxRows),
            cancellationToken);
        if (schemaResult.IsTruncated)
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.MissingRequiredParameter,
                "Cloud 业务记录类别候选结果已截断，无法封印唯一 TypeKey。");
        }
        if (schemaResult.Items.Count == 0)
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.Unavailable,
                "Cloud 当前没有返回该设备实际插件版本的业务记录 Schema。");
        }

        var requestedTypeKey = GetFilterValue(originalQuery, "typeKey");
        var scopedSchemas = schemaResult.Items
            .Where(item => item.DeviceId == device.DeviceId)
            .Where(item => item.PlcCode is null ||
                           string.Equals(item.PlcCode.Trim(), plc.PlcCode.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(item => string.IsNullOrWhiteSpace(requestedTypeKey) ||
                           string.Equals(item.TypeKey.Trim(), requestedTypeKey.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (scopedSchemas.Length > 0 && scopedSchemas.Any(item =>
                !string.Equals(item.PluginVersion, plc.PluginVersion, StringComparison.OrdinalIgnoreCase)))
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.Unavailable,
                "Cloud PLC 快照与业务记录 Schema 的实际插件版本不一致。");
        }

        var schemas = scopedSchemas
            .Where(item => string.Equals(item.PluginVersion, plc.PluginVersion, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (schemas.Length != 1)
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.MissingRequiredParameter,
                "Cloud 实际插件版本的业务记录类别没有唯一命中，请明确选择数据类型。");
        }

        var schema = schemas[0];
        if (!schema.QueryModes.Any(mode =>
                string.Equals(mode?.Trim(), "list", StringComparison.OrdinalIgnoreCase)))
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.Unavailable,
                "Cloud 当前业务记录 Schema 不支持 list 查询模式，本次查询已停止。");
        }

        var sealedFilters = originalQuery.Filters
            .Where(filter => !IsProductionScopeField(filter.Field))
            .Select(filter => new SemanticFilter(
                filter.Field,
                filter.Operator switch
                {
                    "contains" => SemanticFilterOperator.Contains,
                    "gte" => SemanticFilterOperator.GreaterOrEqual,
                    "lte" => SemanticFilterOperator.LessOrEqual,
                    "in" => SemanticFilterOperator.In,
                    _ => SemanticFilterOperator.Equal
                },
                filter.Value))
            .Append(new SemanticFilter("deviceId", SemanticFilterOperator.Equal, device.DeviceId.ToString("D")))
            .Append(new SemanticFilter("plcCode", SemanticFilterOperator.Equal, plc.PlcCode))
            .Append(new SemanticFilter("typeKey", SemanticFilterOperator.Equal, schema.TypeKey))
            .OrderBy(filter => filter.Field, StringComparer.Ordinal)
            .ToArray();

        return plan with
        {
            Filters = sealedFilters,
            ProductionMetadataSeal = new ProductionQueryMetadataSeal(
                device.DeviceId,
                plc.PlcCode.Trim(),
                schema.TypeKey.Trim(),
                plc.PluginVersion.Trim(),
                schema.SchemaName.Trim(),
                schema.SchemaVersion)
        };
    }

    private static bool IsProductionScopeField(string field)
    {
        return field.Equals("deviceId", StringComparison.OrdinalIgnoreCase) ||
               field.Equals("deviceCode", StringComparison.OrdinalIgnoreCase) ||
               field.Equals("deviceName", StringComparison.OrdinalIgnoreCase) ||
               field.Equals("processId", StringComparison.OrdinalIgnoreCase) ||
               field.Equals("processCode", StringComparison.OrdinalIgnoreCase) ||
               field.Equals("processName", StringComparison.OrdinalIgnoreCase) ||
               field.Equals("plcCode", StringComparison.OrdinalIgnoreCase) ||
               field.Equals("plcName", StringComparison.OrdinalIgnoreCase) ||
               field.Equals("typeKey", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<CloudAiReadFilter?> ResolveProductionProcessFilterAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken)
    {
        var requestedProcessId = GetFilterValue(query, "processId");
        var exactFilters = query.Filters
            .Where(filter =>
                filter.Field.Equals("processCode", StringComparison.OrdinalIgnoreCase) ||
                filter.Field.Equals("processName", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (exactFilters.Length == 0)
        {
            return string.IsNullOrWhiteSpace(requestedProcessId)
                ? null
                : new CloudAiReadFilter("processId", "eq", requestedProcessId);
        }

        var searchFilters = exactFilters.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(requestedProcessId))
        {
            searchFilters = searchFilters.Prepend(
                new CloudAiReadFilter("processId", "eq", requestedProcessId));
        }

        var processResult = await GetProcessesAsync(
            new CloudAiReadQuery(
                null,
                searchFilters.ToArray(),
                null,
                null,
                false,
                CloudAiReadRowLimitPolicy.MaxRows),
            cancellationToken);
        if (processResult.IsTruncated)
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.MissingRequiredParameter,
                "Cloud 工序候选结果已截断，无法封印唯一工序范围。");
        }

        var processes = processResult.Items
            .Where(process => string.IsNullOrWhiteSpace(requestedProcessId) ||
                              Guid.TryParse(requestedProcessId, out var processId) &&
                              process.ProcessId == processId)
            .Where(process => exactFilters.All(filter =>
                string.Equals(
                    filter.Field.Equals("processCode", StringComparison.OrdinalIgnoreCase)
                        ? process.ProcessCode.Trim()
                        : process.ProcessName.Trim(),
                    filter.Value.Trim(),
                    StringComparison.OrdinalIgnoreCase)))
            .GroupBy(process => process.ProcessId)
            .Select(group => group.First())
            .ToArray();
        if (processes.Length != 1)
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.MissingRequiredParameter,
                "Cloud 工序编码或名称没有唯一命中正式工序，请明确选择工序。");
        }

        return new CloudAiReadFilter(
            "processId",
            "eq",
            processes[0].ProcessId.ToString("D"));
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
        var effectivePlan = plan.Target == SemanticQueryTarget.ProductionData &&
                            !IsProductionScopeSealed(plan)
            ? await SealProductionScopeAsync(plan, cancellationToken)
            : plan;
        var query = await PrepareSemanticQueryAsync(effectivePlan, cancellationToken);
        return effectivePlan.Target switch
        {
            SemanticQueryTarget.Device when effectivePlan.Kind == SemanticQueryKind.Status =>
                ToUntyped(await GetDeviceClientStatesAsync(query, cancellationToken)),
            SemanticQueryTarget.Device => ToUntyped(await GetDevicesAsync(query, cancellationToken)),
            SemanticQueryTarget.Process => await QueryProcessesAsync(effectivePlan, query, cancellationToken),
            SemanticQueryTarget.ClientRelease => ToUntyped(await GetClientReleasesAsync(query, cancellationToken)),
            SemanticQueryTarget.Capacity when ShouldUseCapacityHourly(effectivePlan) => ToUntyped(await GetCapacityHourlyAsync(query, cancellationToken)),
            SemanticQueryTarget.Capacity => ToUntyped(await GetCapacitySummaryAsync(query, cancellationToken)),
            SemanticQueryTarget.DeviceLog => ToUntyped(await GetDeviceLogsAsync(query, cancellationToken)),
            SemanticQueryTarget.ProductionData => ToUntyped(await GetProductionRecordsAsync(query, cancellationToken)),
            _ => throw new NotSupportedException($"Cloud AiRead does not support semantic target '{effectivePlan.Target}'.")
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

    private static bool IsProductionScopeSealed(SemanticQueryPlan plan)
    {
        return ProductionQueryScopePolicy.IsSealed(plan);
    }

    private static string? GetFilterValue(CloudAiReadQuery query, string field)
    {
        return query.Filters.FirstOrDefault(filter =>
            field.Equals(filter.Field, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    private static bool MatchesDeviceFilters(
        CloudAiReadDeviceDto device,
        IReadOnlyCollection<CloudAiReadFilter> filters)
    {
        foreach (var filter in filters)
        {
            if (filter.Field.Equals("deviceId", StringComparison.OrdinalIgnoreCase) &&
                (!Guid.TryParse(filter.Value, out var deviceId) || device.DeviceId != deviceId))
            {
                return false;
            }

            if (filter.Field.Equals("deviceCode", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(device.DeviceCode.Trim(), filter.Value.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (filter.Field.Equals("deviceName", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(device.DeviceName.Trim(), filter.Value.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (filter.Field.Equals("processId", StringComparison.OrdinalIgnoreCase) &&
                (!Guid.TryParse(filter.Value, out var processId) || device.ProcessId != processId))
            {
                return false;
            }
        }

        return true;
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
