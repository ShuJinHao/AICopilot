using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AICopilot.Infrastructure.CloudRead;
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
                        deviceName = "叠片一号",
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
            CreateRange("2026-04-20T00:00:00Z", "2026-04-21T00:00:00Z"),
            "deviceCode",
            false,
            20));

        result.SourceLabel.Should().Contain("Cloud AiRead API");
        result.IsTruncated.Should().BeFalse();
        result.Items.Should().ContainSingle();
        result.Items[0].DeviceCode.Should().Be("DEV-001");
        result.Items[0].ProcessId.Should().Be(ProcessId);
        result.AsOfUtc.Should().Be(DateTimeOffset.Parse(ProviderAsOfUtc));
        result.ProviderSource.Should().Be("devices");
        result.QueryScope.Should().Be("test=present");
        result.RowCount.Should().Be(1);
        result.Rows[0]["deviceName"].Should().Be("叠片一号");
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
                new CloudAiReadFilter("deviceName", "contains", "叠片")
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
        query.Should().Contain("keyword", "叠片");
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
                            publishedAtUtc = "2026-05-11T01:02:03Z"
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
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(CreateEnvelope(
                    new object[]
                    {
                        new { id = SecondProcessId, processCode = "CUT-OLD", processName = "旧模切" },
                        new { id = ProcessId, processCode = "CUT", processName = "模切" }
                    },
                    rowCount: 2,
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

        var result = await client.QuerySemanticAsync(plan);

        capturedRequest!.RequestUri!.AbsolutePath.Should().Be("/api/v1/ai/read/processes");
        ParseQuery(capturedRequest.RequestUri).Should().Contain("maxRows", "100");
        result.Rows.Should().ContainSingle().Which["processCode"].Should().Be("CUT");
        result.Limit.Should().Be(1);
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
                            softwareStatus = "Running",
                            runtimeStatus = "Running",
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
        result.Items[0].LastRuntimeHeartbeatAtUtc.Should().Be(DateTimeOffset.Parse("2026-05-11T01:02:03Z"));
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

        await client.GetCapacitySummaryAsync(new CloudAiReadQuery(
            "不要作为 queryText 发送",
            [
                new CloudAiReadFilter("deviceId", "eq", DeviceId),
                new CloudAiReadFilter("plcName", "eq", "PLC-A")
            ],
            CreateRange("2026-04-20T01:02:03Z", "2026-04-21T04:05:06Z"),
            "occurredAt",
            true,
            50));

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.AbsolutePath.Should().Be("/api/v1/ai/read/capacity/summary");
        var query = ParseQuery(capturedRequest.RequestUri);
        query.Should().Contain("deviceId", DeviceId);
        query.Should().Contain("startDate", "2026-04-20");
        query.Should().Contain("endDate", "2026-04-21");
        query.Should().Contain("plcName", "PLC-A");
        query.Should().Contain("maxRows", "50");
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
        query.Should().Contain("minLevel", "warn");
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
                                deviceName = "叠片一号",
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
                            deviceName = "叠片一号",
                            clientCode = "DEV-001",
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
        result.Rows[0]["lastRuntimeHeartbeatAtUtc"].Should().Be(DateTimeOffset.Parse("2026-07-10T01:02:00Z"));
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
                        new { deviceId = DeviceId, deviceName = "设备一", clientCode = "EDGE-001", softwareStatus = "Running", runtimeStatus = "Running", lastRuntimeHeartbeatAtUtc = "2026-07-10T01:02:00Z" },
                        new { deviceId = SecondDeviceId, deviceName = "设备二", clientCode = "EDGE-002", softwareStatus = "MissingRuntimeHeartbeat", runtimeStatus = (string?)null, lastRuntimeHeartbeatAtUtc = (string?)null }
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
                new CloudAiReadFilter("typeKey", "eq", "stacking"),
                new CloudAiReadFilter("barcode", "eq", "CELL-001"),
                new CloudAiReadFilter("result", "eq", "Pass"),
                new CloudAiReadFilter("fieldMode", "eq", "full")
            ],
            CreateRange("2026-04-20T00:00:00Z", "2026-04-21T00:00:00Z"),
            "occurredAt",
            true,
            40));

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.AbsolutePath.Should().Be("/api/v1/ai/read/production-records");
        var query = ParseQuery(capturedRequest.RequestUri);
        query.Should().Contain("typeKey", "stacking");
        query.Should().Contain("deviceId", DeviceId);
        query.Should().Contain("startTime", "2026-04-20T00:00:00.0000000Z");
        query.Should().Contain("endTime", "2026-04-21T00:00:00.0000000Z");
        query.Should().Contain("barcode", "CELL-001");
        query.Should().Contain("result", "Pass");
        query.Should().Contain("fieldMode", "full");
        query.Should().Contain("maxRows", "40");
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
                        typeKey = "stacking",
                        typeName = "叠片",
                        deviceId = DeviceId,
                        deviceName = "叠片一号",
                        barcode = "CELL-001",
                        result = "Pass",
                        completedAt = "2026-04-20T01:02:03Z",
                        fields = new { pressure = 12.5m },
                        fieldSchema = new[]
                        {
                            new { key = "pressure", label = "压力", type = "number", unit = "N", precision = 1, required = true }
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
        result.Items[0].TypeKey.Should().Be("stacking");
        result.Items[0].Fields.Should().ContainKey("pressure");
        result.Items[0].FieldSchema.Should().ContainSingle().Which.Key.Should().Be("pressure");
        result.Rows[0].Should().NotContainKey("processName");
        result.Rows[0].Should().NotContainKey("stationName");
        result.Rows[0]["fields"].Should().BeAssignableTo<IReadOnlyDictionary<string, object?>>();
        result.Rows[0]["fieldSchema"].Should().BeAssignableTo<IReadOnlyList<CloudAiReadProductionFieldSchemaDto>>();
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
        exception.Which.Message.Should().Contain("deviceId");
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
}
