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
    [InlineData("/api/v1/ai/read/capacity/summary")]
    [InlineData("/api/v1/ai/read/device-logs")]
    [InlineData("/api/v1/ai/read/pass-stations/stacking")]
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
    [InlineData("GET", "/api/v1/ai/read/pass-stations/a/b")]
    [InlineData("GET", "/api/v1/ai/read/recipes/versions")]
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
            null,
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
            "overload",
            [
                new CloudAiReadFilter("deviceId", "eq", "device-1"),
                new CloudAiReadFilter("level", "eq", "Error")
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
        query.Should().Contain("level", "Error");
        query.Should().Contain("keyword", "overload");
        query.Should().Contain("maxRows", "30");
        AssertNoLegacyParameters(query);
    }

    [Fact]
    public async Task Client_ShouldSendPassStationQueryWithCloudAiReadParametersOnly()
    {
        HttpRequestMessage? capturedRequest = null;
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            capturedRequest = request;
            return CreateOkItemsResponse();
        }));
        var client = CreateClient(httpClient);

        await client.GetPassStationRecordsAsync(new CloudAiReadQuery(
            "不要作为 queryText 发送",
            [
                new CloudAiReadFilter("deviceId", "eq", "device-1"),
                new CloudAiReadFilter("barcode", "eq", "CELL-001")
            ],
            CreateRange("2026-04-20T00:00:00Z", "2026-04-21T00:00:00Z"),
            "occurredAt",
            true,
            40,
            PassStationTypeKey: "stacking"));

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.AbsolutePath.Should().Be("/api/v1/ai/read/pass-stations/stacking");
        var query = ParseQuery(capturedRequest.RequestUri);
        query.Should().Contain("deviceId", "device-1");
        query.Should().Contain("startTime", "2026-04-20T00:00:00.0000000Z");
        query.Should().Contain("endTime", "2026-04-21T00:00:00.0000000Z");
        query.Should().Contain("barcode", "CELL-001");
        query.Should().Contain("maxRows", "40");
        AssertNoLegacyParameters(query);
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
            [new CloudAiReadFilter("deviceCode", "eq", "DEV-001")],
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
    public async Task Client_ShouldNotSendHttpWhenPassStationTimeRangeIsMissing()
    {
        var sendCount = 0;
        using var httpClient = new HttpClient(new StubHandler(_ =>
        {
            sendCount++;
            return CreateOkItemsResponse();
        }));
        var client = CreateClient(httpClient);

        var act = () => client.GetPassStationRecordsAsync(new CloudAiReadQuery(
            null,
            [new CloudAiReadFilter("barcode", "eq", "CELL-001")],
            null,
            null,
            false,
            20));

        var exception = await act.Should().ThrowAsync<CloudAiReadException>();
        exception.Which.Code.Should().Be(CloudAiReadProblemCodes.MissingRequiredParameter);
        exception.Which.Message.Should().Contain("时间");
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
