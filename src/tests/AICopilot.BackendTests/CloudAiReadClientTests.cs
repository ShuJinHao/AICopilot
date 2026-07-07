using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AICopilot.Infrastructure.CloudRead;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AICopilot.BackendTests;

public sealed class CloudAiReadClientTests
{
    [Theory]
    [InlineData("/api/v1/ai/read/devices")]
    [InlineData("/api/v1/ai/read/processes")]
    [InlineData("/api/v1/ai/read/client-releases")]
    [InlineData("/api/v1/ai/read/device-client-states")]
    [InlineData("/api/v1/ai/read/capacity/summary")]
    [InlineData("/api/v1/ai/read/capacity/hourly")]
    [InlineData("/api/v1/ai/read/device-logs")]
    [InlineData("/api/v1/ai/read/production-records")]
    [InlineData("/api/v1/ai/identity/users/cloud-user-1/status")]
    public void EndpointPolicy_ShouldAllowWhitelistedGetPaths(string path)
    {
        var decision = CloudAiReadEndpointPolicy.Evaluate(HttpMethod.Get, path);

        decision.IsAllowed.Should().BeTrue(decision.Reason);
    }

    [Theory]
    [InlineData("PUT", "/api/v1/ai/read/devices")]
    [InlineData("PATCH", "/api/v1/ai/read/devices")]
    [InlineData("DELETE", "/api/v1/ai/read/device-logs")]
    [InlineData("GET", "/api/v1/devices")]
    [InlineData("GET", "/api/v1/ai/read/pass-stations/stacking")]
    [InlineData("GET", "/api/v1/ai/read/pass-stations/a/b")]
    [InlineData("POST", "/api/v1/ai/read/devices")]
    public void EndpointPolicy_ShouldRejectWriteMethodsAndNonWhitelistedPaths(string method, string path)
    {
        var decision = CloudAiReadEndpointPolicy.Evaluate(new HttpMethod(method), path);

        decision.IsAllowed.Should().BeFalse();
    }

    [Fact]
    public void SemanticSupport_ShouldNotSupportRecipeTarget()
    {
        CloudAiReadSemanticSupport.IsSupported(SemanticQueryTarget.Recipe).Should().BeFalse();
    }

    [Theory]
    [InlineData("/api/v1/ai/read/devices/query")]
    [InlineData("/api/v1/ai/read/devices/create")]
    [InlineData("/api/v1/ai/read/devices/update")]
    [InlineData("/api/v1/devices/query")]
    public void EndpointPolicy_ShouldKeepPostDisabledUnlessExplicitlySafe(string path)
    {
        var defaultDecision = CloudAiReadEndpointPolicy.Evaluate(HttpMethod.Post, path);

        defaultDecision.IsAllowed.Should().BeFalse();

        var explicitDecision = CloudAiReadEndpointPolicy.Evaluate(
            HttpMethod.Post,
            path,
            [path]);

        var shouldAllow = path.Equals("/api/v1/ai/read/devices/query", StringComparison.OrdinalIgnoreCase);
        explicitDecision.IsAllowed.Should().Be(shouldAllow);
    }

    [Fact]
    public async Task Client_ShouldMapDevicePayloadIntoInternalReadonlyDto()
    {
        using var httpClient = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                items = new[]
                {
                    new
                    {
                        deviceId = "device-1",
                        deviceCode = "DEV-001",
                        deviceName = "叠片一号",
                        status = "Running",
                        lineName = "LINE-A",
                        updatedAt = "2026-05-11T01:02:03Z"
                    }
                },
                isTruncated = false
            })
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
        result.Rows[0]["deviceName"].Should().Be("叠片一号");
    }

    [Fact]
    public async Task Client_ShouldSendDeviceQueryWithKeywordAndMaxRowsOnly()
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
            [new CloudAiReadFilter("deviceCode", "eq", "DEV-001")],
            null,
            "deviceCode",
            false,
            20));

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.AbsolutePath.Should().Be("/api/v1/ai/read/devices");
        var query = ParseQuery(capturedRequest.RequestUri);
        query.Should().Contain("keyword", "DEV-001");
        query.Should().Contain("maxRows", "20");
        AssertNoLegacyParameters(query);
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
                Content = JsonContent.Create(new
                {
                    items = new[]
                    {
                        new
                        {
                            id = "process-1",
                            processCode = "CUT",
                            processName = "模切"
                        }
                    },
                    isTruncated = false
                })
            };
        }));
        var client = CreateClient(httpClient);

        var result = await client.GetProcessesAsync(new CloudAiReadQuery(
            "不要作为 queryText 发送",
            [new CloudAiReadFilter("processName", "contains", "模切")],
            null,
            "processName",
            false,
            15));

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.AbsolutePath.Should().Be("/api/v1/ai/read/processes");
        var query = ParseQuery(capturedRequest.RequestUri);
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
                Content = JsonContent.Create(new
                {
                    items = new[]
                    {
                        new
                        {
                            id = "release-1",
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
                    isTruncated = false
                })
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
    public async Task Client_ShouldSendDeviceClientStateQueryWithOfficialParametersOnly()
    {
        HttpRequestMessage? capturedRequest = null;
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    items = new[]
                    {
                        new
                        {
                            deviceId = "device-1",
                            deviceName = "模切一号",
                            clientCode = "EDGE-001",
                            primaryIp = "10.0.0.11",
                            channel = "stable",
                            hostVersion = "1.2.3",
                            hostApiVersion = "2026.07",
                            runtimeStatus = "Running",
                            updatedAtUtc = "2026-05-11T01:02:03Z"
                        }
                    },
                    isTruncated = false
                })
            };
        }));
        var client = CreateClient(httpClient);

        var result = await client.GetDeviceClientStatesAsync(new CloudAiReadQuery(
            "不要作为 queryText 发送",
            [
                new CloudAiReadFilter("deviceId", "eq", "device-1"),
                new CloudAiReadFilter("clientCode", "eq", "EDGE-001")
            ],
            null,
            "updatedAtUtc",
            true,
            10));

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.AbsolutePath.Should().Be("/api/v1/ai/read/device-client-states");
        var query = ParseQuery(capturedRequest.RequestUri);
        query.Should().Contain("deviceId", "device-1");
        query.Should().Contain("keyword", "EDGE-001");
        query.Should().Contain("maxRows", "10");
        AssertNoLegacyParameters(query);
        result.Items.Should().ContainSingle().Which.RuntimeStatus.Should().Be("Running");
        result.Rows[0]["clientCode"].Should().Be("EDGE-001");
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
                new CloudAiReadFilter("deviceId", "eq", "device-1"),
                new CloudAiReadFilter("processName", "eq", "PLC-A")
            ],
            CreateRange("2026-04-20T01:02:03Z", "2026-04-21T04:05:06Z"),
            "occurredAt",
            true,
            50));

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.AbsolutePath.Should().Be("/api/v1/ai/read/capacity/summary");
        var query = ParseQuery(capturedRequest.RequestUri);
        query.Should().Contain("deviceId", "device-1");
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
                new CloudAiReadFilter("deviceId", "eq", "device-1"),
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
        query.Should().Contain("deviceId", "device-1");
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
                new CloudAiReadFilter("deviceId", "eq", "device-1"),
                new CloudAiReadFilter("preset", "eq", "last_24h")
            ],
            null,
            "occurredAt",
            true,
            30));

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.AbsolutePath.Should().Be("/api/v1/ai/read/device-logs");
        var query = ParseQuery(capturedRequest.RequestUri);
        query.Should().Contain("deviceId", "device-1");
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
                    Content = JsonContent.Create(new
                    {
                        items = new[]
                        {
                            new
                            {
                                id = "device-1",
                                deviceCode = "DEV-001",
                                deviceName = "叠片一号"
                            }
                        },
                        isTruncated = false
                    })
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
        ParseQuery(requestUris[0]).Should().Contain("keyword", "DEV-001");
        requestUris[1].AbsolutePath.Should().Be("/api/v1/ai/read/device-logs");
        var query = ParseQuery(requestUris[1]);
        query.Should().Contain("deviceId", "device-1");
        query.Should().Contain("preset", "last_24h");
        query.Should().NotContainKey("deviceCode");
        AssertNoLegacyParameters(query);
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
                new CloudAiReadFilter("deviceId", "eq", "device-1"),
                new CloudAiReadFilter("date", "eq", "2026-04-20"),
                new CloudAiReadFilter("processName", "eq", "PLC-A")
            ],
            null,
            "occurredAt",
            true,
            40));

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.AbsolutePath.Should().Be("/api/v1/ai/read/capacity/hourly");
        var query = ParseQuery(capturedRequest.RequestUri);
        query.Should().Contain("deviceId", "device-1");
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
                new CloudAiReadFilter("deviceId", "eq", "device-1"),
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
        query.Should().Contain("deviceId", "device-1");
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
            Content = JsonContent.Create(new
            {
                items = new[]
                {
                    new
                    {
                        recordId = "record-1",
                        typeKey = "stacking",
                        typeName = "叠片",
                        deviceId = "device-1",
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
                isTruncated = false
            })
        }));
        var client = CreateClient(httpClient);

        var result = await client.GetProductionRecordsAsync(new CloudAiReadQuery(
            "生产记录",
            [
                new CloudAiReadFilter("deviceId", "eq", "device-1"),
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
            [new CloudAiReadFilter("deviceId", "eq", "device-1")],
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
            [new CloudAiReadFilter("deviceId", "eq", "device-1"), new CloudAiReadFilter("barcode", "eq", "CELL-001")],
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
    [InlineData(HttpStatusCode.Unauthorized, CloudAiReadProblemCodes.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, CloudAiReadProblemCodes.Forbidden)]
    public async Task Client_ShouldReturnClearErrorsForTokenPermissionAndScopeProblems(
        HttpStatusCode statusCode,
        string expectedCode)
    {
        using var httpClient = new HttpClient(new StubHandler(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent("{}")
        }));
        var client = CreateClient(httpClient);

        var act = () => client.GetDeviceLogsAsync(new CloudAiReadQuery(
            "查看日志",
            [new CloudAiReadFilter("deviceId", "eq", "device-1")],
            CreateRange("2026-04-20T00:00:00Z", "2026-04-21T00:00:00Z"),
            "occurredAt",
            true,
            10));

        var exception = await act.Should().ThrowAsync<CloudAiReadException>();
        exception.Which.Code.Should().Be(expectedCode);
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
            Content = JsonContent.Create(new
            {
                items = Array.Empty<object>(),
                isTruncated = false
            })
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
                     "sortDirection",
                     "deviceCode"
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
