using System.Text.Json;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Runtime;
using Microsoft.Extensions.Options;

namespace AICopilot.BackendTests;

[Trait("Suite", "AgentSimulationAcceptance")]
public sealed class CloudReadonlySimulationTests
{
    [Fact]
    public async Task SimulationProvider_ShouldReturnSixClassDataset_WithSimulationMarkers()
    {
        var provider = CreateSimulationProvider();
        var intents = new[]
        {
            "Analysis.Device.Status",
            "Analysis.DeviceLog.Recent",
            "Analysis.Capacity.Trend",
            "Analysis.Quality.Defect",
            "Analysis.WorkOrder.Maintenance",
            "Analysis.Line.WeeklyReport"
        };

        foreach (var intent in intents)
        {
            var result = await provider.QueryAsync(new CloudReadonlyAgentToolRequest(
                intent,
                """{"lineName":"LINE-A","days":7,"limit":20}""",
                0.95));

            result.SourceMode.Should().Be(CloudReadonlySourceMarkers.SimulationSourceMode);
            result.IsSimulation.Should().BeTrue();
            result.SourceLabel.Should().Be(CloudReadonlySourceMarkers.SimulationSourceLabel);
            result.Rows.Should().NotBeEmpty();
            foreach (var row in result.Rows)
            {
                row["sourceMode"].Should().Be("Simulation");
                row["isSimulation"].Should().Be(true);
                row["sourceLabel"].Should().Be(CloudReadonlySourceMarkers.SimulationSourceLabel);
            }
        }
    }

    [Fact]
    public void SimulationDataSet_ShouldMeetOfflineAcceptanceMinimumCounts()
    {
        var dataSet = new CloudReadonlySimulationDataSet();

        dataSet.Devices.Should().HaveCountGreaterThanOrEqualTo(10);
        dataSet.DeviceLogs.Should().HaveCountGreaterThanOrEqualTo(80);
        dataSet.CapacityRecords.Should().HaveCountGreaterThanOrEqualTo(60);
        dataSet.QualityRecords.Should().HaveCountGreaterThanOrEqualTo(50);
        dataSet.WorkOrders.Should().HaveCountGreaterThanOrEqualTo(30);
    }

    [Fact]
    public async Task RealProvider_ShouldRequireCloudReadonlyAndCloudAiReadDoubleEnable()
    {
        var provider = new RealCloudReadonlyDataProvider(
            new FixedSemanticQueryPlanner(),
            new DisabledCloudAiReadClient(),
            Options.Create(new CloudReadonlyOptions
            {
                Mode = CloudReadonlyDataSourceMode.Real,
                Real = new CloudReadonlyRealOptions
                {
                    Enabled = true,
                    AllowProductionRead = true
                }
            }));

        var action = () => provider.QueryAsync(new CloudReadonlyAgentToolRequest(
            "Analysis.Device.List",
            """{"lineName":"LINE-A"}""",
            0.9));

        var exception = await action.Should().ThrowAsync<CloudAiReadException>();
        exception.Which.Code.Should().Be(CloudAiReadProblemCodes.NotConfigured);
    }

    [Fact]
    public void CloudReadonlyStatus_ShouldReturnSanitizedRealReady()
    {
        var status = CloudReadonlyStatusEvaluator.Evaluate(
            new CloudReadonlyOptions
            {
                Mode = CloudReadonlyDataSourceMode.Real,
                Real = new CloudReadonlyRealOptions
                {
                    Enabled = true,
                    AllowProductionRead = true
                }
            },
            new CloudAiReadOptions
            {
                Enabled = true,
                BaseUrl = "https://cloud.internal.example",
                ServiceAccountToken = "secret-token"
            });

        status.Status.Should().Be(CloudReadonlyRuntimeStatuses.RealReady);
        status.BaseUrlConfigured.Should().BeTrue();
        status.TokenConfigured.Should().BeTrue();
        status.ProductionReadAllowed.Should().BeTrue();
        status.Message.Should().Contain("可读取和分析数据");

        var serialized = JsonSerializer.Serialize(status);
        serialized.Should().NotContain("secret-token");
        serialized.Should().NotContain("cloud.internal.example");
    }

    [Theory]
    [InlineData(null, "secret-token", true, CloudReadonlyRuntimeStatuses.RealMissingBaseUrl)]
    [InlineData("https://cloud.internal.example", "", true, CloudReadonlyRuntimeStatuses.RealMissingToken)]
    [InlineData("https://cloud.internal.example", "secret-token", false, CloudReadonlyRuntimeStatuses.RealNotAllowed)]
    public void CloudReadonlyStatus_ShouldExposeConfigurationStateOnly(
        string? baseUrl,
        string token,
        bool productionReadAllowed,
        string expectedStatus)
    {
        var status = CloudReadonlyStatusEvaluator.Evaluate(
            new CloudReadonlyOptions
            {
                Mode = CloudReadonlyDataSourceMode.Real,
                Real = new CloudReadonlyRealOptions
                {
                    Enabled = true,
                    AllowProductionRead = productionReadAllowed
                }
            },
            new CloudAiReadOptions
            {
                Enabled = true,
                BaseUrl = baseUrl ?? string.Empty,
                ServiceAccountToken = token
            });

        status.Status.Should().Be(expectedStatus);
        JsonSerializer.Serialize(status).Should().NotContain("secret-token");
    }

    private static SimulationCloudReadonlyDataProvider CreateSimulationProvider()
    {
        return new SimulationCloudReadonlyDataProvider(
            new CloudReadonlySimulationDataSet(),
            Options.Create(new CloudReadonlyOptions
            {
                Mode = CloudReadonlyDataSourceMode.Simulation,
                Simulation = new CloudReadonlySimulationOptions
                {
                    Enabled = true,
                    SeedData = true,
                    DataSet = "ManufacturingDemo",
                    AlwaysMarkAsSimulation = true
                }
            }));
    }

    private sealed class FixedSemanticQueryPlanner : ISemanticQueryPlanner
    {
        public SemanticPlanningResult Plan(string intent, string? query)
        {
            return SemanticPlanningResult.Success(new SemanticQueryPlan(
                intent,
                SemanticQueryTarget.Device,
                SemanticQueryKind.List,
                query,
                new SemanticProjection(["deviceCode"]),
                [],
                null,
                new SemanticSort("deviceCode", SemanticSortDirection.Asc),
                20));
        }
    }

    private sealed class DisabledCloudAiReadClient : ICloudAiReadClient
    {
        public bool IsEnabled => false;

        public Task<JsonDocument> SendJsonAsync(
            HttpMethod method,
            string path,
            IReadOnlyDictionary<string, string?>? query = null,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Real Cloud client should not be called.");
        }

        public Task<CloudAiReadResult<CloudAiReadDeviceDto>> GetDevicesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Real Cloud client should not be called.");
        }

        public Task<CloudAiReadResult<CloudAiReadCapacitySummaryDto>> GetCapacitySummaryAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Real Cloud client should not be called.");
        }

        public Task<CloudAiReadResult<CloudAiReadDeviceLogDto>> GetDeviceLogsAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Real Cloud client should not be called.");
        }

        public Task<CloudAiReadResult<CloudAiReadPassStationRecordDto>> GetPassStationRecordsAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Real Cloud client should not be called.");
        }

        public Task<CloudAiReadResult<object>> QuerySemanticAsync(
            SemanticQueryPlan plan,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Real Cloud client should not be called.");
        }
    }
}
