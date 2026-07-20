using System.Text.Json;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Runtime;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace AICopilot.SimulationTests;

public sealed class CloudReadonlySimulationTests
{
    [Fact]
    public void DevelopmentConfiguration_ShouldKeepCloudReadonlyDisabledByDefault()
    {
        var httpApiRoot = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "hosts",
            "AICopilot.HttpApi");
        var configuration = new ConfigurationBuilder()
            .SetBasePath(httpApiRoot)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: false)
            .Build();
        var cloudReadonly = configuration
            .GetSection(CloudReadonlyOptions.SectionName)
            .Get<CloudReadonlyOptions>()!;
        var cloudAiRead = configuration
            .GetSection(CloudAiReadOptions.SectionName)
            .Get<CloudAiReadOptions>()!;

        cloudReadonly.Mode.Should().Be(CloudReadonlyDataSourceMode.Disabled);
        cloudReadonly.Simulation.Enabled.Should().BeFalse();
        cloudAiRead.Enabled.Should().BeFalse();
        cloudReadonly.EnsureValid(cloudAiRead, "Development");
    }

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
            var result = await provider.QueryAsync(CreateToolRequest(intent));

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
    public void CloudReadonlyOptions_ShouldAllowSimulationOnlyInDevelopment()
    {
        var options = CreateSimulationOptions();

        options.EnsureValid(environmentName: "Development");

        var action = () => options.EnsureValid(environmentName: "Production");
        action.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Simulation is only allowed in Development*");
    }

    [Fact]
    public void CloudReadonlyOptions_ShouldAllowDisabledInProduction()
    {
        var options = new CloudReadonlyOptions
        {
            Mode = CloudReadonlyDataSourceMode.Disabled
        };

        options.EnsureValid(environmentName: "Production");
    }

    [Fact]
    public void CloudReadonlyOptions_ShouldAllowRealInProduction_WhenAiReadConfigured()
    {
        var options = new CloudReadonlyOptions
        {
            Mode = CloudReadonlyDataSourceMode.Real,
            Real = new CloudReadonlyRealOptions
            {
                Enabled = true,
                AllowProductionRead = true
            }
        };
        var aiRead = new CloudAiReadOptions
        {
            Enabled = true,
            BaseUrl = "https://cloud.internal.example",
            ServiceAccountToken = "secret-token"
        };

        options.EnsureValid(aiRead, "Production");
    }

    [Fact]
    public async Task RealProvider_ShouldRequireCloudReadonlyAndCloudAiReadDoubleEnable()
    {
        var provider = new RealCloudReadonlyDataProvider(
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

        var action = () => provider.QueryAsync(CreateToolRequest("Analysis.Device.List", confidence: 0.9));

        var exception = await action.Should().ThrowAsync<CloudAiReadException>();
        exception.Which.Code.Should().Be(CloudAiReadProblemCodes.NotConfigured);
    }

    [Fact]
    public async Task RealProvider_ShouldPropagateCloudAiReadFailureWithoutSimulationFallback()
    {
        var cloudAiReadClient = new FailingCloudAiReadClient();
        var provider = new RealCloudReadonlyDataProvider(
            cloudAiReadClient,
            Options.Create(new CloudReadonlyOptions
            {
                Mode = CloudReadonlyDataSourceMode.Real,
                Real = new CloudReadonlyRealOptions
                {
                    Enabled = true,
                    AllowProductionRead = true
                }
            }));
        var resolver = new FixedCloudReadonlyDataProviderResolver(provider);
        var executor = new CloudReadonlyAgentToolExecutor(resolver);

        var overLimitAction = () => executor.ExecuteAsync(
            CreateToolRequest("Analysis.Device.List", confidence: 0.9, limit: 101));

        var overLimitException = await overLimitAction.Should().ThrowAsync<CloudAiReadException>();
        overLimitException.Which.Code.Should().Be(AppProblemCodes.CloudReadonlyIntentUnsupported);
        overLimitException.Which.Message.Should().Be(
            "Cloud readonly intent violates the frozen typed semantic plan contract.");
        resolver.ResolveCount.Should().Be(0, "the typed boundary must reject before resolving a provider");
        cloudAiReadClient.QueryCount.Should().Be(0);

        var action = () => executor.ExecuteAsync(
            CreateToolRequest("Analysis.Device.List", confidence: 0.9));

        var exception = await action.Should().ThrowAsync<CloudAiReadException>();
        exception.Which.Code.Should().Be(CloudAiReadProblemCodes.Unavailable);
        exception.Which.Message.Should().NotContain("Simulation");
        resolver.ResolveCount.Should().Be(1);
        cloudAiReadClient.QueryCount.Should().Be(1);
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
            Options.Create(CreateSimulationOptions()));
    }

    private static CloudReadonlyAgentToolRequest CreateToolRequest(
        string intent,
        double confidence = 0.95,
        int limit = 20)
    {
        var (target, kind) = intent switch
        {
            "Analysis.Device.Status" => (SemanticQueryTarget.Device, SemanticQueryKind.Status),
            "Analysis.Device.List" => (SemanticQueryTarget.Device, SemanticQueryKind.List),
            "Analysis.DeviceLog.Recent" => (SemanticQueryTarget.DeviceLog, SemanticQueryKind.Latest),
            "Analysis.Capacity.Trend" => (SemanticQueryTarget.Capacity, SemanticQueryKind.Range),
            "Analysis.Quality.Defect" => (SemanticQueryTarget.ProductionData, SemanticQueryKind.Range),
            "Analysis.WorkOrder.Maintenance" => (SemanticQueryTarget.DeviceLog, SemanticQueryKind.Range),
            "Analysis.Line.WeeklyReport" => (SemanticQueryTarget.Capacity, SemanticQueryKind.Range),
            _ => throw new ArgumentOutOfRangeException(nameof(intent), intent, "Unsupported simulation intent fixture.")
        };
        var semanticPlan = new SemanticQueryPlan(
            intent,
            target,
            kind,
            QueryText: null,
            new SemanticProjection([]),
            [
                new SemanticFilter("days", SemanticFilterOperator.Equal, "7"),
                new SemanticFilter("lineName", SemanticFilterOperator.Equal, "LINE-A")
            ],
            TimeRange: null,
            Sort: null,
            Limit: limit);
        var plannedIntent = CloudReadonlyAgentPlanIntent.FromSemanticPlan(semanticPlan, confidence);
        return new CloudReadonlyAgentToolRequest(
            plannedIntent.SemanticPlan,
            plannedIntent.SemanticPlanDigest,
            plannedIntent.Confidence);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AICopilot.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate AICopilot.slnx from the Simulation test output directory.");
    }

    private static CloudReadonlyOptions CreateSimulationOptions()
    {
        return new CloudReadonlyOptions
        {
            Mode = CloudReadonlyDataSourceMode.Simulation,
            Simulation = new CloudReadonlySimulationOptions
            {
                Enabled = true,
                SeedData = true,
                DataSet = "ManufacturingDemo",
                AlwaysMarkAsSimulation = true
            }
        };
    }

    private sealed class DisabledCloudAiReadClient : ICloudAiReadClient
    {
        public bool IsEnabled => false;

        public Task<CloudAiReadResult<CloudAiReadDeviceDto>> GetDevicesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Real Cloud client should not be called.");
        }

        public Task<CloudAiReadResult<CloudAiReadProcessDto>> GetProcessesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Real Cloud client should not be called.");
        }

        public Task<CloudAiReadResult<CloudAiReadClientReleaseVersionDto>> GetClientReleasesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Real Cloud client should not be called.");
        }

        public Task<CloudAiReadResult<CloudAiReadDeviceClientStateDto>> GetDeviceClientStatesAsync(
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

        public Task<CloudAiReadResult<CloudAiReadCapacityHourlyDto>> GetCapacityHourlyAsync(
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

        public Task<CloudAiReadResult<CloudAiReadProductionRecordDto>> GetProductionRecordsAsync(
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

    private sealed class FailingCloudAiReadClient : ICloudAiReadClient
    {
        public bool IsEnabled => true;

        public int QueryCount { get; private set; }

        public Task<CloudAiReadResult<CloudAiReadDeviceDto>> GetDevicesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw CreateUnavailableException();
        }

        public Task<CloudAiReadResult<CloudAiReadProcessDto>> GetProcessesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw CreateUnavailableException();
        }

        public Task<CloudAiReadResult<CloudAiReadClientReleaseVersionDto>> GetClientReleasesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw CreateUnavailableException();
        }

        public Task<CloudAiReadResult<CloudAiReadDeviceClientStateDto>> GetDeviceClientStatesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw CreateUnavailableException();
        }

        public Task<CloudAiReadResult<CloudAiReadCapacitySummaryDto>> GetCapacitySummaryAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw CreateUnavailableException();
        }

        public Task<CloudAiReadResult<CloudAiReadCapacityHourlyDto>> GetCapacityHourlyAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw CreateUnavailableException();
        }

        public Task<CloudAiReadResult<CloudAiReadDeviceLogDto>> GetDeviceLogsAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw CreateUnavailableException();
        }

        public Task<CloudAiReadResult<CloudAiReadProductionRecordDto>> GetProductionRecordsAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw CreateUnavailableException();
        }

        public Task<CloudAiReadResult<object>> QuerySemanticAsync(
            SemanticQueryPlan plan,
            CancellationToken cancellationToken = default)
        {
            QueryCount++;
            throw CreateUnavailableException();
        }

        private static CloudAiReadException CreateUnavailableException()
        {
            return new CloudAiReadException(
                CloudAiReadProblemCodes.Unavailable,
                "Cloud AiRead endpoint is unavailable.");
        }
    }

    private sealed class FixedCloudReadonlyDataProviderResolver(ICloudReadonlyDataProvider provider)
        : ICloudReadonlyDataProviderResolver
    {
        public int ResolveCount { get; private set; }

        public ICloudReadonlyDataProvider Resolve()
        {
            ResolveCount++;
            return provider;
        }
    }
}
