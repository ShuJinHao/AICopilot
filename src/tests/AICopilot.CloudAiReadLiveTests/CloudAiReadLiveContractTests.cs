using System.Net.Http.Headers;
using System.Text.Json;
using AICopilot.Infrastructure.CloudRead;
using AICopilot.Services.Contracts;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AICopilot.CloudAiReadLiveTests;

/// <summary>
/// This project is intentionally separate from the normal backend test suite.
/// It only accepts a real, non-production Cloud provider through environment variables;
/// invoking it without the required environment fails instead of silently skipping.
/// </summary>
public sealed class CloudAiReadLiveContractTests
{
    private static readonly string[] EnvelopeProperties =
    [
        "items",
        "asOfUtc",
        "source",
        "queryScope",
        "rowCount",
        "truncated",
        "nextCursor"
    ];

    [Fact]
    public async Task CurrentCloudProviderAndTypedClient_ShouldPassEightEndpointContract()
    {
        var environment = LiveEnvironment.ReadRequired();
        using var httpClient = new HttpClient();
        var client = CreateClient(httpClient, environment.BaseUrl, environment.FullToken);

        var range = new CloudAiReadTimeRange("occurredAt", environment.StartTime, environment.EndTime);
        var futureRange = new CloudAiReadTimeRange(
            "occurredAt",
            environment.EndTime.AddYears(10),
            environment.EndTime.AddYears(10).AddDays(1));

        var devices = await client.GetDevicesAsync(Query(
            [Filter("deviceId", environment.DeviceId)],
            limit: 20));
        AssertNonEmpty(devices, "devices");
        devices.Items.Should().ContainSingle(item => item.DeviceId == environment.DeviceId);
        devices.Rows[0].Keys.Should().NotContain(["status", "lineName", "processName", "updatedAt", "updatedAtUtc"]);
        await AssertEmptyAsync(client.GetDevicesAsync(Query([Filter("deviceId", Guid.NewGuid().ToString())])));
        AssertTruncated(await client.GetDevicesAsync(Query([], limit: 1)), "devices");

        var processes = await client.GetProcessesAsync(Query(
            [Filter("processId", environment.ProcessId)],
            limit: 20));
        AssertNonEmpty(processes, "processes");
        processes.Items.Should().ContainSingle(item => item.ProcessId == environment.ProcessId);
        await AssertEmptyAsync(client.GetProcessesAsync(Query([Filter("processId", Guid.NewGuid().ToString())])));
        AssertTruncated(await client.GetProcessesAsync(Query([], limit: 1)), "processes");

        var releases = await client.GetClientReleasesAsync(Query(
            [
                Filter("channel", environment.Channel),
                Filter("targetRuntime", environment.TargetRuntime),
                Filter("status", "Published")
            ],
            limit: 20));
        AssertNonEmpty(releases, "client_release_versions");
        releases.Items.Should().OnlyContain(item => item.Channel == environment.Channel);
        await AssertEmptyAsync(client.GetClientReleasesAsync(Query(
            [Filter("channel", $"alignment-empty-{Guid.NewGuid():N}")])));
        AssertTruncated(await client.GetClientReleasesAsync(Query(
            [Filter("channel", environment.Channel), Filter("targetRuntime", environment.TargetRuntime)],
            limit: 1)), "client_release_versions");

        var states = await client.GetDeviceClientStatesAsync(Query(
            [Filter("deviceId", environment.DeviceId)],
            limit: 20));
        AssertNonEmpty(states, "device_client_states");
        states.Items.Should().ContainSingle().Which.SoftwareStatus.Should().Be("Running");

        var missing = await client.GetDeviceClientStatesAsync(Query(
            [Filter("deviceId", environment.MissingDeviceId)],
            limit: 20));
        missing.Items.Should().ContainSingle().Which.SoftwareStatus.Should().Be("MissingRuntimeHeartbeat");

        var stale = await client.GetDeviceClientStatesAsync(Query(
            [Filter("deviceId", environment.StaleDeviceId)],
            limit: 20));
        stale.Items.Should().ContainSingle().Which.SoftwareStatus.Should().Be("RuntimeHeartbeatStale");
        stale.Items[0].SoftwareStatus.Should().NotBe("Stopped");

        await AssertEmptyAsync(client.GetDeviceClientStatesAsync(Query(
            [Filter("deviceId", Guid.NewGuid().ToString())])));
        AssertTruncated(await client.GetDeviceClientStatesAsync(Query([], limit: 1)), "device_client_states");

        using (var stateOnlyHttpClient = new HttpClient())
        {
            var stateOnlyClient = CreateClient(stateOnlyHttpClient, environment.BaseUrl, environment.StateOnlyToken);
            var stateByCode = await stateOnlyClient.GetDeviceClientStatesAsync(Query(
                [Filter("deviceCode", environment.DeviceCode)],
                limit: 20));
            stateByCode.Items.Should().ContainSingle(item => item.DeviceId == environment.DeviceId);
        }

        using (var forbiddenHttpClient = new HttpClient())
        {
            var forbiddenClient = CreateClient(forbiddenHttpClient, environment.BaseUrl, environment.ForbiddenToken);
            var forbidden = () => forbiddenClient.GetDevicesAsync(Query([]));
            var exception = await forbidden.Should().ThrowAsync<CloudAiReadException>();
            exception.Which.Code.Should().Be(CloudAiReadProblemCodes.Forbidden);
        }

        var capacity = await client.GetCapacitySummaryAsync(Query(
            [Filter("deviceId", environment.DeviceId)],
            range,
            limit: 20));
        AssertNonEmpty(capacity, "capacity.summary");
        await AssertEmptyAsync(client.GetCapacitySummaryAsync(Query(
            [Filter("deviceId", environment.DeviceId)],
            futureRange)));
        AssertTruncated(await client.GetCapacitySummaryAsync(Query(
            [Filter("deviceId", environment.DeviceId)],
            range,
            limit: 1)), "capacity.summary");

        var hourly = await client.GetCapacityHourlyAsync(Query(
            [Filter("deviceId", environment.DeviceId), Filter("date", environment.HourlyDate)],
            limit: 20));
        AssertNonEmpty(hourly, "capacity.hourly");
        await AssertEmptyAsync(client.GetCapacityHourlyAsync(Query(
            [Filter("deviceId", environment.DeviceId), Filter("date", "2099-01-01")])));
        AssertTruncated(await client.GetCapacityHourlyAsync(Query(
            [Filter("deviceId", environment.DeviceId), Filter("date", environment.HourlyDate)],
            limit: 1)), "capacity.hourly");

        var logs = await client.GetDeviceLogsAsync(Query(
            [Filter("deviceId", environment.DeviceId)],
            range,
            limit: 20));
        AssertNonEmpty(logs, "device_logs");
        await AssertEmptyAsync(client.GetDeviceLogsAsync(Query(
            [Filter("deviceId", environment.DeviceId)],
            futureRange)));
        AssertTruncated(await client.GetDeviceLogsAsync(Query(
            [Filter("deviceId", environment.DeviceId)],
            range,
            limit: 1)), "device_logs");

        var production = await client.GetProductionRecordsAsync(Query(
            [Filter("deviceId", environment.DeviceId)],
            range,
            limit: 20));
        AssertNonEmpty(production, "production_records");
        production.Rows.Should().OnlyContain(row =>
            !row.ContainsKey("processName") &&
            !row.ContainsKey("stationName") &&
            !row.ContainsKey("deviceCode"));
        await AssertEmptyAsync(client.GetProductionRecordsAsync(Query(
            [Filter("deviceId", environment.DeviceId)],
            futureRange)));
        AssertTruncated(await client.GetProductionRecordsAsync(Query(
            [Filter("deviceId", environment.DeviceId)],
            range,
            limit: 1)), "production_records");

        await AssertRedactedScopesAsync(client, environment, range);
        await AssertStrictProviderShapesAsync(environment);
    }

    private static async Task AssertRedactedScopesAsync(
        CloudAiReadClient client,
        LiveEnvironment environment,
        CloudAiReadTimeRange range)
    {
        var sentinel = environment.Sentinel;
        AssertRedacted(await client.GetDevicesAsync(Query([Filter("keyword", sentinel)])), "keyword", sentinel);
        AssertRedacted(await client.GetProcessesAsync(Query([Filter("keyword", sentinel)])), "keyword", sentinel);
        AssertRedacted(await client.GetClientReleasesAsync(Query([Filter("channel", sentinel)])), "channel", sentinel);
        AssertRedacted(await client.GetDeviceClientStatesAsync(Query([Filter("keyword", sentinel)])), "keyword", sentinel);
        AssertRedacted(await client.GetCapacitySummaryAsync(Query(
            [Filter("deviceId", environment.DeviceId), Filter("plcName", sentinel)],
            range)), "plcName", sentinel);
        AssertRedacted(await client.GetCapacityHourlyAsync(Query(
            [
                Filter("deviceId", environment.DeviceId),
                Filter("date", environment.HourlyDate),
                Filter("plcName", sentinel)
            ])), "plcName", sentinel);
        AssertRedacted(await client.GetDeviceLogsAsync(Query(
            [Filter("deviceId", environment.DeviceId), Filter("keyword", sentinel)],
            range)), "keyword", sentinel);
        AssertRedacted(await client.GetProductionRecordsAsync(Query(
            [Filter("deviceId", environment.DeviceId), Filter("barcode", sentinel)],
            range)), "barcode", sentinel);
    }

    private static async Task AssertStrictProviderShapesAsync(LiveEnvironment environment)
    {
        using var httpClient = new HttpClient { BaseAddress = new Uri(environment.BaseUrl) };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", environment.FullToken);
        var start = Uri.EscapeDataString(environment.StartTime.ToString("O"));
        var end = Uri.EscapeDataString(environment.EndTime.ToString("O"));

        await AssertRawShapeAsync(httpClient,
            $"/api/v1/ai/read/devices?deviceId={environment.DeviceId}",
            ["id", "deviceCode", "deviceName", "processId"]);
        await AssertRawShapeAsync(httpClient,
            $"/api/v1/ai/read/processes?processId={environment.ProcessId}",
            ["id", "processCode", "processName"]);
        await AssertRawShapeAsync(httpClient,
            $"/api/v1/ai/read/client-releases?channel={environment.Channel}&targetRuntime={environment.TargetRuntime}&status=Published&maxRows=1",
            [
                "id", "componentKind", "componentKey", "displayName", "channel", "targetRuntime",
                "version", "status", "releaseNotes", "createdAtUtc", "publishedAtUtc", "deletedAtUtc"
            ]);
        await AssertRawShapeAsync(httpClient,
            $"/api/v1/ai/read/device-client-states?deviceId={environment.DeviceId}",
            [
                "deviceId", "deviceName", "clientCode", "primaryIp", "channel", "hostVersion",
                "hostApiVersion", "versionReportedAtUtc", "versionReceivedAtUtc", "softwareStatus",
                "runtimeStatus", "runtimeStartedAtUtc", "lastRuntimeHeartbeatAtUtc", "updatedAtUtc"
            ]);
        await AssertRawShapeAsync(httpClient,
            $"/api/v1/ai/read/capacity/summary?deviceId={environment.DeviceId}&startDate={environment.StartTime:yyyy-MM-dd}&endDate={environment.EndTime:yyyy-MM-dd}&maxRows=1",
            ["date", "totalCount", "okCount", "ngCount", "dayShiftTotal", "nightShiftTotal"]);
        await AssertRawShapeAsync(httpClient,
            $"/api/v1/ai/read/capacity/hourly?deviceId={environment.DeviceId}&date={environment.HourlyDate}&maxRows=1",
            ["time", "date", "hour", "minute", "timeLabel", "shiftCode", "totalCount", "okCount", "ngCount", "okRate"]);
        await AssertRawShapeAsync(httpClient,
            $"/api/v1/ai/read/device-logs?deviceId={environment.DeviceId}&startTime={start}&endTime={end}&maxRows=1",
            ["id", "deviceId", "deviceName", "level", "message", "logTime", "receivedAt"]);
        await AssertRawShapeAsync(httpClient,
            $"/api/v1/ai/read/production-records?deviceId={environment.DeviceId}&startTime={start}&endTime={end}&maxRows=1",
            [
                "recordId", "typeKey", "typeName", "deviceId", "deviceName", "barcode", "result",
                "completedAt", "receivedAt", "fields", "fieldSchema"
            ]);
    }

    private static async Task AssertRawShapeAsync(
        HttpClient httpClient,
        string path,
        IReadOnlyCollection<string> expectedItemProperties)
    {
        using var response = await httpClient.GetAsync(path);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(EnvelopeProperties);
        var items = document.RootElement.GetProperty("items");
        items.GetArrayLength().Should().BeGreaterThan(0, path);
        items[0].EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(expectedItemProperties);
    }

    private static CloudAiReadClient CreateClient(HttpClient httpClient, string baseUrl, string token)
    {
        return new CloudAiReadClient(
            httpClient,
            Options.Create(new CloudAiReadOptions
            {
                Enabled = true,
                BaseUrl = baseUrl,
                ServiceAccountToken = token,
                TimeoutSeconds = 30
            }),
            NullLogger<CloudAiReadClient>.Instance);
    }

    private static CloudAiReadFilter Filter(string field, string value) => new(field, "eq", value);

    private static CloudAiReadQuery Query(
        IReadOnlyList<CloudAiReadFilter> filters,
        CloudAiReadTimeRange? range = null,
        int limit = 100)
    {
        return new CloudAiReadQuery(null, filters, range, null, false, limit);
    }

    private static void AssertNonEmpty<T>(CloudAiReadResult<T> result, string providerSource)
    {
        result.Items.Should().NotBeEmpty();
        result.ProviderSource.Should().Be(providerSource);
        result.RowCount.Should().Be(result.Items.Count);
        result.AsOfUtc.Should().NotBe(default);
    }

    private static void AssertTruncated<T>(CloudAiReadResult<T> result, string providerSource)
    {
        result.ProviderSource.Should().Be(providerSource);
        result.Items.Should().ContainSingle();
        result.RowCount.Should().Be(1);
        result.IsTruncated.Should().BeTrue();
    }

    private static async Task AssertEmptyAsync<T>(Task<CloudAiReadResult<T>> resultTask)
    {
        var result = await resultTask;
        result.Items.Should().BeEmpty();
        result.RowCount.Should().Be(0);
        result.IsTruncated.Should().BeFalse();
    }

    private static void AssertRedacted<T>(CloudAiReadResult<T> result, string key, string sentinel)
    {
        result.QueryScope.Should().Contain($"{key}=present");
        result.QueryScope.Should().NotContain(sentinel);
    }

    private sealed record LiveEnvironment(
        string BaseUrl,
        string FullToken,
        string StateOnlyToken,
        string ForbiddenToken,
        string DeviceId,
        string MissingDeviceId,
        string StaleDeviceId,
        string DeviceCode,
        string ProcessId,
        string Channel,
        string TargetRuntime,
        string HourlyDate,
        DateTimeOffset StartTime,
        DateTimeOffset EndTime,
        string Sentinel)
    {
        public static LiveEnvironment ReadRequired()
        {
            return new LiveEnvironment(
                Require("CLOUD_AI_READ_LIVE_BASE_URL"),
                Require("CLOUD_AI_READ_LIVE_TOKEN"),
                Require("CLOUD_AI_READ_LIVE_STATE_ONLY_TOKEN"),
                Require("CLOUD_AI_READ_LIVE_FORBIDDEN_TOKEN"),
                RequireGuid("CLOUD_AI_READ_LIVE_DEVICE_ID"),
                RequireGuid("CLOUD_AI_READ_LIVE_MISSING_DEVICE_ID"),
                RequireGuid("CLOUD_AI_READ_LIVE_STALE_DEVICE_ID"),
                Require("CLOUD_AI_READ_LIVE_DEVICE_CODE"),
                RequireGuid("CLOUD_AI_READ_LIVE_PROCESS_ID"),
                Require("CLOUD_AI_READ_LIVE_CHANNEL"),
                Require("CLOUD_AI_READ_LIVE_TARGET_RUNTIME"),
                RequireDate("CLOUD_AI_READ_LIVE_HOURLY_DATE"),
                RequireDateTimeOffset("CLOUD_AI_READ_LIVE_START_TIME"),
                RequireDateTimeOffset("CLOUD_AI_READ_LIVE_END_TIME"),
                Require("CLOUD_AI_READ_LIVE_SENTINEL"));
        }

        private static string Require(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"Explicit Cloud AiRead live contract test requires environment variable {name}.");
            }

            return value;
        }

        private static string RequireGuid(string name)
        {
            var value = Require(name);
            if (!Guid.TryParse(value, out var parsed) || parsed == Guid.Empty)
            {
                throw new InvalidOperationException($"Environment variable {name} must be a non-empty GUID.");
            }

            return parsed.ToString();
        }

        private static string RequireDate(string name)
        {
            var value = Require(name);
            if (!DateOnly.TryParse(value, out _))
            {
                throw new InvalidOperationException($"Environment variable {name} must be an ISO date.");
            }

            return value;
        }

        private static DateTimeOffset RequireDateTimeOffset(string name)
        {
            var value = Require(name);
            if (!DateTimeOffset.TryParse(value, out var parsed))
            {
                throw new InvalidOperationException($"Environment variable {name} must be an ISO timestamp.");
            }

            return parsed;
        }
    }
}
