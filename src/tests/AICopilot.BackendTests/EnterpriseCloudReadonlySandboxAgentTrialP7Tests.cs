using System.Text.Json;
using AICopilot.AiGatewayService.CloudReadiness;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using Microsoft.Extensions.Options;

namespace AICopilot.BackendTests;

[Trait("Suite", "EnterpriseCloudReadonlySandboxAgentTrialP7")]
public sealed class EnterpriseCloudReadonlySandboxAgentTrialP7Tests
{
    [Fact]
    public void BuildStatus_WithDefaultDisabledConfig_ShouldKeepTrialClosed()
    {
        var service = CreateService();

        var status = service.BuildStatus();

        status.Status.Should().Be(CloudReadonlySandboxAgentTrialStatuses.Disabled);
        status.TrialEnabled.Should().BeFalse();
        status.ToolVisible.Should().BeFalse();
        status.ToolExecutable.Should().BeFalse();
        status.Boundary.Should().Be(CloudReadonlySandboxAgentTrialMarkers.Boundary);
        status.SandboxSmokeStatus.Should().Be(CloudReadonlyReadinessStatuses.NotConfigured);
    }

    [Fact]
    public void SandboxTrialTool_ShouldBeSeparateFromProductionCloudReadonlyTool()
    {
        var productionTool = BuiltInToolRegistrations.AgentRuntimeTools
            .Should()
            .ContainSingle(item => item.ToolCode == "query_cloud_data_readonly")
            .Which;
        productionTool.IsEnabled.Should().BeFalse();
        productionTool.IsVisibleToPlanner.Should().BeFalse();
        productionTool.IsExecutableByAgent.Should().BeFalse();
        productionTool.ApprovalPolicy.Should().Be("DisabledRealCloudReadonly");

        var sandboxTool = BuiltInToolRegistrations.AgentRuntimeTools
            .Should()
            .ContainSingle(item => item.ToolCode == CloudReadonlySandboxAgentTrialMarkers.ToolCode)
            .Which;
        sandboxTool.ProviderType.Should().Be(ToolProviderType.CloudReadonly);
        sandboxTool.DataBoundary.Should().Be(ToolDataBoundary.CloudReadonlySandboxOnly);
        sandboxTool.ApprovalPolicy.Should().Be("SandboxAgentTrial");
        sandboxTool.RequiresApproval.Should().BeTrue();
        sandboxTool.IsEnabled.Should().BeTrue();
        sandboxTool.IsVisibleToPlanner.Should().BeTrue();
        sandboxTool.IsExecutableByAgent.Should().BeTrue();
    }

    [Fact]
    public async Task RunScenario_WhenTrialEnabledButSmokeMissing_ShouldRejectWithoutCallingSandbox()
    {
        var client = new RecordingSandboxClient();
        var service = CreateService(
            sandbox: EnabledSandbox(),
            trial: EnabledTrial(),
            sandboxClient: client);

        var result = await service.RunScenarioAsync(
            "cloud-sandbox-devices",
            ["Markdown"],
            maxRows: 20,
            timeoutMs: 5000,
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        client.CallCount.Should().Be(0);
        service.BuildStatus().Status.Should().Be(CloudReadonlySandboxAgentTrialStatuses.SandboxSmokeRequired);
    }

    [Fact]
    public async Task RunScenario_WithSmokePassed_ShouldReturnSandboxMarkersAndHashes()
    {
        var history = new TestReadinessHistoryStore();
        history.Save(PassedSandboxSmoke());
        var client = new RecordingSandboxClient();
        var service = CreateService(
            sandbox: EnabledSandbox(),
            trial: EnabledTrial(),
            readinessHistory: history,
            sandboxClient: client);

        var result = await service.RunScenarioAsync(
            "cloud-sandbox-capacity-summary",
            ["Chart", "Markdown"],
            maxRows: 20,
            timeoutMs: 5000,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var value = result.Value!;
        value.Status.Should().Be(CloudReadonlySandboxAgentTrialStatuses.Completed);
        value.Boundary.Should().Be(CloudReadonlySandboxAgentTrialMarkers.Boundary);
        value.QueryResult.SourceType.Should().Be("CloudReadonly");
        value.QueryResult.SourceMode.Should().Be("CloudReadonlySandbox");
        value.QueryResult.IsSandbox.Should().BeTrue();
        value.QueryResult.IsSimulation.Should().BeFalse();
        value.QueryResult.SourceLabel.Should().Be("Cloud 只读 Sandbox（非生产）");
        value.QueryResult.Boundary.Should().Be("SandboxAgentTrial");
        value.QueryResult.EndpointCode.Should().Be("capacity_summary");
        value.QueryResult.RowCount.Should().Be(2);
        value.QueryResult.QueryHash.Should().NotBeNullOrWhiteSpace();
        value.QueryResult.ResultHash.Should().NotBeNullOrWhiteSpace();
        value.QueryResult.Rows.Should().OnlyContain(row =>
            row.ContainsKey("sourceMode") &&
            row.ContainsKey("isSandbox") &&
            row.ContainsKey("endpointCode"));
        client.CallCount.Should().Be(1);

        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json.Should().NotContain("super-secret-token");
        json.Should().NotContain("ServiceAccountToken");
    }

    [Theory]
    [InlineData("cloud-sandbox-devices", "devices")]
    [InlineData("cloud-sandbox-capacity-summary", "capacity_summary")]
    [InlineData("cloud-sandbox-device-logs", "device_logs")]
    [InlineData("cloud-sandbox-pass-station-records", "pass_station_records")]
    [InlineData("cloud-sandbox-device-exception-analysis", "device_logs")]
    [InlineData("cloud-sandbox-capacity-delivery-analysis", "capacity_summary")]
    public async Task RunScenario_ShouldAllowOnlySixFixedSandboxTemplates(string scenarioId, string endpointCode)
    {
        var history = new TestReadinessHistoryStore();
        history.Save(PassedSandboxSmoke());
        var service = CreateService(
            sandbox: EnabledSandbox(),
            trial: EnabledTrial(),
            readinessHistory: history,
            sandboxClient: new RecordingSandboxClient());

        var result = await service.RunScenarioAsync(
            scenarioId,
            null,
            maxRows: 10,
            timeoutMs: 5000,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.QueryResult.EndpointCode.Should().Be(endpointCode);
    }

    [Fact]
    public async Task RunScenario_WithUnknownScenario_ShouldReject()
    {
        var history = new TestReadinessHistoryStore();
        history.Save(PassedSandboxSmoke());
        var client = new RecordingSandboxClient();
        var service = CreateService(
            sandbox: EnabledSandbox(),
            trial: EnabledTrial(),
            readinessHistory: history,
            sandboxClient: client);

        var result = await service.RunScenarioAsync(
            "recipe_versions",
            null,
            maxRows: 20,
            timeoutMs: 5000,
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        client.CallCount.Should().Be(0);
    }

    private static CloudReadonlySandboxAgentTrialService CreateService(
        CloudReadonlyOptions? cloudReadonly = null,
        CloudAiReadOptions? cloudAiRead = null,
        CloudReadonlySandboxOptions? sandbox = null,
        CloudReadonlySandboxAgentTrialOptions? trial = null,
        ICloudReadonlyReadinessHistoryStore? readinessHistory = null,
        ICloudReadonlySandboxAgentTrialHistoryStore? trialHistory = null,
        ICloudReadonlySandboxClient? sandboxClient = null)
    {
        return new CloudReadonlySandboxAgentTrialService(
            Options.Create(cloudReadonly ?? new CloudReadonlyOptions()),
            Options.Create(sandbox ?? new CloudReadonlySandboxOptions()),
            Options.Create(cloudAiRead ?? new CloudAiReadOptions()),
            Options.Create(trial ?? new CloudReadonlySandboxAgentTrialOptions()),
            readinessHistory ?? new TestReadinessHistoryStore(),
            trialHistory ?? new TestTrialHistoryStore(),
            sandboxClient ?? new RecordingSandboxClient());
    }

    private static CloudReadonlySandboxAgentTrialOptions EnabledTrial() => new()
    {
        Enabled = true
    };

    private static CloudReadonlySandboxOptions EnabledSandbox() => new()
    {
        Enabled = true,
        BaseUrl = "http://sandbox-cloud.local",
        ServiceAccountToken = "super-secret-token",
        TimeoutSeconds = 10,
        DefaultPassStationTypeKey = "default"
    };

    private static CloudReadonlyReadinessDto PassedSandboxSmoke() => new(
        CloudReadonlyReadinessStatuses.RealSandboxPassed,
        CloudReadonlyReadinessModes.RealSandboxSmoke,
        CloudAiReadEnabled: false,
        RealEnabled: false,
        AllowProductionRead: false,
        BaseUrlConfigured: true,
        TokenConfigured: true,
        LastCheckedAt: DateTimeOffset.UtcNow,
        Checks: [],
        Errors: [],
        Warnings: [],
        Boundary: "SandboxSmokeOnly",
        SandboxStatus: new CloudReadonlySandboxStatusDto(
            CloudReadonlyReadinessStatuses.RealSandboxPassed,
            SandboxEnabled: true,
            BaseUrlConfigured: true,
            TokenConfigured: true,
            LastSmokeAt: DateTimeOffset.UtcNow,
            Checks: [],
            Errors: [],
            Warnings: []));

    private sealed class RecordingSandboxClient : ICloudReadonlySandboxClient
    {
        public int CallCount { get; private set; }

        public Task<JsonDocument> SendJsonAsync(
            CloudReadonlySandboxOptions options,
            HttpMethod method,
            string path,
            IReadOnlyDictionary<string, string?>? query = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(JsonDocument.Parse("""
                {
                  "items": [
                    { "id": "trial-001", "deviceCode": "SANDBOX-DEVICE-1", "actualQty": 12 },
                    { "id": "trial-002", "deviceCode": "SANDBOX-DEVICE-2", "actualQty": 18 }
                  ],
                  "isTruncated": false
                }
                """));
        }
    }

    private sealed class TestReadinessHistoryStore : ICloudReadonlyReadinessHistoryStore
    {
        private readonly List<CloudReadonlyReadinessDto> items = [];

        public void Save(CloudReadonlyReadinessDto report) => items.Insert(0, report);

        public IReadOnlyCollection<CloudReadonlyReadinessDto> List() => items.ToArray();
    }

    private sealed class TestTrialHistoryStore : ICloudReadonlySandboxAgentTrialHistoryStore
    {
        private readonly List<CloudReadonlySandboxAgentTrialResultDto> items = [];

        public void Save(CloudReadonlySandboxAgentTrialResultDto result) => items.Insert(0, result);

        public IReadOnlyCollection<CloudReadonlySandboxAgentTrialResultDto> List() => items.ToArray();
    }
}
