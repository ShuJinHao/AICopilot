using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using AICopilot.DataAnalysisService.Semantics;
using AICopilot.Infrastructure.CloudRead;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AICopilot.InProcessTests;

public sealed class CloudAiReadClientContractTests
{
    private const string DeviceId = "11111111-1111-1111-1111-111111111111";
    private const string SecondDeviceId = "22222222-2222-2222-2222-222222222222";
    private const string ProcessId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string SecondProcessId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    private const string ReleaseId = "cccccccc-cccc-cccc-cccc-cccccccccccc";
    private const string ProviderAsOfUtc = "2026-07-10T01:02:03Z";

    [Fact]
    public void ClientContract_ShouldExposeTypedReadsOnly()
    {
        var methods = typeof(ICloudAiReadClient).GetMethods();

        methods.Should().NotContain(method => method.Name == "SendJsonAsync");
        methods.Should().OnlyContain(method =>
            method.Name == "get_IsEnabled" ||
            method.Name == nameof(ICloudAiReadClient.GetDevicesAsync) ||
            method.Name == nameof(ICloudAiReadClient.GetProcessesAsync) ||
            method.Name == nameof(ICloudAiReadClient.GetClientReleasesAsync) ||
            method.Name == nameof(ICloudAiReadClient.GetDeviceClientStatesAsync) ||
            method.Name == nameof(ICloudAiReadClient.GetCapacitySummaryAsync) ||
            method.Name == nameof(ICloudAiReadClient.GetCapacityHourlyAsync) ||
            method.Name == nameof(ICloudAiReadClient.GetDeviceLogsAsync) ||
            method.Name == nameof(ICloudAiReadClient.GetProductionRecordsAsync) ||
            method.Name == nameof(ICloudAiReadClient.QuerySemanticAsync));

        AssertRequiredReferenceProperties<CloudAiReadDeviceDto>(
            nameof(CloudAiReadDeviceDto.DeviceCode),
            nameof(CloudAiReadDeviceDto.DeviceName),
            nameof(CloudAiReadDeviceDto.AdditionalFields));
        AssertRequiredValueProperties<CloudAiReadDeviceDto>(
            nameof(CloudAiReadDeviceDto.DeviceId),
            nameof(CloudAiReadDeviceDto.ProcessId));

        AssertRequiredReferenceProperties<CloudAiReadProcessDto>(
            nameof(CloudAiReadProcessDto.ProcessCode),
            nameof(CloudAiReadProcessDto.ProcessName),
            nameof(CloudAiReadProcessDto.AdditionalFields));
        AssertRequiredValueProperties<CloudAiReadProcessDto>(nameof(CloudAiReadProcessDto.ProcessId));

        AssertRequiredReferenceProperties<CloudAiReadClientReleaseVersionDto>(
            nameof(CloudAiReadClientReleaseVersionDto.ComponentKind),
            nameof(CloudAiReadClientReleaseVersionDto.ComponentKey),
            nameof(CloudAiReadClientReleaseVersionDto.DisplayName),
            nameof(CloudAiReadClientReleaseVersionDto.Channel),
            nameof(CloudAiReadClientReleaseVersionDto.TargetRuntime),
            nameof(CloudAiReadClientReleaseVersionDto.Version),
            nameof(CloudAiReadClientReleaseVersionDto.Status),
            nameof(CloudAiReadClientReleaseVersionDto.AdditionalFields));
        AssertOptionalReferenceProperties<CloudAiReadClientReleaseVersionDto>(
            nameof(CloudAiReadClientReleaseVersionDto.ReleaseNotes));
        AssertRequiredValueProperties<CloudAiReadClientReleaseVersionDto>(
            nameof(CloudAiReadClientReleaseVersionDto.ReleaseId),
            nameof(CloudAiReadClientReleaseVersionDto.CreatedAtUtc));
        AssertOptionalValueProperties<CloudAiReadClientReleaseVersionDto>(
            nameof(CloudAiReadClientReleaseVersionDto.PublishedAtUtc),
            nameof(CloudAiReadClientReleaseVersionDto.DeletedAtUtc));

        AssertRequiredReferenceProperties<CloudAiReadDeviceClientStateDto>(
            nameof(CloudAiReadDeviceClientStateDto.DeviceName),
            nameof(CloudAiReadDeviceClientStateDto.ClientCode),
            nameof(CloudAiReadDeviceClientStateDto.SoftwareStatus),
            nameof(CloudAiReadDeviceClientStateDto.AdditionalFields));
        AssertOptionalReferenceProperties<CloudAiReadDeviceClientStateDto>(
            nameof(CloudAiReadDeviceClientStateDto.PrimaryIp),
            nameof(CloudAiReadDeviceClientStateDto.Channel),
            nameof(CloudAiReadDeviceClientStateDto.HostVersion),
            nameof(CloudAiReadDeviceClientStateDto.HostApiVersion),
            nameof(CloudAiReadDeviceClientStateDto.RuntimeStatus));
        AssertRequiredValueProperties<CloudAiReadDeviceClientStateDto>(
            nameof(CloudAiReadDeviceClientStateDto.DeviceId));
        AssertOptionalValueProperties<CloudAiReadDeviceClientStateDto>(
            nameof(CloudAiReadDeviceClientStateDto.VersionReportedAtUtc),
            nameof(CloudAiReadDeviceClientStateDto.VersionReceivedAtUtc),
            nameof(CloudAiReadDeviceClientStateDto.RuntimeStartedAtUtc),
            nameof(CloudAiReadDeviceClientStateDto.LastRuntimeHeartbeatAtUtc),
            nameof(CloudAiReadDeviceClientStateDto.UpdatedAtUtc));

        AssertRequiredReferenceProperties<CloudAiReadCapacitySummaryDto>(
            nameof(CloudAiReadCapacitySummaryDto.AdditionalFields));
        AssertRequiredValueProperties<CloudAiReadCapacitySummaryDto>(
            nameof(CloudAiReadCapacitySummaryDto.Date),
            nameof(CloudAiReadCapacitySummaryDto.TotalCount),
            nameof(CloudAiReadCapacitySummaryDto.OkCount),
            nameof(CloudAiReadCapacitySummaryDto.NgCount),
            nameof(CloudAiReadCapacitySummaryDto.DayShiftTotal),
            nameof(CloudAiReadCapacitySummaryDto.NightShiftTotal));

        AssertRequiredReferenceProperties<CloudAiReadCapacityHourlyDto>(
            nameof(CloudAiReadCapacityHourlyDto.TimeLabel),
            nameof(CloudAiReadCapacityHourlyDto.ShiftCode),
            nameof(CloudAiReadCapacityHourlyDto.AdditionalFields));
        AssertRequiredValueProperties<CloudAiReadCapacityHourlyDto>(
            nameof(CloudAiReadCapacityHourlyDto.Time),
            nameof(CloudAiReadCapacityHourlyDto.Date),
            nameof(CloudAiReadCapacityHourlyDto.Hour),
            nameof(CloudAiReadCapacityHourlyDto.Minute),
            nameof(CloudAiReadCapacityHourlyDto.TotalCount),
            nameof(CloudAiReadCapacityHourlyDto.OkCount),
            nameof(CloudAiReadCapacityHourlyDto.NgCount),
            nameof(CloudAiReadCapacityHourlyDto.OkRate));

        AssertRequiredReferenceProperties<CloudAiReadDeviceLogDto>(
            nameof(CloudAiReadDeviceLogDto.DeviceName),
            nameof(CloudAiReadDeviceLogDto.Level),
            nameof(CloudAiReadDeviceLogDto.Message),
            nameof(CloudAiReadDeviceLogDto.AdditionalFields));
        AssertRequiredValueProperties<CloudAiReadDeviceLogDto>(
            nameof(CloudAiReadDeviceLogDto.LogId),
            nameof(CloudAiReadDeviceLogDto.DeviceId),
            nameof(CloudAiReadDeviceLogDto.LogTime),
            nameof(CloudAiReadDeviceLogDto.ReceivedAt));

        AssertRequiredReferenceProperties<CloudAiReadProductionFieldSchemaDto>(
            nameof(CloudAiReadProductionFieldSchemaDto.Key),
            nameof(CloudAiReadProductionFieldSchemaDto.Label),
            nameof(CloudAiReadProductionFieldSchemaDto.Type),
            nameof(CloudAiReadProductionFieldSchemaDto.AdditionalFields));
        AssertOptionalReferenceProperties<CloudAiReadProductionFieldSchemaDto>(
            nameof(CloudAiReadProductionFieldSchemaDto.Unit));
        AssertRequiredValueProperties<CloudAiReadProductionFieldSchemaDto>(
            nameof(CloudAiReadProductionFieldSchemaDto.Required));
        AssertOptionalValueProperties<CloudAiReadProductionFieldSchemaDto>(
            nameof(CloudAiReadProductionFieldSchemaDto.Precision));

        AssertRequiredReferenceProperties<CloudAiReadProductionRecordDto>(
            nameof(CloudAiReadProductionRecordDto.TypeKey),
            nameof(CloudAiReadProductionRecordDto.TypeName),
            nameof(CloudAiReadProductionRecordDto.DeviceName),
            nameof(CloudAiReadProductionRecordDto.Fields),
            nameof(CloudAiReadProductionRecordDto.FieldSchema),
            nameof(CloudAiReadProductionRecordDto.AdditionalFields));
        AssertOptionalReferenceProperties<CloudAiReadProductionRecordDto>(
            nameof(CloudAiReadProductionRecordDto.Barcode),
            nameof(CloudAiReadProductionRecordDto.Result));
        AssertRequiredValueProperties<CloudAiReadProductionRecordDto>(
            nameof(CloudAiReadProductionRecordDto.RecordId),
            nameof(CloudAiReadProductionRecordDto.DeviceId));
        AssertOptionalValueProperties<CloudAiReadProductionRecordDto>(
            nameof(CloudAiReadProductionRecordDto.CompletedAt),
            nameof(CloudAiReadProductionRecordDto.ReceivedAt));

        AssertPropertyTypes<CloudAiReadDeviceDto>(
            (nameof(CloudAiReadDeviceDto.DeviceId), typeof(Guid)),
            (nameof(CloudAiReadDeviceDto.ProcessId), typeof(Guid)));
        AssertPropertyTypes<CloudAiReadProcessDto>(
            (nameof(CloudAiReadProcessDto.ProcessId), typeof(Guid)));
        AssertPropertyTypes<CloudAiReadClientReleaseVersionDto>(
            (nameof(CloudAiReadClientReleaseVersionDto.ReleaseId), typeof(Guid)),
            (nameof(CloudAiReadClientReleaseVersionDto.CreatedAtUtc), typeof(DateTime)),
            (nameof(CloudAiReadClientReleaseVersionDto.PublishedAtUtc), typeof(DateTime?)),
            (nameof(CloudAiReadClientReleaseVersionDto.DeletedAtUtc), typeof(DateTime?)));
        AssertPropertyTypes<CloudAiReadDeviceClientStateDto>(
            (nameof(CloudAiReadDeviceClientStateDto.DeviceId), typeof(Guid)),
            (nameof(CloudAiReadDeviceClientStateDto.VersionReportedAtUtc), typeof(DateTime?)),
            (nameof(CloudAiReadDeviceClientStateDto.VersionReceivedAtUtc), typeof(DateTime?)),
            (nameof(CloudAiReadDeviceClientStateDto.RuntimeStartedAtUtc), typeof(DateTime?)),
            (nameof(CloudAiReadDeviceClientStateDto.LastRuntimeHeartbeatAtUtc), typeof(DateTime?)),
            (nameof(CloudAiReadDeviceClientStateDto.UpdatedAtUtc), typeof(DateTime?)));
        AssertPropertyTypes<CloudAiReadCapacitySummaryDto>(
            (nameof(CloudAiReadCapacitySummaryDto.Date), typeof(DateOnly)),
            (nameof(CloudAiReadCapacitySummaryDto.TotalCount), typeof(int)),
            (nameof(CloudAiReadCapacitySummaryDto.OkCount), typeof(int)),
            (nameof(CloudAiReadCapacitySummaryDto.NgCount), typeof(int)),
            (nameof(CloudAiReadCapacitySummaryDto.DayShiftTotal), typeof(int)),
            (nameof(CloudAiReadCapacitySummaryDto.NightShiftTotal), typeof(int)));
        AssertPropertyTypes<CloudAiReadCapacityHourlyDto>(
            (nameof(CloudAiReadCapacityHourlyDto.Time), typeof(DateTime)),
            (nameof(CloudAiReadCapacityHourlyDto.Date), typeof(DateOnly)),
            (nameof(CloudAiReadCapacityHourlyDto.Hour), typeof(int)),
            (nameof(CloudAiReadCapacityHourlyDto.Minute), typeof(int)),
            (nameof(CloudAiReadCapacityHourlyDto.TotalCount), typeof(int)),
            (nameof(CloudAiReadCapacityHourlyDto.OkCount), typeof(int)),
            (nameof(CloudAiReadCapacityHourlyDto.NgCount), typeof(int)),
            (nameof(CloudAiReadCapacityHourlyDto.OkRate), typeof(decimal)));
        AssertPropertyTypes<CloudAiReadDeviceLogDto>(
            (nameof(CloudAiReadDeviceLogDto.LogId), typeof(Guid)),
            (nameof(CloudAiReadDeviceLogDto.DeviceId), typeof(Guid)),
            (nameof(CloudAiReadDeviceLogDto.LogTime), typeof(DateTime)),
            (nameof(CloudAiReadDeviceLogDto.ReceivedAt), typeof(DateTime)));
        AssertPropertyTypes<CloudAiReadProductionFieldSchemaDto>(
            (nameof(CloudAiReadProductionFieldSchemaDto.Precision), typeof(int?)),
            (nameof(CloudAiReadProductionFieldSchemaDto.Required), typeof(bool)));
        AssertPropertyTypes<CloudAiReadProductionRecordDto>(
            (nameof(CloudAiReadProductionRecordDto.RecordId), typeof(Guid)),
            (nameof(CloudAiReadProductionRecordDto.DeviceId), typeof(Guid)),
            (nameof(CloudAiReadProductionRecordDto.CompletedAt), typeof(DateTime?)),
            (nameof(CloudAiReadProductionRecordDto.ReceivedAt), typeof(DateTime?)));
    }

    [Fact]
    public async Task Client_ShouldMapDevicePayloadIntoInternalReadonlyDto()
    {
        using var httpClient = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(CreateEnvelope(
                new[]
                {
                    new
                    {
                        id = DeviceId,
                        deviceCode = "DEV-001",
                        deviceName = "正极模切客户端",
                        processId = ProcessId
                    }
                },
                rowCount: 1,
                source: "devices"))
        }));
        var client = CreateClient(httpClient);

        var result = await client.GetDevicesAsync(new CloudAiReadQuery(
            "列出设备",
            [],
            null,
            "deviceCode",
            false,
            20));

        result.SourceLabel.Should().Contain("Cloud AiRead API");
        result.IsTruncated.Should().BeFalse();
        result.Items.Should().ContainSingle();
        result.Items[0].DeviceCode.Should().Be("DEV-001");
        result.Items[0].ProcessId.Should().Be(Guid.Parse(ProcessId));
        result.AsOfUtc.Should().Be(DateTimeOffset.Parse(ProviderAsOfUtc));
        result.ProviderSource.Should().Be("devices");
        result.QueryScope.Should().Be("test=present");
        result.RowCount.Should().Be(1);
        result.Rows[0]["deviceName"].Should().Be("正极模切客户端");
        result.Rows[0].Should().NotContainKey("status");
        result.Rows[0].Should().NotContainKey("lineName");
        result.Rows[0].Should().NotContainKey("updatedAt");
    }

    [Fact]
    public async Task Client_ShouldSendDeviceQueryWithExactAndKeywordParametersAsAndFilters()
    {
        HttpRequestMessage? capturedRequest = null;
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            capturedRequest = request;
            return CreateOkItemsResponse();
        }));
        var client = CreateClient(httpClient);

        await client.GetDevicesAsync(new CloudAiReadQuery(
            null,
            [
                new CloudAiReadFilter("deviceId", "eq", DeviceId),
                new CloudAiReadFilter("deviceCode", "eq", "DEV-001"),
                new CloudAiReadFilter("processId", "eq", ProcessId),
                new CloudAiReadFilter("deviceName", "contains", "正极模切")
            ],
            null,
            "deviceCode",
            false,
            20));

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.AbsolutePath.Should().Be("/api/v1/ai/read/devices");
        var query = ParseQuery(capturedRequest.RequestUri);
        query.Should().Contain("deviceId", DeviceId);
        query.Should().Contain("deviceCode", "DEV-001");
        query.Should().Contain("processId", ProcessId);
        query.Should().Contain("keyword", "正极模切");
        query.Should().Contain("maxRows", "20");
        AssertNoLegacyParameters(query);
    }

    [Fact]
    public async Task Client_ShouldNotSendNaturalLanguageAsDeviceKeyword()
    {
        HttpRequestMessage? capturedRequest = null;
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            capturedRequest = request;
            return CreateOkItemsResponse();
        }));
        var client = CreateClient(httpClient);

        await client.GetDevicesAsync(new CloudAiReadQuery(
            "请列出所有设备并分析当前状态",
            [],
            null,
            "deviceCode",
            false,
            20));

        ParseQuery(capturedRequest!.RequestUri!).Should().NotContainKey("keyword");
    }

    [Fact]
    public async Task Client_ShouldSendProcessQueryWithKeywordAndMaxRowsOnly()
    {
        HttpRequestMessage? capturedRequest = null;
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(CreateEnvelope(
                    new[]
                    {
                        new
                        {
                            id = ProcessId,
                            processCode = "CUT",
                            processName = "模切"
                        }
                    },
                    rowCount: 1,
                    source: "processes"))
            };
        }));
        var client = CreateClient(httpClient);

        var result = await client.GetProcessesAsync(new CloudAiReadQuery(
            "不要作为 queryText 发送",
            [
                new CloudAiReadFilter("processId", "eq", ProcessId),
                new CloudAiReadFilter("processName", "contains", "模切")
            ],
            null,
            "processName",
            false,
            15));

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.AbsolutePath.Should().Be("/api/v1/ai/read/processes");
        var query = ParseQuery(capturedRequest.RequestUri);
        query.Should().Contain("processId", ProcessId);
        query.Should().Contain("keyword", "模切");
        query.Should().Contain("maxRows", "15");
        AssertNoLegacyParameters(query);
        result.Items.Should().ContainSingle().Which.ProcessCode.Should().Be("CUT");
        result.Rows[0]["processName"].Should().Be("模切");
    }

    [Fact]
    public async Task Client_ShouldSendClientReleaseQueryWithCloudAiReadParametersOnly()
    {
        HttpRequestMessage? capturedRequest = null;
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(CreateEnvelope(
                    new[]
                    {
                        new
                        {
                            id = ReleaseId,
                            componentKind = "Host",
                            componentKey = "edge-host",
                            displayName = "Edge Host",
                            channel = "stable",
                            targetRuntime = "win-x64",
                            version = "1.2.3",
                            status = "Published",
                            releaseNotes = "history",
                            createdAtUtc = "2026-05-10T01:02:03Z",
                            publishedAtUtc = "2026-05-11T01:02:03Z",
                            deletedAtUtc = (string?)null
                        }
                    },
                    rowCount: 1,
                    source: "client_release_versions"))
            };
        }));
        var client = CreateClient(httpClient);

        var result = await client.GetClientReleasesAsync(new CloudAiReadQuery(
            "不要作为 queryText 发送",
            [
                new CloudAiReadFilter("channel", "eq", "stable"),
                new CloudAiReadFilter("targetRuntime", "eq", "win-x64"),
                new CloudAiReadFilter("status", "eq", "Published"),
                new CloudAiReadFilter("includeArchived", "eq", "1")
            ],
            null,
            "publishedAtUtc",
            true,
            12));

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.AbsolutePath.Should().Be("/api/v1/ai/read/client-releases");
        var query = ParseQuery(capturedRequest.RequestUri);
        query.Should().Contain("channel", "stable");
        query.Should().Contain("targetRuntime", "win-x64");
        query.Should().Contain("status", "Published");
        query.Should().Contain("includeArchived", "true");
        query.Should().Contain("maxRows", "12");
        AssertNoLegacyParameters(query);
        result.Items.Should().ContainSingle().Which.Version.Should().Be("1.2.3");
        result.Rows[0]["componentKey"].Should().Be("edge-host");
    }

    [Fact]
    public async Task QuerySemantic_ShouldRouteProcessDetailThroughProcessesAndRequireUniqueExactMatch()
    {
        HttpRequestMessage? capturedRequest = null;
        var sendCount = 0;
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            sendCount++;
            capturedRequest = request;
            object[] items = sendCount switch
            {
                1 => new object[]
                {
                    new { id = SecondProcessId, processCode = "CUT-OLD", processName = "旧模切" }
                },
                2 =>
                [
                    new { id = SecondProcessId, processCode = "CUT-OLD", processName = "旧模切" },
                    new { id = ProcessId, processCode = "CUT", processName = "模切" }
                ],
                3 =>
                [
                    new { id = SecondProcessId, processCode = "CUT", processName = "模切" }
                ],
                _ =>
                [
                    new { id = ProcessId, processCode = "CUT", processName = "模切" }
                ]
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(CreateEnvelope(
                    items,
                    rowCount: items.Length,
                    source: "processes"))
            };
        }));
        var client = CreateClient(httpClient);
        var plan = new SemanticQueryPlan(
            "Analysis.Process.Detail",
            SemanticQueryTarget.Process,
            SemanticQueryKind.Detail,
            "查看 CUT 工序详情",
            new SemanticProjection(["processId", "processCode", "processName"]),
            [new SemanticFilter("processCode", SemanticFilterOperator.Equal, "CUT")],
            null,
            null,
            1);

        var overLimitAction = () => client.QuerySemanticAsync(plan with { Limit = 101 });

        var overLimitException = await overLimitAction.Should().ThrowAsync<CloudAiReadException>();
        overLimitException.Which.Code.Should().Be(AppProblemCodes.CloudReadonlyIntentUnsupported);
        overLimitException.Which.Message.Should().Be(
            "Cloud readonly intent violates the frozen typed semantic plan contract.");
        sendCount.Should().Be(0);

        var keywordOnlyAction = () => client.QuerySemanticAsync(plan with
        {
            Filters = [new SemanticFilter("keyword", SemanticFilterOperator.Contains, "CUT")]
        });
        var keywordOnlyException = await keywordOnlyAction.Should().ThrowAsync<CloudAiReadException>();
        keywordOnlyException.Which.Code.Should().Be(AppProblemCodes.CloudReadonlyIntentUnsupported);
        keywordOnlyException.Which.Message.Should().Be(
            "Cloud readonly intent violates the frozen typed semantic plan contract.");
        sendCount.Should().Be(0);

        var fuzzyOnlyAction = () => client.QuerySemanticAsync(plan);
        var fuzzyOnlyException = await fuzzyOnlyAction.Should().ThrowAsync<CloudAiReadException>();
        fuzzyOnlyException.Which.Code.Should().Be(CloudAiReadProblemCodes.MissingRequiredParameter);
        sendCount.Should().Be(1);

        var result = await client.QuerySemanticAsync(plan);

        capturedRequest!.RequestUri!.AbsolutePath.Should().Be("/api/v1/ai/read/processes");
        ParseQuery(capturedRequest.RequestUri).Should().Contain("maxRows", "100");
        sendCount.Should().Be(2);
        result.Rows.Should().ContainSingle().Which["processCode"].Should().Be("CUT");
        result.Limit.Should().Be(1);

        var processIdPlan = plan with
        {
            Filters =
            [
                new SemanticFilter(
                    "processId",
                    SemanticFilterOperator.Equal,
                    ProcessId)
            ]
        };
        var mismatchedDirectAction = () => client.QuerySemanticAsync(processIdPlan);
        var mismatchedDirectException = await mismatchedDirectAction.Should().ThrowAsync<CloudAiReadException>();
        mismatchedDirectException.Which.Code.Should().Be(CloudAiReadProblemCodes.MissingRequiredParameter);
        sendCount.Should().Be(3);

        var directResult = await client.QuerySemanticAsync(processIdPlan);

        var directQuery = ParseQuery(capturedRequest!.RequestUri!);
        directQuery.Should().Contain("processId", ProcessId);
        directQuery.Should().Contain("maxRows", "1");
        sendCount.Should().Be(4);
        directResult.Rows.Should().ContainSingle().Which["processId"].Should().Be(Guid.Parse(ProcessId));
    }

    [Fact]
    public async Task QuerySemantic_ShouldRouteClientReleaseListThroughOfficialGetEndpoint()
    {
        HttpRequestMessage? capturedRequest = null;
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            capturedRequest = request;
            return CreateOkItemsResponse();
        }));
        var client = CreateClient(httpClient);
        var plan = new SemanticQueryPlan(
            "Analysis.ClientRelease.List",
            SemanticQueryTarget.ClientRelease,
            SemanticQueryKind.List,
            "列出 stable 通道的客户端发布版本",
            new SemanticProjection(["componentKey", "version", "channel", "targetRuntime", "status"]),
            [
                new SemanticFilter("channel", SemanticFilterOperator.Equal, "stable"),
                new SemanticFilter("targetRuntime", SemanticFilterOperator.Equal, "win-x64"),
                new SemanticFilter("status", SemanticFilterOperator.Equal, "Published")
            ],
            null,
            null,
            20);

        await client.QuerySemanticAsync(plan);

        capturedRequest!.Method.Should().Be(HttpMethod.Get);
        capturedRequest.RequestUri!.AbsolutePath.Should().Be("/api/v1/ai/read/client-releases");
        var query = ParseQuery(capturedRequest.RequestUri);
        query.Should().Contain("channel", "stable");
        query.Should().Contain("targetRuntime", "win-x64");
        query.Should().Contain("status", "Published");
    }

    [Fact]
    public async Task Client_ShouldSendDeviceClientStateQueryWithOfficialParametersOnly()
    {
        HttpRequestMessage? capturedRequest = null;
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(CreateEnvelope(
                    new[]
                    {
                        new
                        {
                            deviceId = DeviceId,
                            deviceName = "模切一号",
                            clientCode = "EDGE-001",
                            primaryIp = "10.0.0.11",
                            channel = "stable",
                            hostVersion = "1.2.3",
                            hostApiVersion = "2026.07",
                            versionReportedAtUtc = (string?)null,
                            versionReceivedAtUtc = (string?)null,
                            softwareStatus = "Running",
                            runtimeStatus = "Running",
                            runtimeStartedAtUtc = (string?)null,
                            lastRuntimeHeartbeatAtUtc = "2026-05-11T01:02:03Z",
                            updatedAtUtc = "2026-05-11T01:02:03Z"
                        }
                    },
                    rowCount: 1,
                    source: "device_client_states"))
            };
        }));
        var client = CreateClient(httpClient);

        var result = await client.GetDeviceClientStatesAsync(new CloudAiReadQuery(
            "不要作为 queryText 发送",
            [
                new CloudAiReadFilter("deviceId", "eq", DeviceId),
                new CloudAiReadFilter("clientCode", "eq", "EDGE-001"),
                new CloudAiReadFilter("processId", "eq", ProcessId),
                new CloudAiReadFilter("deviceName", "contains", "模切")
            ],
            null,
            "lastRuntimeHeartbeatAtUtc",
            true,
            10));

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.AbsolutePath.Should().Be("/api/v1/ai/read/device-client-states");
        var query = ParseQuery(capturedRequest.RequestUri);
        query.Should().Contain("deviceId", DeviceId);
        query.Should().Contain("deviceCode", "EDGE-001");
        query.Should().Contain("processId", ProcessId);
        query.Should().Contain("keyword", "模切");
        query.Should().Contain("maxRows", "10");
        AssertNoLegacyParameters(query);
        result.Items.Should().ContainSingle().Which.RuntimeStatus.Should().Be("Running");
        result.Items[0].SoftwareStatus.Should().Be("Running");
        result.Rows[0]["clientCode"].Should().Be("EDGE-001");
        result.Items[0].LastRuntimeHeartbeatAtUtc.Should().Be(
            new DateTime(2026, 5, 11, 1, 2, 3, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Client_ShouldSendCapacityQueryWithCloudAiReadParametersOnly()
    {
        HttpRequestMessage? capturedRequest = null;
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            capturedRequest = request;
            return CreateOkItemsResponse();
        }));
        var client = CreateClient(httpClient);

        var result = await client.GetCapacitySummaryAsync(new CloudAiReadQuery(
            "不要作为 queryText 发送",
            [
                new CloudAiReadFilter("deviceId", "eq", DeviceId),
                new CloudAiReadFilter("plcName", "eq", "PLC-A")
            ],
            CreateRange("2026-04-20T01:02:03Z", "2026-04-21T04:05:06Z"),
            "occurredAt",
            true,
            200));

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.AbsolutePath.Should().Be("/api/v1/ai/read/capacity/summary");
        var query = ParseQuery(capturedRequest.RequestUri);
        query.Should().Contain("deviceId", DeviceId);
        query.Should().Contain("startDate", "2026-04-20");
        query.Should().Contain("endDate", "2026-04-21");
        query.Should().Contain("plcName", "PLC-A");
        query.Should().Contain("maxRows", "100");
        result.Limit.Should().Be(100);
        AssertNoLegacyParameters(query);
    }

    [Fact]
    public async Task Client_ShouldSendDeviceLogQueryWithCloudAiReadParametersOnly()
    {
        HttpRequestMessage? capturedRequest = null;
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            capturedRequest = request;
            return CreateOkItemsResponse();
        }));
        var client = CreateClient(httpClient);

        await client.GetDeviceLogsAsync(new CloudAiReadQuery(
            null,
            [
                new CloudAiReadFilter("deviceId", "eq", DeviceId),
                new CloudAiReadFilter("minLevel", "eq", "warn"),
                new CloudAiReadFilter("keyword", "eq", "overload")
            ],
            CreateRange("2026-04-20T00:00:00Z", "2026-04-21T00:00:00Z"),
            "occurredAt",
            true,
            30));

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.AbsolutePath.Should().Be("/api/v1/ai/read/device-logs");
        var query = ParseQuery(capturedRequest.RequestUri);
        query.Should().Contain("deviceId", DeviceId);
        query.Should().Contain("startTime", "2026-04-20T00:00:00.0000000Z");
        query.Should().Contain("endTime", "2026-04-21T00:00:00.0000000Z");
        query.Should().Contain("minLevel", "WARN");
        query.Should().Contain("keyword", "overload");
        query.Should().NotContainKey("level");
        query.Should().Contain("maxRows", "30");
        AssertNoLegacyParameters(query);
    }

    [Fact]
    public async Task Client_ShouldSendDeviceLogPresetWithCloudAiReadParametersOnly()
    {
        HttpRequestMessage? capturedRequest = null;
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            capturedRequest = request;
            return CreateOkItemsResponse();
        }));
        var client = CreateClient(httpClient);

        await client.GetDeviceLogsAsync(new CloudAiReadQuery(
            "查看最近日志",
            [
                new CloudAiReadFilter("deviceId", "eq", DeviceId),
                new CloudAiReadFilter("preset", "eq", "last_24h")
            ],
            null,
            "occurredAt",
            true,
            30));

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.AbsolutePath.Should().Be("/api/v1/ai/read/device-logs");
        var query = ParseQuery(capturedRequest.RequestUri);
        query.Should().Contain("deviceId", DeviceId);
        query.Should().Contain("preset", "last_24h");
        query.Should().NotContainKey("startTime");
        query.Should().NotContainKey("endTime");
        AssertNoLegacyParameters(query);
    }

    [Fact]
    public async Task Client_ShouldResolveDeviceCodeBeforeSemanticDeviceLogQuery()
    {
        var requestUris = new List<Uri>();
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            requestUris.Add(request.RequestUri!);
            if (request.RequestUri!.AbsolutePath == "/api/v1/ai/read/devices")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(CreateEnvelope(
                        new[]
                        {
                            new
                            {
                                id = DeviceId,
                                deviceCode = "DEV-001",
                                deviceName = "正极模切客户端",
                                processId = ProcessId
                            }
                        },
                        rowCount: 1,
                        source: "devices"))
                };
            }

            return CreateOkItemsResponse();
        }));
        var client = CreateClient(httpClient);
        var plan = new SemanticQueryPlan(
            "Analysis.DeviceLog.Latest",
            SemanticQueryTarget.DeviceLog,
            SemanticQueryKind.Latest,
            "查看 DEV-001 最近日志",
            new SemanticProjection(["deviceId", "level", "message"]),
            [new SemanticFilter("deviceCode", SemanticFilterOperator.Equal, "DEV-001")],
            null,
            new SemanticSort("occurredAt", SemanticSortDirection.Desc),
            20);

        await client.QuerySemanticAsync(plan);

        requestUris.Should().HaveCount(2);
        requestUris[0].AbsolutePath.Should().Be("/api/v1/ai/read/devices");
        ParseQuery(requestUris[0]).Should().Contain("deviceCode", "DEV-001");
        requestUris[1].AbsolutePath.Should().Be("/api/v1/ai/read/device-logs");
        var query = ParseQuery(requestUris[1]);
        query.Should().Contain("deviceId", DeviceId);
        query.Should().Contain("preset", "last_24h");
        query.Should().NotContainKey("deviceCode");
        AssertNoLegacyParameters(query);
    }

    [Fact]
    public async Task Client_ShouldRouteDeviceCodeStatusDirectlyToDeviceClientStates()
    {
        var requestUris = new List<Uri>();
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            requestUris.Add(request.RequestUri!);
            request.RequestUri!.AbsolutePath.Should().Be("/api/v1/ai/read/device-client-states");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(CreateEnvelope(
                    new[]
                    {
                        new
                        {
                            deviceId = DeviceId,
                            deviceName = "正极模切客户端",
                            clientCode = "DEV-001",
                            primaryIp = (string?)null,
                            channel = (string?)null,
                            hostVersion = (string?)null,
                            hostApiVersion = (string?)null,
                            versionReportedAtUtc = (string?)null,
                            versionReceivedAtUtc = (string?)null,
                            softwareStatus = "Running",
                            runtimeStatus = "Running",
                            runtimeStartedAtUtc = "2026-07-10T01:00:00Z",
                            lastRuntimeHeartbeatAtUtc = "2026-07-10T01:02:00Z",
                            updatedAtUtc = "2026-07-10T01:02:01Z"
                        }
                    },
                    rowCount: 1,
                    source: "device_client_states"))
            };
        }));
        var client = CreateClient(httpClient);
        var plan = new SemanticQueryPlan(
            "Analysis.Device.Status",
            SemanticQueryTarget.Device,
            SemanticQueryKind.Status,
            "设备 DEV-001 最后上报的运行状态",
            new SemanticProjection(["deviceId", "deviceName", "clientCode", "softwareStatus", "runtimeStatus", "lastRuntimeHeartbeatAtUtc"]),
            [new SemanticFilter("deviceCode", SemanticFilterOperator.Equal, "DEV-001")],
            null,
            null,
            10);

        var result = await client.QuerySemanticAsync(plan);

        requestUris.Should().ContainSingle();
        requestUris[0].AbsolutePath.Should().Be("/api/v1/ai/read/device-client-states");
        ParseQuery(requestUris[0]).Should().Contain("deviceCode", "DEV-001");
        ParseQuery(requestUris[0]).Should().NotContainKey("deviceId");
        result.Rows.Should().ContainSingle().Which["runtimeStatus"].Should().Be("Running");
        result.Rows[0]["softwareStatus"].Should().Be("Running");
        result.Rows[0]["lastRuntimeHeartbeatAtUtc"].Should().Be(
            new DateTime(2026, 7, 10, 1, 2, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Client_ShouldRejectUnsupportedStateFiltersBeforeHttp()
    {
        var sendCount = 0;
        using var httpClient = new HttpClient(new StubHandler(_ =>
        {
            sendCount++;
            return CreateOkItemsResponse();
        }));
        var client = CreateClient(httpClient);

        var exception = await client.Invoking(value => value.GetDeviceClientStatesAsync(new CloudAiReadQuery(
                null,
                [new CloudAiReadFilter("runtimeStatus", "eq", "Running")],
                null,
                null,
                false,
                10)))
            .Should().ThrowAsync<CloudAiReadException>();

        exception.Which.Code.Should().Be(CloudAiReadProblemCodes.MissingRequiredParameter);
        sendCount.Should().Be(0);
    }

    [Theory]
    [InlineData("capacity", "processName")]
    [InlineData("hourly", "stationName")]
    [InlineData("logs", "source")]
    [InlineData("production", "typeName")]
    [InlineData("releases", "runtime")]
    public async Task Client_ShouldRejectUnsupportedOrLegacyFiltersBeforeHttp(
        string endpoint,
        string unsupportedField)
    {
        var sendCount = 0;
        using var httpClient = new HttpClient(new StubHandler(_ =>
        {
            sendCount++;
            return CreateOkItemsResponse();
        }));
        var client = CreateClient(httpClient);
        var filters = new List<CloudAiReadFilter>
        {
            new("deviceId", "eq", DeviceId),
            new(unsupportedField, "eq", "legacy-value")
        };
        var query = new CloudAiReadQuery(
            null,
            filters,
            CreateRange("2026-04-20T00:00:00Z", "2026-04-21T00:00:00Z"),
            null,
            false,
            10);

        Func<Task> act = endpoint switch
        {
            "capacity" => () => client.GetCapacitySummaryAsync(query),
            "hourly" => () => client.GetCapacityHourlyAsync(query.WithFilters(
                filters.Append(new CloudAiReadFilter("date", "eq", "2026-04-20")).ToArray())),
            "logs" => () => client.GetDeviceLogsAsync(query),
            "production" => () => client.GetProductionRecordsAsync(query),
            "releases" => () => client.GetClientReleasesAsync(query),
            _ => throw new InvalidOperationException("Unknown endpoint test case.")
        };

        var exception = await act.Should().ThrowAsync<CloudAiReadException>();
        exception.Which.Code.Should().Be(CloudAiReadProblemCodes.MissingRequiredParameter);
        sendCount.Should().Be(0);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Client_ShouldRejectInvalidStateDeviceIdBeforeHttp(string invalidDeviceId)
    {
        var sendCount = 0;
        using var httpClient = new HttpClient(new StubHandler(_ =>
        {
            sendCount++;
            return CreateOkItemsResponse();
        }));
        var client = CreateClient(httpClient);

        var exception = await client.Invoking(value => value.GetDeviceClientStatesAsync(new CloudAiReadQuery(
                null,
                [new CloudAiReadFilter("deviceId", "eq", invalidDeviceId)],
                null,
                null,
                false,
                10)))
            .Should().ThrowAsync<CloudAiReadException>();

        exception.Which.Code.Should().Be(CloudAiReadProblemCodes.MissingRequiredParameter);
        sendCount.Should().Be(0);
    }

    [Fact]
    public async Task QuerySemantic_ShouldReturnMultipleDeviceStatusRowsWithoutInventingMissingHeartbeat()
    {
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            request.RequestUri!.AbsolutePath.Should().Be("/api/v1/ai/read/device-client-states");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(CreateEnvelope(
                    new object[]
                    {
                        new
                        {
                            deviceId = DeviceId,
                            deviceName = "设备一",
                            clientCode = "EDGE-001",
                            primaryIp = (string?)null,
                            channel = (string?)null,
                            hostVersion = (string?)null,
                            hostApiVersion = (string?)null,
                            versionReportedAtUtc = (string?)null,
                            versionReceivedAtUtc = (string?)null,
                            softwareStatus = "Running",
                            runtimeStatus = "Running",
                            runtimeStartedAtUtc = (string?)null,
                            lastRuntimeHeartbeatAtUtc = "2026-07-10T01:02:00Z",
                            updatedAtUtc = (string?)null
                        },
                        new
                        {
                            deviceId = SecondDeviceId,
                            deviceName = "设备二",
                            clientCode = "EDGE-002",
                            primaryIp = (string?)null,
                            channel = (string?)null,
                            hostVersion = (string?)null,
                            hostApiVersion = (string?)null,
                            versionReportedAtUtc = (string?)null,
                            versionReceivedAtUtc = (string?)null,
                            softwareStatus = "MissingRuntimeHeartbeat",
                            runtimeStatus = (string?)null,
                            runtimeStartedAtUtc = (string?)null,
                            lastRuntimeHeartbeatAtUtc = (string?)null,
                            updatedAtUtc = (string?)null
                        }
                    },
                    rowCount: 2,
                    source: "device_client_states"))
            };
        }));
        var client = CreateClient(httpClient);
        var plan = new SemanticQueryPlan(
            "Analysis.Device.Status",
            SemanticQueryTarget.Device,
            SemanticQueryKind.Status,
            "列出设备最后上报运行状态",
            new SemanticProjection(["deviceId", "clientCode", "softwareStatus", "runtimeStatus", "lastRuntimeHeartbeatAtUtc"]),
            [],
            null,
            null,
            10);

        var result = await client.QuerySemanticAsync(plan);

        result.Rows.Should().HaveCount(2);
        result.Rows[1]["softwareStatus"].Should().Be("MissingRuntimeHeartbeat");
        result.Rows[1]["runtimeStatus"].Should().BeNull();
        result.Rows[1]["lastRuntimeHeartbeatAtUtc"].Should().BeNull();
    }

    [Fact]
    public async Task DeviceStatus_ShouldSurfaceCloudPermissionFailure()
    {
        using var httpClient = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("{}")
        }));
        var client = CreateClient(httpClient);

        var exception = await client.Invoking(value => value.GetDeviceClientStatesAsync(new CloudAiReadQuery(
                null,
                [new CloudAiReadFilter("deviceId", "eq", DeviceId)],
                null,
                null,
                false,
                10)))
            .Should().ThrowAsync<CloudAiReadException>();

        exception.Which.Code.Should().Be(CloudAiReadProblemCodes.Forbidden);
    }

    [Fact]
    public async Task Client_ShouldSendCapacityHourlyQueryWithCloudAiReadParametersOnly()
    {
        HttpRequestMessage? capturedRequest = null;
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            capturedRequest = request;
            return CreateOkItemsResponse();
        }));
        var client = CreateClient(httpClient);

        await client.GetCapacityHourlyAsync(new CloudAiReadQuery(
            "不要作为 queryText 发送",
            [
                new CloudAiReadFilter("deviceId", "eq", DeviceId),
                new CloudAiReadFilter("date", "eq", "2026-04-20"),
                new CloudAiReadFilter("plcName", "eq", "PLC-A")
            ],
            null,
            "occurredAt",
            true,
            40));

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.AbsolutePath.Should().Be("/api/v1/ai/read/capacity/hourly");
        var query = ParseQuery(capturedRequest.RequestUri);
        query.Should().Contain("deviceId", DeviceId);
        query.Should().Contain("date", "2026-04-20");
        query.Should().Contain("plcName", "PLC-A");
        query.Should().Contain("maxRows", "40");
        AssertNoLegacyParameters(query);
    }

    [Fact]
    public async Task Client_ShouldSendProductionRecordQueryWithCloudAiReadParametersOnly()
    {
        HttpRequestMessage? capturedRequest = null;
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            capturedRequest = request;
            return CreateOkItemsResponse();
        }));
        var client = CreateClient(httpClient);

        await client.GetProductionRecordsAsync(new CloudAiReadQuery(
            "不要作为 queryText 发送",
            [
                new CloudAiReadFilter("deviceId", "eq", DeviceId),
                new CloudAiReadFilter("typeKey", "eq", "cp"),
                new CloudAiReadFilter("plcCode", "eq", "P2-CP05"),
                new CloudAiReadFilter("plcName", "eq", "正极模切05"),
                new CloudAiReadFilter("barcode", "eq", "CP-CLIP-001"),
                new CloudAiReadFilter("result", "eq", "OK"),
                new CloudAiReadFilter("fieldMode", "eq", "full")
            ],
            CreateRange("2026-04-20T00:00:00Z", "2026-04-21T00:00:00Z"),
            "occurredAt",
            true,
            40));

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.AbsolutePath.Should().Be("/api/v1/ai/read/production-records");
        var query = ParseQuery(capturedRequest.RequestUri);
        query.Should().Contain("typeKey", "cp");
        query.Should().Contain("deviceId", DeviceId);
        query.Should().Contain("plcCode", "P2-CP05");
        query.Should().Contain("plcName", "正极模切05");
        query.Should().Contain("startTime", "2026-04-20T00:00:00.0000000Z");
        query.Should().Contain("endTime", "2026-04-21T00:00:00.0000000Z");
        query.Should().Contain("barcode", "CP-CLIP-001");
        query.Should().Contain("result", "OK");
        query.Should().Contain("fieldMode", "full");
        query.Should().Contain("maxRows", "40");
        query.Should().NotContainKey("clientCode");
        AssertNoLegacyParameters(query);
    }

    [Fact]
    public async Task Client_ShouldMapProductionRecordPayloadIntoSchemaFields()
    {
        using var httpClient = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(CreateEnvelope(
                new[]
                {
                    new
                    {
                        recordId = "dddddddd-dddd-dddd-dddd-dddddddddddd",
                        typeKey = "cp",
                        typeName = "正极模切",
                        deviceId = DeviceId,
                        deviceName = "正极模切客户端",
                        barcode = "CP-CLIP-001",
                        result = "OK",
                        completedAt = "2026-04-20T01:02:03Z",
                        receivedAt = (string?)null,
                        fields = new
                        {
                            plcCode = "P2-CP05",
                            plcName = "正极模切05",
                            startTime = "2026-04-20T00:58:03Z",
                            punchingQuantity = 123,
                            punchingSpeed = 1.25m
                        },
                        fieldSchema = new[]
                        {
                            new { key = "plcCode", label = "PLC 编码", type = "string", unit = (string?)null, precision = (int?)null, required = true },
                            new { key = "plcName", label = "PLC 名称", type = "string", unit = (string?)null, precision = (int?)null, required = true },
                            new { key = "startTime", label = "开始时间", type = "datetime", unit = (string?)null, precision = (int?)null, required = true },
                            new { key = "punchingQuantity", label = "冲切数量", type = "integer", unit = (string?)null, precision = (int?)null, required = true },
                            new { key = "punchingSpeed", label = "冲切速度", type = "number", unit = (string?)null, precision = (int?)2, required = true }
                        }
                    }
                },
                rowCount: 1,
                source: "production_records"))
        }));
        var client = CreateClient(httpClient);

        var result = await client.GetProductionRecordsAsync(new CloudAiReadQuery(
            "生产记录",
            [
                new CloudAiReadFilter("deviceId", "eq", DeviceId),
                new CloudAiReadFilter("preset", "eq", "last_24h")
            ],
            null,
            "occurredAt",
            true,
            20));

        result.Items.Should().ContainSingle();
        result.Items[0].TypeKey.Should().Be("cp");
        result.Items[0].DeviceName.Should().Be("正极模切客户端");
        result.Items[0].Fields["plcCode"].Should().Be("P2-CP05");
        result.Items[0].Fields["plcName"].Should().Be("正极模切05");
        result.Items[0].Fields["punchingQuantity"].Should().Be(123L);
        result.Items[0].Fields["punchingSpeed"].Should().Be(1.25m);
        result.Items[0].FieldSchema.Should().HaveCount(5);
        result.Items[0].FieldSchema[0].Key.Should().Be("plcCode");
        result.Rows[0].Should().NotContainKey("processName");
        result.Rows[0].Should().NotContainKey("stationName");
        result.Rows[0]["fields"].Should().BeAssignableTo<IReadOnlyDictionary<string, object?>>();
        result.Rows[0]["fieldSchema"].Should().BeAssignableTo<IReadOnlyList<CloudAiReadProductionFieldSchemaDto>>();
    }

    [Fact]
    public async Task SemanticQuery_ShouldCallGenericProductionRecordApiForChineseCpPlcQuestion()
    {
        HttpRequestMessage? capturedRequest = null;
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(CreateEnvelope(
                    new[]
                    {
                        new
                        {
                            recordId = "dddddddd-dddd-dddd-dddd-dddddddddddd",
                            typeKey = "cp",
                            typeName = "正极模切",
                            deviceId = DeviceId,
                            deviceName = "正极模切客户端",
                            barcode = "CP-CLIP-005",
                            result = "OK",
                            completedAt = "2026-07-24T01:02:03Z",
                            receivedAt = "2026-07-24T01:02:04Z",
                            fields = new
                            {
                                plcName = "正极模切05",
                                punchingQuantity = 123,
                                punchingSpeed = 1.25m
                            },
                            fieldSchema = new[]
                            {
                                new { key = "plcName", label = "PLC 名称", type = "string", unit = (string?)null, precision = (int?)null, required = true },
                                new { key = "punchingQuantity", label = "冲切数量", type = "integer", unit = (string?)null, precision = (int?)null, required = true },
                                new { key = "punchingSpeed", label = "冲切速度", type = "number", unit = (string?)null, precision = (int?)2, required = true }
                            }
                        }
                    },
                    rowCount: 1,
                    source: "production_records"))
            };
        }));
        var definitions = new SemanticDefinitionCatalog();
        var planner = new SemanticQueryPlanner(
            new SemanticQuerySchemaRegistry(definitions),
            definitions);
        var planning = planner.Plan(
            "Analysis.ProductionData.ByDevice",
            "查询今天正极模切05的弹夹、冲切数量和速度");
        planning.IsSuccess.Should().BeTrue(planning.ErrorMessage);
        var client = CreateClient(httpClient);

        var result = await client.QuerySemanticAsync(planning.Plan!);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.AbsolutePath.Should().Be("/api/v1/ai/read/production-records");
        var query = ParseQuery(capturedRequest.RequestUri);
        query.Should().Contain("typeKey", "cp");
        query.Should().Contain("plcName", "正极模切05");
        query.Should().Contain("preset", "today");
        query.Should().NotContainKey("clientCode");
        result.Rows.Should().ContainSingle();
        result.Rows[0]["deviceName"].Should().Be("正极模切客户端");
        result.Rows[0]["barcode"].Should().Be("CP-CLIP-005");
        var fields = result.Rows[0]["fields"].Should()
            .BeAssignableTo<IReadOnlyDictionary<string, object?>>().Subject;
        fields["plcName"].Should().Be("正极模切05");
        fields["punchingQuantity"].Should().Be(123L);
        fields["punchingSpeed"].Should().Be(1.25m);
    }

    [Fact]
    public async Task Client_ShouldNotSendHttpWhenCapacityRequiredParametersAreMissing()
    {
        var sendCount = 0;
        using var httpClient = new HttpClient(new StubHandler(_ =>
        {
            sendCount++;
            return CreateOkItemsResponse();
        }));
        var client = CreateClient(httpClient);

        var act = () => client.GetCapacitySummaryAsync(new CloudAiReadQuery(
            null,
            [],
            CreateRange("2026-04-20T00:00:00Z", "2026-04-21T00:00:00Z"),
            null,
            false,
            20));

        var exception = await act.Should().ThrowAsync<CloudAiReadException>();
        exception.Which.Code.Should().Be(CloudAiReadProblemCodes.MissingRequiredParameter);
        exception.Which.Message.Should().Contain("设备 ID");
        sendCount.Should().Be(0);
    }

    [Fact]
    public async Task Client_ShouldNotSendHttpWhenDeviceCodeIsProvidedWithoutDeviceId()
    {
        var sendCount = 0;
        using var httpClient = new HttpClient(new StubHandler(_ =>
        {
            sendCount++;
            return CreateOkItemsResponse();
        }));
        var client = CreateClient(httpClient);

        var act = () => client.GetCapacitySummaryAsync(new CloudAiReadQuery(
            null,
            [new CloudAiReadFilter("deviceCode", "eq", "DEV-001")],
            CreateRange("2026-04-20T00:00:00Z", "2026-04-21T00:00:00Z"),
            null,
            false,
            20));

        var exception = await act.Should().ThrowAsync<CloudAiReadException>();
        exception.Which.Code.Should().Be(CloudAiReadProblemCodes.MissingRequiredParameter);
        exception.Which.Message.Should().Be("Cloud AiRead operation 'CapacitySummary' received a duplicate or unsupported typed filter.");
        exception.Which.Message.Should().NotContain("DEV-001");
        sendCount.Should().Be(0);
    }

    [Fact]
    public async Task Client_ShouldNotSendHttpWhenDeviceLogTimeRangeIsMissing()
    {
        var sendCount = 0;
        using var httpClient = new HttpClient(new StubHandler(_ =>
        {
            sendCount++;
            return CreateOkItemsResponse();
        }));
        var client = CreateClient(httpClient);

        var act = () => client.GetDeviceLogsAsync(new CloudAiReadQuery(
            null,
            [new CloudAiReadFilter("deviceId", "eq", DeviceId)],
            null,
            null,
            false,
            20));

        var exception = await act.Should().ThrowAsync<CloudAiReadException>();
        exception.Which.Code.Should().Be(CloudAiReadProblemCodes.MissingRequiredParameter);
        exception.Which.Message.Should().Contain("时间");
        sendCount.Should().Be(0);
    }

    [Fact]
    public async Task Client_ShouldNotSendHttpWhenProductionRecordTimeRangeIsMissing()
    {
        var sendCount = 0;
        using var httpClient = new HttpClient(new StubHandler(_ =>
        {
            sendCount++;
            return CreateOkItemsResponse();
        }));
        var client = CreateClient(httpClient);

        var act = () => client.GetProductionRecordsAsync(new CloudAiReadQuery(
            null,
            [new CloudAiReadFilter("deviceId", "eq", DeviceId), new CloudAiReadFilter("barcode", "eq", "CELL-001")],
            null,
            null,
            false,
            20));

        var exception = await act.Should().ThrowAsync<CloudAiReadException>();
        exception.Which.Code.Should().Be(CloudAiReadProblemCodes.MissingRequiredParameter);
        exception.Which.Message.Should().Contain("时间");
        sendCount.Should().Be(0);
    }

    [Fact]
    public async Task Client_ShouldNotSendHttpWhenProductionRecordScopeIsMissing()
    {
        var sendCount = 0;
        using var httpClient = new HttpClient(new StubHandler(_ =>
        {
            sendCount++;
            return CreateOkItemsResponse();
        }));
        var client = CreateClient(httpClient);

        var act = () => client.GetProductionRecordsAsync(new CloudAiReadQuery(
            null,
            [new CloudAiReadFilter("barcode", "eq", "CELL-001")],
            CreateRange("2026-04-20T00:00:00Z", "2026-04-21T00:00:00Z"),
            null,
            false,
            20));

        var exception = await act.Should().ThrowAsync<CloudAiReadException>();
        exception.Which.Code.Should().Be(CloudAiReadProblemCodes.MissingRequiredParameter);
        exception.Which.Message.Should().Contain("typeKey");
        sendCount.Should().Be(0);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, CloudAiReadProblemCodes.InvalidRequest)]
    [InlineData(HttpStatusCode.Unauthorized, CloudAiReadProblemCodes.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, CloudAiReadProblemCodes.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, CloudAiReadProblemCodes.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests, CloudAiReadProblemCodes.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, CloudAiReadProblemCodes.Unavailable)]
    public async Task Client_ShouldReturnClearErrorsForTokenPermissionAndScopeProblems(
        HttpStatusCode statusCode,
        string expectedCode)
    {
        using var httpClient = new HttpClient(new StubHandler(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent("{\"detail\":\"secret prompt token SQL must stay hidden\"}")
        }));
        var client = CreateClient(httpClient);

        var act = () => client.GetDeviceLogsAsync(new CloudAiReadQuery(
            "查看日志",
            [new CloudAiReadFilter("deviceId", "eq", DeviceId)],
            CreateRange("2026-04-20T00:00:00Z", "2026-04-21T00:00:00Z"),
            "occurredAt",
            true,
            10));

        var exception = await act.Should().ThrowAsync<CloudAiReadException>();
        exception.Which.Code.Should().Be(expectedCode);
        exception.Which.Message.Should().NotContain("secret");
        exception.Which.Message.Should().NotContain("prompt");
        exception.Which.Message.Should().NotContain("SQL");
    }

    [Theory]
    [InlineData("legacy-data", "{\"data\":[]}")]
    [InlineData("legacy-records", "{\"records\":[]}")]
    [InlineData("legacy-results", "{\"results\":[]}")]
    [InlineData("invalid-as-of", "{\"items\":[],\"asOfUtc\":\"not-a-date\",\"source\":\"devices\",\"queryScope\":\"\",\"rowCount\":0,\"truncated\":false}")]
    [InlineData("negative-row-count", "{\"items\":[],\"asOfUtc\":\"2026-07-10T01:02:03Z\",\"source\":\"devices\",\"queryScope\":\"\",\"rowCount\":-1,\"truncated\":false}")]
    [InlineData("legacy-truncated-name", "{\"items\":[],\"asOfUtc\":\"2026-07-10T01:02:03Z\",\"source\":\"devices\",\"queryScope\":\"\",\"rowCount\":0,\"isTruncated\":false}")]
    public async Task Client_ShouldRejectLegacyOrMalformedProviderEnvelope(string caseId, string payload)
    {
        _ = caseId;
        using var httpClient = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload)
        }));
        var client = CreateClient(httpClient);

        var act = () => client.GetDevicesAsync(new CloudAiReadQuery(
            null,
            [],
            null,
            null,
            false,
            10));

        var exception = await act.Should().ThrowAsync<CloudAiReadException>();
        exception.Which.Code.Should().Be(CloudAiReadProblemCodes.Unavailable);
        exception.Which.Message.Should().Contain("invalid provider contract");

        if (caseId == "legacy-data")
        {
            await AssertMalformedProviderEnvelopeMetadataAsync();
            await AssertMalformedProviderItemMatrixAsync();
        }
    }

    [Fact]
    public void CloudIdentityStatusOptions_ShouldRejectNonIdentityStatusPath()
    {
        var options = new CloudIdentityStatusOptions
        {
            Enabled = true,
            BaseUrl = "https://cloud.example.com",
            StatusEndpointPath = "/api/v1/users/{cloudUserId}/status",
            ServiceAccountToken = "token"
        };

        var act = () => options.EnsureValid();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*CloudIdentityStatus:StatusEndpointPath*");
    }

    private static CloudAiReadClient CreateClient(HttpClient httpClient)
    {
        return new CloudAiReadClient(
            httpClient,
            Options.Create(new CloudAiReadOptions
            {
                Enabled = true,
                BaseUrl = "https://cloud.example.com",
                ServiceAccountToken = "service-token"
            }),
            NullLogger<CloudAiReadClient>.Instance);
    }

    private static CloudAiReadTimeRange CreateRange(string start, string end)
    {
        return new CloudAiReadTimeRange(
            "occurredAt",
            DateTimeOffset.Parse(start),
            DateTimeOffset.Parse(end));
    }

    private static HttpResponseMessage CreateOkItemsResponse()
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(CreateEnvelope(Array.Empty<object>()))
        };
    }

    private static object CreateEnvelope(
        object items,
        int rowCount = 0,
        bool truncated = false,
        string source = "test_source",
        string queryScope = "test=present",
        string? nextCursor = null)
    {
        return new
        {
            items,
            asOfUtc = ProviderAsOfUtc,
            source,
            queryScope,
            rowCount,
            truncated,
            nextCursor
        };
    }

    private static async Task AssertMalformedProviderItemMatrixAsync()
    {
        var cases = new[]
        {
            new MalformedEndpointCase(
                "devices",
                $$"""{"id":"{{DeviceId}}","deviceCode":"DEV-001","deviceName":"Device 1","processId":"{{ProcessId}}"}""",
                "deviceCode",
                "id",
                "42",
                static async client =>
                {
                    _ = await client.GetDevicesAsync(CreateUnscopedQuery());
                }),
            new MalformedEndpointCase(
                "processes",
                $$"""{"id":"{{ProcessId}}","processCode":"PROC-001","processName":"Process 1"}""",
                "processName",
                "processCode",
                "false",
                static async client =>
                {
                    _ = await client.GetProcessesAsync(CreateUnscopedQuery());
                }),
            new MalformedEndpointCase(
                "client-releases",
                $$"""{"id":"{{ReleaseId}}","componentKind":"EdgeHost","componentKey":"edge-host","displayName":"Edge Host","channel":"stable","targetRuntime":"win-x64","version":"1.0.0","status":"Published","releaseNotes":null,"createdAtUtc":"2026-07-10T01:02:03Z","publishedAtUtc":null,"deletedAtUtc":null}""",
                "status",
                "createdAtUtc",
                "123",
                static async client =>
                {
                    _ = await client.GetClientReleasesAsync(CreateUnscopedQuery());
                }),
            new MalformedEndpointCase(
                "device-client-states",
                $$"""{"deviceId":"{{DeviceId}}","deviceName":"Device 1","clientCode":"EDGE-001","primaryIp":null,"channel":null,"hostVersion":null,"hostApiVersion":null,"versionReportedAtUtc":null,"versionReceivedAtUtc":null,"softwareStatus":"Running","runtimeStatus":null,"runtimeStartedAtUtc":null,"lastRuntimeHeartbeatAtUtc":null,"updatedAtUtc":null}""",
                "softwareStatus",
                "softwareStatus",
                "[]",
                static async client =>
                {
                    _ = await client.GetDeviceClientStatesAsync(CreateUnscopedQuery());
                }),
            new MalformedEndpointCase(
                "capacity-summary",
                """{"date":"2026-07-10","totalCount":10,"okCount":9,"ngCount":1,"dayShiftTotal":6,"nightShiftTotal":4}""",
                "ngCount",
                "totalCount",
                "\"opaque-provider-item-secret-number\"",
                static async client =>
                {
                    _ = await client.GetCapacitySummaryAsync(CreateCapacitySummaryQuery());
                }),
            new MalformedEndpointCase(
                "capacity-hourly",
                """{"time":"2026-07-10T01:00:00Z","date":"2026-07-10","hour":1,"minute":0,"timeLabel":"01:00","shiftCode":"DAY","totalCount":10,"okCount":9,"ngCount":1,"okRate":0.9}""",
                "shiftCode",
                "hour",
                "\"opaque-provider-item-secret-integer\"",
                static async client =>
                {
                    _ = await client.GetCapacityHourlyAsync(CreateCapacityHourlyQuery());
                }),
            new MalformedEndpointCase(
                "device-logs",
                $$"""{"id":"dddddddd-dddd-dddd-dddd-dddddddddddd","deviceId":"{{DeviceId}}","deviceName":"Device 1","level":"WARN","message":"warning","logTime":"2026-07-10T01:00:00Z","receivedAt":"2026-07-10T01:00:01Z"}""",
                "message",
                "logTime",
                "{}",
                static async client =>
                {
                    _ = await client.GetDeviceLogsAsync(CreateDeviceLogQuery());
                }),
            new MalformedEndpointCase(
                "production-records",
                $$"""{"recordId":"eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee","typeKey":"cp","typeName":"正极模切","deviceId":"{{DeviceId}}","deviceName":"正极模切客户端","barcode":null,"result":null,"completedAt":null,"receivedAt":null,"fields":{},"fieldSchema":[{"key":"pressure","label":"Pressure","type":"number","unit":null,"precision":1,"required":true}]}""",
                "typeName",
                "fields",
                "[]",
                static async client =>
                {
                    _ = await client.GetProductionRecordsAsync(CreateProductionRecordQuery());
                })
        };

        foreach (var endpoint in cases)
        {
            await AssertInvalidProviderItemAsync(
                endpoint.Invoke,
                "\"opaque-provider-item-secret-non-object\"");
            await AssertInvalidProviderItemAsync(
                endpoint.Invoke,
                RemoveProperty(endpoint.ValidItemJson, endpoint.RequiredProperty));
            await AssertInvalidProviderItemAsync(
                endpoint.Invoke,
                ReplaceProperty(endpoint.ValidItemJson, endpoint.RequiredProperty, "null"));
            await AssertInvalidProviderItemAsync(
                endpoint.Invoke,
                ReplaceProperty(
                    endpoint.ValidItemJson,
                    endpoint.WrongTypeProperty,
                    endpoint.WrongTypeJson));
        }

        var devices = cases.Single(endpoint => endpoint.Name == "devices");
        await AssertInvalidProviderItemAsync(
            devices.Invoke,
            ReplaceProperty(devices.ValidItemJson, "id", "\"not-a-guid\""));
        await AssertInvalidProviderItemAsync(
            devices.Invoke,
            AddProperty(
                devices.ValidItemJson,
                "unknownProviderField",
                "\"opaque-provider-item-secret-unknown\""));
        await AssertInvalidProviderItemAsync(
            devices.Invoke,
            AppendRawProperty(devices.ValidItemJson, "deviceCode", "\"duplicate\""));
        await AssertInvalidProviderItemAsync(
            devices.Invoke,
            AppendRawProperty(devices.ValidItemJson, "DeviceCode", "\"case-collision\""));

        var releases = cases.Single(endpoint => endpoint.Name == "client-releases");
        await AssertInvalidProviderItemAsync(
            releases.Invoke,
            RemoveProperty(releases.ValidItemJson, "releaseNotes"));
        await AssertInvalidProviderItemAsync(
            releases.Invoke,
            ReplaceProperty(releases.ValidItemJson, "createdAtUtc", "\"2026-99-99T25:61:61Z\""));

        var capacitySummary = cases.Single(endpoint => endpoint.Name == "capacity-summary");
        await AssertInvalidProviderItemAsync(
            capacitySummary.Invoke,
            ReplaceProperty(capacitySummary.ValidItemJson, "date", "\"2026-02-30\""));
        await AssertInvalidProviderItemAsync(
            capacitySummary.Invoke,
            ReplaceProperty(capacitySummary.ValidItemJson, "totalCount", "1.5"));
        await AssertInvalidProviderItemAsync(
            capacitySummary.Invoke,
            ReplaceProperty(capacitySummary.ValidItemJson, "totalCount", "2147483648"));

        var capacityHourly = cases.Single(endpoint => endpoint.Name == "capacity-hourly");
        await AssertInvalidProviderItemAsync(
            capacityHourly.Invoke,
            ReplaceProperty(capacityHourly.ValidItemJson, "okRate", "1e1000"));

        var production = cases.Single(endpoint => endpoint.Name == "production-records");
        var deviceClientStates = cases.Single(endpoint => endpoint.Name == "device-client-states");
        await AssertInvalidProviderItemAsync(
            deviceClientStates.Invoke,
            RemoveProperty(deviceClientStates.ValidItemJson, "primaryIp"));
        await AssertInvalidProviderItemAsync(
            production.Invoke,
            RemoveProperty(production.ValidItemJson, "barcode"));
        await AssertInvalidProviderItemAsync(
            production.Invoke,
            ReplaceProperty(production.ValidItemJson, "fields", "{\"pressure\":{\"nested\":1}}"));
        await AssertInvalidProviderItemAsync(
            production.Invoke,
            production.ValidItemJson.Replace(
                "\"fields\":{}",
                "\"fields\":{\"pressure\":1,\"pressure\":2}",
                StringComparison.Ordinal));
        await AssertInvalidProviderItemAsync(
            production.Invoke,
            production.ValidItemJson.Replace(
                "\"fields\":{}",
                "\"fields\":{\"pressure\":1,\"Pressure\":2}",
                StringComparison.Ordinal));
        await AssertInvalidProviderItemAsync(
            production.Invoke,
            ReplaceProperty(production.ValidItemJson, "fieldSchema", "[42]"));
        await AssertInvalidProviderItemAsync(
            production.Invoke,
            ReplaceProperty(production.ValidItemJson, "fields", "{\"unknownField\":1}"));
        await AssertInvalidProviderItemAsync(
            production.Invoke,
            ReplaceProperty(production.ValidItemJson, "fields", "{\"pressure\":null}"));

        var duplicateSchemaKey = """
            [
              {"key":"pressure","label":"Pressure","type":"number","unit":null,"precision":1,"required":true},
              {"key":"pressure","label":"Pressure Copy","type":"number","unit":null,"precision":1,"required":false}
            ]
            """;
        await AssertInvalidProviderItemAsync(
            production.Invoke,
            ReplaceProperty(production.ValidItemJson, "fieldSchema", duplicateSchemaKey));
        await AssertInvalidProviderItemAsync(
            production.Invoke,
            ReplaceProperty(
                production.ValidItemJson,
                "fieldSchema",
                duplicateSchemaKey.Replace("\"key\":\"pressure\",\"label\":\"Pressure Copy\"", "\"key\":\"Pressure\",\"label\":\"Pressure Copy\"", StringComparison.Ordinal)));

        foreach (var invalidTypedRecord in new[]
                 {
                     WithProductionFieldContract(production.ValidItemJson, "integer", "1.5"),
                     WithProductionFieldContract(production.ValidItemJson, "integer", "1e1000"),
                     WithProductionFieldContract(production.ValidItemJson, "number", "1e1000"),
                     WithProductionFieldContract(production.ValidItemJson, "boolean", "\"true\""),
                     WithProductionFieldContract(production.ValidItemJson, "string", "true"),
                     WithProductionFieldContract(production.ValidItemJson, "datetime", "\"not-a-date\""),
                     WithProductionFieldContract(production.ValidItemJson, "enum", "42"),
                     WithProductionFieldContract(production.ValidItemJson, "date", "\"2026-07-10\"")
                 })
        {
            await AssertInvalidProviderItemAsync(production.Invoke, invalidTypedRecord);
        }

        foreach (var invalidFieldKey in new[] { "pressure.value", "pressure_value", "Pressure" })
        {
            await AssertInvalidProviderItemAsync(
                production.Invoke,
                WithProductionFieldKey(production.ValidItemJson, invalidFieldKey));
        }

        var productionNestedMissing = JsonNode.Parse(production.ValidItemJson)!.AsObject();
        productionNestedMissing["fieldSchema"]!.AsArray()[0]!.AsObject()
            .Remove("required")
            .Should().BeTrue();
        await AssertInvalidProviderItemAsync(
            production.Invoke,
            productionNestedMissing.ToJsonString());

        var productionNestedNullableMissing = JsonNode.Parse(production.ValidItemJson)!.AsObject();
        productionNestedNullableMissing["fieldSchema"]!.AsArray()[0]!.AsObject()
            .Remove("unit")
            .Should().BeTrue();
        await AssertInvalidProviderItemAsync(
            production.Invoke,
            productionNestedNullableMissing.ToJsonString());

        var productionNestedWrongType = JsonNode.Parse(production.ValidItemJson)!.AsObject();
        productionNestedWrongType["fieldSchema"]!.AsArray()[0]!.AsObject()["required"] =
            "opaque-provider-item-secret-required";
        await AssertInvalidProviderItemAsync(
            production.Invoke,
            productionNestedWrongType.ToJsonString());

        var productionNestedNull = JsonNode.Parse(production.ValidItemJson)!.AsObject();
        productionNestedNull["fieldSchema"]!.AsArray()[0]!.AsObject()["required"] = null;
        await AssertInvalidProviderItemAsync(
            production.Invoke,
            productionNestedNull.ToJsonString());

        await AssertInvalidProviderItemAsync(
            production.Invoke,
            production.ValidItemJson.Replace(
                "\"required\":true",
                "\"required\":true,\"required\":false",
                StringComparison.Ordinal));
        await AssertInvalidProviderItemAsync(
            production.Invoke,
            production.ValidItemJson.Replace(
                "\"required\":true",
                "\"required\":true,\"Required\":false",
                StringComparison.Ordinal));

        await AssertInvalidProviderItemAsync(
            static async client =>
            {
                _ = await client.GetDevicesAsync(
                    new CloudAiReadQuery(null, [], null, null, false, 1));
            },
            devices.ValidItemJson,
            "\"opaque-provider-item-secret-after-limit\"");
    }

    private static async Task AssertMalformedProviderEnvelopeMetadataAsync()
    {
        var payloads = new[]
        {
            $$"""{"items":[],"asOfUtc":"{{ProviderAsOfUtc}}","source":"devices","queryScope":"","rowCount":0,"truncated":false}""",
            $$"""{"items":[],"asOfUtc":"{{ProviderAsOfUtc}}","source":"devices","queryScope":"","rowCount":1,"truncated":false,"nextCursor":null}""",
            $$"""{"items":[],"asOfUtc":"{{ProviderAsOfUtc}}","source":"devices","queryScope":"","rowCount":0,"truncated":false,"nextCursor":false}"""
        };

        foreach (var payload in payloads)
        {
            using var httpClient = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            }));
            var client = CreateClient(httpClient);

            var action = () => client.GetDevicesAsync(CreateUnscopedQuery());

            var exception = await action.Should().ThrowAsync<CloudAiReadException>();
            exception.Which.Code.Should().Be(CloudAiReadProblemCodes.Unavailable);
            exception.Which.Message.Should().Be(
                "Cloud AiRead endpoint returned an invalid provider contract.");
        }
    }

    private static async Task AssertInvalidProviderItemAsync(
        Func<CloudAiReadClient, Task> invoke,
        params string[] itemJsons)
    {
        var payload = $$"""
            {"items":[{{string.Join(',', itemJsons)}}],"asOfUtc":"{{ProviderAsOfUtc}}","source":"test_source","queryScope":"test=present","rowCount":{{itemJsons.Length}},"truncated":false,"nextCursor":null}
            """;
        using var httpClient = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        }));
        var client = CreateClient(httpClient);

        var action = () => invoke(client);

        var exception = await action.Should().ThrowAsync<CloudAiReadException>();
        exception.Which.Code.Should().Be(CloudAiReadProblemCodes.Unavailable);
        exception.Which.Message.Should().Be(
            "Cloud AiRead endpoint returned an invalid provider contract.");
        exception.Which.Message.Should().NotContain("opaque-provider-item-secret");
    }

    private static string RemoveProperty(string itemJson, string propertyName)
    {
        var item = JsonNode.Parse(itemJson)!.AsObject();
        item.Remove(propertyName).Should().BeTrue();
        return item.ToJsonString();
    }

    private static string ReplaceProperty(
        string itemJson,
        string propertyName,
        string replacementJson)
    {
        var item = JsonNode.Parse(itemJson)!.AsObject();
        item.ContainsKey(propertyName).Should().BeTrue();
        item[propertyName] = JsonNode.Parse(replacementJson);
        return item.ToJsonString();
    }

    private static string AddProperty(
        string itemJson,
        string propertyName,
        string propertyJson)
    {
        var item = JsonNode.Parse(itemJson)!.AsObject();
        item.ContainsKey(propertyName).Should().BeFalse();
        item[propertyName] = JsonNode.Parse(propertyJson);
        return item.ToJsonString();
    }

    private static string AppendRawProperty(
        string itemJson,
        string propertyName,
        string propertyJson)
    {
        itemJson.Should().EndWith("}");
        return $"{itemJson[..^1]},\"{propertyName}\":{propertyJson}}}";
    }

    private static string WithProductionFieldContract(
        string itemJson,
        string fieldType,
        string fieldValueJson)
    {
        var withType = itemJson.Replace(
            "\"type\":\"number\"",
            $"\"type\":\"{fieldType}\"",
            StringComparison.Ordinal);
        return withType.Replace(
            "\"fields\":{}",
            $"\"fields\":{{\"pressure\":{fieldValueJson}}}",
            StringComparison.Ordinal);
    }

    private static string WithProductionFieldKey(string itemJson, string fieldKey)
    {
        var withSchemaKey = itemJson.Replace(
            "\"key\":\"pressure\"",
            $"\"key\":\"{fieldKey}\"",
            StringComparison.Ordinal);
        return withSchemaKey.Replace(
            "\"fields\":{}",
            $"\"fields\":{{\"{fieldKey}\":1}}",
            StringComparison.Ordinal);
    }

    private static CloudAiReadQuery CreateUnscopedQuery()
    {
        return new CloudAiReadQuery(null, [], null, null, false, 10);
    }

    private static CloudAiReadQuery CreateCapacitySummaryQuery()
    {
        return new CloudAiReadQuery(
            null,
            [new CloudAiReadFilter("deviceId", "eq", DeviceId)],
            CreateRange("2026-07-10T00:00:00Z", "2026-07-10T23:59:59Z"),
            null,
            false,
            10);
    }

    private static CloudAiReadQuery CreateCapacityHourlyQuery()
    {
        return new CloudAiReadQuery(
            null,
            [
                new CloudAiReadFilter("deviceId", "eq", DeviceId),
                new CloudAiReadFilter("date", "eq", "2026-07-10")
            ],
            null,
            null,
            false,
            10);
    }

    private static CloudAiReadQuery CreateDeviceLogQuery()
    {
        return new CloudAiReadQuery(
            null,
            [
                new CloudAiReadFilter("deviceId", "eq", DeviceId),
                new CloudAiReadFilter("preset", "eq", "last_24h")
            ],
            null,
            null,
            false,
            10);
    }

    private static CloudAiReadQuery CreateProductionRecordQuery()
    {
        return new CloudAiReadQuery(
            null,
            [
                new CloudAiReadFilter("deviceId", "eq", DeviceId),
                new CloudAiReadFilter("preset", "eq", "last_24h")
            ],
            null,
            null,
            false,
            10);
    }

    private static void AssertRequiredReferenceProperties<T>(params string[] propertyNames)
    {
        var nullability = new NullabilityInfoContext();
        foreach (var propertyName in propertyNames)
        {
            var property = GetProperty<T>(propertyName);
            property.PropertyType.IsValueType.Should().BeFalse();
            nullability.Create(property).ReadState.Should().Be(NullabilityState.NotNull);
        }
    }

    private static void AssertOptionalReferenceProperties<T>(params string[] propertyNames)
    {
        var nullability = new NullabilityInfoContext();
        foreach (var propertyName in propertyNames)
        {
            var property = GetProperty<T>(propertyName);
            property.PropertyType.IsValueType.Should().BeFalse();
            nullability.Create(property).ReadState.Should().Be(NullabilityState.Nullable);
        }
    }

    private static void AssertRequiredValueProperties<T>(params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var property = GetProperty<T>(propertyName);
            property.PropertyType.IsValueType.Should().BeTrue();
            Nullable.GetUnderlyingType(property.PropertyType).Should().BeNull();
        }
    }

    private static void AssertOptionalValueProperties<T>(params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var property = GetProperty<T>(propertyName);
            property.PropertyType.IsValueType.Should().BeTrue();
            Nullable.GetUnderlyingType(property.PropertyType).Should().NotBeNull();
        }
    }

    private static void AssertPropertyTypes<T>(
        params (string PropertyName, Type ExpectedType)[] properties)
    {
        foreach (var property in properties)
        {
            GetProperty<T>(property.PropertyName).PropertyType.Should().Be(property.ExpectedType);
        }
    }

    private static PropertyInfo GetProperty<T>(string propertyName)
    {
        return typeof(T).GetProperty(propertyName)
               ?? throw new InvalidOperationException(
                   $"{typeof(T).Name} is missing property '{propertyName}'.");
    }

    private static Dictionary<string, string> ParseQuery(Uri uri)
    {
        return uri.Query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                part => Uri.UnescapeDataString(part[0]),
                part => part.Length == 2 ? Uri.UnescapeDataString(part[1]) : string.Empty,
                StringComparer.OrdinalIgnoreCase);
    }

    private static void AssertNoLegacyParameters(Dictionary<string, string> query)
    {
        foreach (var legacyParameter in new[]
                 {
                     "limit",
                     "queryText",
                     "from",
                     "to",
                     "timeField",
                     "sortField",
                     "sortDirection"
                 })
        {
            query.Should().NotContainKey(legacyParameter);
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request));
        }
    }

    private sealed record MalformedEndpointCase(
        string Name,
        string ValidItemJson,
        string RequiredProperty,
        string WrongTypeProperty,
        string WrongTypeJson,
        Func<CloudAiReadClient, Task> Invoke);
}
