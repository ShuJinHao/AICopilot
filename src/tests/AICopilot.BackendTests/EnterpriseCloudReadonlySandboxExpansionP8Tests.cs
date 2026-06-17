using System.Text.Json;
using AICopilot.AiGatewayService.CloudReadiness;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using Microsoft.Extensions.Options;

namespace AICopilot.BackendTests;

[Trait("Suite", "EnterpriseCloudReadonlySandboxExpansionP8")]
public sealed class EnterpriseCloudReadonlySandboxExpansionP8Tests
{
    [Fact]
    public void BuildStatus_WithDefaultDisabledConfig_ShouldKeepControlledTrialClosed()
    {
        var service = CreateService();

        var status = service.BuildStatus();

        status.Status.Should().Be(CloudReadonlySandboxControlledTrialStatuses.Disabled);
        status.ControlledTrialEnabled.Should().BeFalse();
        status.FreeGoalEnabled.Should().BeTrue();
        status.ToolVisible.Should().BeFalse();
        status.ToolExecutable.Should().BeFalse();
        status.Boundary.Should().Be(CloudReadonlySandboxControlledTrialMarkers.Boundary);
        status.SandboxSmokeStatus.Should().Be(CloudReadonlyReadinessStatuses.NotConfigured);
    }

    [Fact]
    public void BuildStatus_WithControlledEnabledButFixedTrialDisabled_ShouldRequireP7Gate()
    {
        var history = new TestReadinessHistoryStore();
        history.Save(PassedSandboxSmoke());
        var service = CreateService(
            sandbox: EnabledSandbox(),
            controlled: EnabledControlledTrial(),
            readinessHistory: history);

        var status = service.BuildStatus();

        status.Status.Should().Be(CloudReadonlySandboxControlledTrialStatuses.FixedTrialRequired);
        status.FixedTrialStatus.Should().Be(CloudReadonlySandboxAgentTrialStatuses.Disabled);
        status.ToolVisible.Should().BeFalse();
        status.ToolExecutable.Should().BeFalse();
    }

    [Theory]
    [InlineData("分析 sandbox 设备清单", "devices", "DeviceList")]
    [InlineData("汇总最近一周产能数据", "capacity_summary", "CapacitySummary")]
    [InlineData("查看设备日志并找出停机线索", "device_logs", "DeviceLogs")]
    [InlineData("查看过站记录", "pass_station_records", "PassStationRecords")]
    [InlineData("分析设备异常", "device_logs", "DeviceExceptionAnalysis")]
    [InlineData("分析产能交付风险", "capacity_summary", "CapacityDeliveryAnalysis")]
    public void CreateIntent_WhenReady_ShouldMapControlledFreeGoal(
        string goal,
        string expectedEndpoint,
        string expectedAnalysisType)
    {
        var service = CreateReadyService();

        var result = service.CreateIntent(goal, ["Markdown", "Html"], null, maxRows: 20);

        result.IsSuccess.Should().BeTrue();
        result.Value!.EndpointCodes.Should().ContainSingle().Which.Should().Be(expectedEndpoint);
        result.Value.AnalysisType.Should().Be(expectedAnalysisType);
        result.Value.RejectedReasons.Should().BeEmpty();
        result.Value.RequiresToolApproval.Should().BeTrue();
        result.Value.RequiresFinalApproval.Should().BeTrue();
    }

    [Theory]
    [InlineData("查询配方版本历史")]
    [InlineData("调用生产 Real Cloud 路径读取数据")]
    [InlineData("写入设备状态")]
    [InlineData("分析未知客户投诉")]
    public void CreateIntent_WithBlockedOrUnknownGoal_ShouldRejectWithoutSaving(string goal)
    {
        var service = CreateReadyService();

        var result = service.CreateIntent(goal, ["Markdown"], null, maxRows: 20);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void CreateIntent_WithTooLargeRangeOrRowsOrIllegalArtifact_ShouldReject()
    {
        var service = CreateReadyService();
        var range = new CloudSandboxGoalTimeRangeDto(
            DateTimeOffset.UtcNow.AddDays(-60),
            DateTimeOffset.UtcNow);

        var result = service.CreateIntent("分析产能数据", ["Exe"], range, maxRows: 5000);

        result.IsSuccess.Should().BeFalse();
        string.Join('\n', result.Errors ?? []).Should().Contain("timeRange cannot exceed");
        string.Join('\n', result.Errors ?? []).Should().Contain("maxRows");
        string.Join('\n', result.Errors ?? []).Should().Contain("Artifact type");
    }

    [Fact]
    public void ValidateIntentForPlan_WithForgedIntent_ShouldReject()
    {
        var service = CreateReadyService();
        var forged = new CloudSandboxGoalIntentDto(
            "csg_forged",
            "hash",
            ["devices"],
            new CloudSandboxGoalTimeRangeDto(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow),
            20,
            ["Markdown"],
            "DeviceList",
            [],
            [],
            true,
            true);

        var result = service.ValidateIntentForPlan(forged);

        result.IsSuccess.Should().BeFalse();
        string.Join('\n', result.Errors ?? []).Should().Contain("controlled sandbox intent gate");
    }

    [Fact]
    public async Task RunIntent_WithStoredIntent_ShouldReturnControlledMarkersAndHashes()
    {
        var client = new RecordingSandboxClient();
        var service = CreateReadyService(sandboxClient: client);
        var intent = service.CreateIntent("分析产能交付风险", ["Chart", "Markdown"], null, maxRows: 20).Value!;

        var result = await service.RunIntentAsync(
            intent,
            intent.ArtifactTypes,
            maxRows: 20,
            timeoutMs: 5000,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var value = result.Value!;
        value.Boundary.Should().Be(CloudReadonlySandboxControlledTrialMarkers.Boundary);
        value.Status.Should().Be(CloudReadonlySandboxControlledTrialStatuses.Completed);
        value.QueryResult.SourceType.Should().Be("CloudReadonly");
        value.QueryResult.SourceMode.Should().Be("CloudReadonlySandbox");
        value.QueryResult.IsSandbox.Should().BeTrue();
        value.QueryResult.IsSimulation.Should().BeFalse();
        value.QueryResult.SourceLabel.Should().Be("Cloud 只读 Sandbox（非生产）");
        value.QueryResult.Boundary.Should().Be("SandboxControlledTrial");
        value.QueryResult.EndpointCode.Should().Be("capacity_summary");
        value.QueryResult.QueryHash.Should().NotBeNullOrWhiteSpace();
        value.QueryResult.ResultHash.Should().NotBeNullOrWhiteSpace();
        value.QueryResult.Rows.Should().OnlyContain(row =>
            row.ContainsKey("boundary") &&
            row["boundary"] != null &&
            string.Equals(row["boundary"]!.ToString(), "SandboxControlledTrial", StringComparison.Ordinal));
        client.CallCount.Should().Be(1);

        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json.Should().NotContain("super-secret-token");
        json.Should().NotContain("ServiceAccountToken");
    }

    [Fact]
    public void ProductionCloudReadonlyTool_ShouldRemainDisabledAfterP8()
    {
        var productionTool = BuiltInToolRegistrations.AgentRuntimeTools
            .Should()
            .ContainSingle(item => item.ToolCode == "query_cloud_data_readonly")
            .Which;

        productionTool.IsEnabled.Should().BeFalse();
        productionTool.IsVisibleToPlanner.Should().BeFalse();
        productionTool.IsExecutableByAgent.Should().BeFalse();
    }

    private static CloudReadonlySandboxControlledTrialService CreateReadyService(
        ICloudReadonlySandboxClient? sandboxClient = null)
    {
        var history = new TestReadinessHistoryStore();
        history.Save(PassedSandboxSmoke());
        return CreateService(
            sandbox: EnabledSandbox(),
            fixedTrial: EnabledFixedTrial(),
            controlled: EnabledControlledTrial(),
            readinessHistory: history,
            sandboxClient: sandboxClient);
    }

    private static CloudReadonlySandboxControlledTrialService CreateService(
        CloudReadonlyOptions? cloudReadonly = null,
        CloudAiReadOptions? cloudAiRead = null,
        CloudReadonlySandboxOptions? sandbox = null,
        CloudReadonlySandboxAgentTrialOptions? fixedTrial = null,
        CloudReadonlySandboxControlledTrialOptions? controlled = null,
        ICloudReadonlyReadinessHistoryStore? readinessHistory = null,
        ICloudReadonlySandboxAgentTrialHistoryStore? trialHistory = null,
        ICloudReadonlySandboxControlledTrialIntentStore? intentStore = null,
        ICloudReadonlySandboxClient? sandboxClient = null)
    {
        var readinessStore = readinessHistory ?? new TestReadinessHistoryStore();
        var trialStore = trialHistory ?? new TestTrialHistoryStore();
        var client = sandboxClient ?? new RecordingSandboxClient();
        var fixedService = new CloudReadonlySandboxAgentTrialService(
            Options.Create(cloudReadonly ?? new CloudReadonlyOptions()),
            Options.Create(sandbox ?? new CloudReadonlySandboxOptions()),
            Options.Create(cloudAiRead ?? new CloudAiReadOptions()),
            Options.Create(fixedTrial ?? new CloudReadonlySandboxAgentTrialOptions()),
            readinessStore,
            trialStore,
            client);

        return new CloudReadonlySandboxControlledTrialService(
            Options.Create(cloudReadonly ?? new CloudReadonlyOptions()),
            Options.Create(sandbox ?? new CloudReadonlySandboxOptions()),
            Options.Create(cloudAiRead ?? new CloudAiReadOptions()),
            Options.Create(controlled ?? new CloudReadonlySandboxControlledTrialOptions()),
            readinessStore,
            trialStore,
            intentStore ?? new InMemoryCloudReadonlySandboxControlledTrialIntentStore(),
            client,
            fixedService);
    }

    private static CloudReadonlySandboxAgentTrialOptions EnabledFixedTrial() => new()
    {
        Enabled = true
    };

    private static CloudReadonlySandboxControlledTrialOptions EnabledControlledTrial() => new()
    {
        Enabled = true
    };

    private static CloudReadonlySandboxOptions EnabledSandbox() => new()
    {
        Enabled = true,
        BaseUrl = "http://sandbox-cloud.local",
        ServiceAccountToken = "super-secret-token",
        TimeoutSeconds = 10,
        DefaultPassStationTypeKey = "injection"
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
                    { "id": "p8-001", "deviceCode": "SANDBOX-DEVICE-1", "actualQty": 12 },
                    { "id": "p8-002", "deviceCode": "SANDBOX-DEVICE-2", "actualQty": 18 }
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
