using System.Text.Json;
using AICopilot.AiGatewayService.CloudReadiness;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using Microsoft.Extensions.Options;

namespace AICopilot.BackendTests;

[Trait("Suite", "EnterpriseCloudReadonlyProductionControlledPilotP13")]
public sealed class EnterpriseCloudReadonlyProductionControlledPilotP13Tests
{
    [Fact]
    public void BuildStatus_WithDefaultConfig_ShouldKeepProductionControlledPilotDisabledAndToolClosed()
    {
        var service = CreateService();
        var status = service.BuildStatus(P12Ready(), ProtectedTools());
        var controlledTool = BuiltInToolRegistrations.AgentRuntimeTools
            .Should()
            .ContainSingle(item => item.ToolCode == CloudReadonlyProductionControlledPilotMarkers.ToolCode)
            .Which;

        status.Status.Should().Be(CloudReadonlyProductionControlledPilotStatuses.Disabled);
        status.Enabled.Should().BeFalse();
        status.FreeGoalEnabled.Should().BeFalse();
        status.ToolVisible.Should().BeFalse();
        status.ToolExecutable.Should().BeFalse();
        controlledTool.IsEnabled.Should().BeFalse();
        controlledTool.IsVisibleToPlanner.Should().BeFalse();
        controlledTool.IsExecutableByAgent.Should().BeFalse();
        controlledTool.DataBoundary.Should().Be(ToolDataBoundary.CloudReadonlyProductionControlledOnly);
        controlledTool.ApprovalPolicy.Should().Be("ProductionControlledPilotToolApproval");
    }

    [Fact]
    public async Task ReadyGate_ShouldCreateIntentAndRunWithProductionControlledMarkers()
    {
        var cloudClient = new FakeCloudAiReadClient();
        var service = CreateService(
            cloudAiRead: ConfiguredCloudAiRead(),
            controlled: new CloudReadonlyProductionControlledPilotOptions
            {
                Enabled = true,
                FreeGoalEnabled = true,
                AllowedEndpointCodes = ["devices", "capacity_summary", "device_logs", "pass_station_records"],
                MaxRows = 50
            },
            cloudAiReadClient: cloudClient);

        var intent = service.CreateIntent(
            "show device list",
            ["Markdown", "Html"],
            new CloudProductionGoalTimeRangeDto(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow),
            deviceId: null,
            passStationTypeKey: null,
            maxRows: 10,
            P12Ready(),
            ProtectedTools()).Value!;
        var result = await service.RunIntentAsync(
            intent.IntentId,
            ["Markdown", "Html"],
            10,
            5000,
            P12Ready(),
            ProtectedTools(),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.QueryResult.SourceType.Should().Be(CloudReadonlyProductionControlledPilotMarkers.SourceType);
        result.Value.QueryResult.SourceMode.Should().Be(CloudReadonlyProductionControlledPilotMarkers.SourceMode);
        result.Value.QueryResult.IsProductionData.Should().BeTrue();
        result.Value.QueryResult.IsSandbox.Should().BeFalse();
        result.Value.QueryResult.IsSimulation.Should().BeFalse();
        result.Value.QueryResult.SourceLabel.Should().Be(CloudReadonlyProductionControlledPilotMarkers.SourceLabel);
        result.Value.QueryResult.Boundary.Should().Be(CloudReadonlyProductionControlledPilotMarkers.Boundary);
        result.Value.QueryResult.IntentId.Should().Be(intent.IntentId);
        result.Value.QueryResult.EndpointCode.Should().Be("devices");
        result.Value.QueryResult.QueryHash.Should().NotBeNullOrWhiteSpace();
        result.Value.QueryResult.ResultHash.Should().NotBeNullOrWhiteSpace();
        result.Value.QueryResult.Rows.Should().OnlyContain(row =>
            Convert.ToString(row["sourceMode"]) == CloudReadonlyProductionControlledPilotMarkers.SourceMode &&
            Convert.ToString(row["boundary"]) == CloudReadonlyProductionControlledPilotMarkers.Boundary &&
            Convert.ToString(row["intentId"]) == intent.IntentId);
        cloudClient.LastQuery.Should().ContainKey("maxRows");
        cloudClient.LastQuery.Should().NotContainKey("intentId");
        cloudClient.LastQuery.Should().NotContainKey("goalHash");
        cloudClient.LastQuery.Should().NotContainKey("analysisType");
        cloudClient.LastQuery.Should().NotContainKey("from");
        cloudClient.LastQuery.Should().NotContainKey("to");
        cloudClient.LastQuery.Should().NotContainKey("boundary");
        cloudClient.LastQuery.Should().NotContainKey("pilotWindowId");

        var json = JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json.Should().Contain("CloudReadonlyProductionControlledPilot");
        json.Should().Contain("ProductionControlledPilot");
        json.Should().NotContain("redacted-test-token");
        json.Should().NotContain("ServiceAccountToken");
    }

    [Theory]
    [InlineData("device list", "devices", "DeviceList")]
    [InlineData("capacity delivery analysis", "capacity_summary", "CapacityDeliveryAnalysis")]
    [InlineData("device logs", "device_logs", "DeviceLogs")]
    [InlineData("pass station records", "pass_station_records", "PassStationRecords")]
    [InlineData("device alarm exception analysis", "device_logs", "DeviceExceptionAnalysis")]
    [InlineData("capacity summary", "capacity_summary", "CapacitySummary")]
    public void CreateIntent_ShouldMapAllowedFreeGoalsToReadonlyEndpoints(
        string goal,
        string endpointCode,
        string analysisType)
    {
        var service = CreateService(
            cloudAiRead: ConfiguredCloudAiRead(),
            controlled: new CloudReadonlyProductionControlledPilotOptions { Enabled = true, FreeGoalEnabled = true },
            cloudAiReadClient: new FakeCloudAiReadClient());

        var result = service.CreateIntent(
            goal,
            ["Markdown"],
            null,
            deviceId: Guid.NewGuid(),
            passStationTypeKey: endpointCode == "pass_station_records" ? "injection" : null,
            maxRows: 10,
            P12Ready(),
            ProtectedTools());

        result.IsSuccess.Should().BeTrue();
        result.Value!.EndpointCodes.Should().ContainSingle().Which.Should().Be(endpointCode);
        result.Value.AnalysisType.Should().Be(analysisType);
        result.Value.RequiresToolApproval.Should().BeTrue();
        result.Value.RequiresFinalApproval.Should().BeTrue();
    }

    [Theory]
    [InlineData("show recipe version history")]
    [InlineData("write device payload")]
    [InlineData("run SQL select * from devices")]
    [InlineData("unknown production payload")]
    public void CreateIntent_ShouldRejectBlockedOrUnknownGoals(string goal)
    {
        var service = CreateService(
            cloudAiRead: ConfiguredCloudAiRead(),
            controlled: new CloudReadonlyProductionControlledPilotOptions { Enabled = true, FreeGoalEnabled = true },
            cloudAiReadClient: new FakeCloudAiReadClient());

        var result = service.CreateIntent(
            goal,
            ["Markdown"],
            null,
            deviceId: Guid.NewGuid(),
            passStationTypeKey: null,
            maxRows: 10,
            P12Ready(),
            ProtectedTools());

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(error => error.ToString()!.Contains("BlockedByPolicy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateIntent_ShouldRejectRowsOutsideControlledLimit()
    {
        var service = CreateService(
            cloudAiRead: ConfiguredCloudAiRead(),
            controlled: new CloudReadonlyProductionControlledPilotOptions
            {
                Enabled = true,
                FreeGoalEnabled = true,
                MaxRows = 30
            },
            cloudAiReadClient: new FakeCloudAiReadClient());

        var result = service.CreateIntent(
            "device list",
            ["Markdown"],
            null,
            deviceId: null,
            passStationTypeKey: null,
            maxRows: 31,
            P12Ready(),
            ProtectedTools());

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(error => error.ToString()!.Contains("maxRows", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateIntent_ShouldRequireDeviceIdForDeviceScopedEndpoints()
    {
        var service = CreateService(
            cloudAiRead: ConfiguredCloudAiRead(),
            controlled: new CloudReadonlyProductionControlledPilotOptions { Enabled = true, FreeGoalEnabled = true },
            cloudAiReadClient: new FakeCloudAiReadClient());

        var result = service.CreateIntent(
            "capacity summary",
            ["Markdown"],
            null,
            deviceId: null,
            passStationTypeKey: null,
            maxRows: 10,
            P12Ready(),
            ProtectedTools());

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(error => error.ToString()!.Contains("deviceId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Gate_ShouldBlock_WhenPersistedProductionControlledToolIsEnabled()
    {
        var service = CreateService(
            cloudAiRead: ConfiguredCloudAiRead(),
            controlled: new CloudReadonlyProductionControlledPilotOptions { Enabled = true, FreeGoalEnabled = true },
            cloudAiReadClient: new FakeCloudAiReadClient());
        var unsafeTools = ProtectedTools()
            .Select(tool => tool.ToolCode == CloudReadonlyProductionControlledPilotMarkers.ToolCode
                ? CreateTool(tool.ToolCode, isEnabled: true, isVisibleToPlanner: true, isExecutableByAgent: true)
                : tool)
            .ToArray();

        var status = service.BuildStatus(P12Ready(), unsafeTools);

        status.Status.Should().Be(CloudReadonlyProductionControlledPilotStatuses.Blocked);
        status.Blockers.Should().Contain(item =>
            item.Contains("Persisted ToolRegistry unsafe", StringComparison.Ordinal) &&
            item.Contains(CloudReadonlyProductionControlledPilotMarkers.ToolCode, StringComparison.Ordinal));
    }

    private static CloudReadonlyProductionControlledPilotService CreateService(
        CloudReadonlyOptions? cloudReadonly = null,
        CloudAiReadOptions? cloudAiRead = null,
        CloudReadonlyProductionControlledPilotOptions? controlled = null,
        ICloudReadonlyProductionControlledPilotStore? store = null,
        ICloudAiReadClient? cloudAiReadClient = null)
    {
        return new CloudReadonlyProductionControlledPilotService(
            Options.Create(cloudReadonly ?? new CloudReadonlyOptions()),
            Options.Create(cloudAiRead ?? new CloudAiReadOptions()),
            Options.Create(controlled ?? new CloudReadonlyProductionControlledPilotOptions()),
            store ?? new InMemoryCloudReadonlyProductionControlledPilotStore(),
            cloudAiReadClient ?? new DisabledCloudAiReadClient());
    }

    private static CloudReadonlyProductionPilotStatusDto P12Ready() =>
        new(
            CloudReadonlyProductionPilotStatuses.Ready,
            Enabled: true,
            PilotWindowId: "p12-window-test",
            WindowStatus: CloudReadonlyProductionPilotWindowStatuses.Approved,
            AllowedEndpointCodes: ["devices", "capacity_summary", "device_logs", "pass_station_records"],
            ApprovalStatus: "Approved",
            ToolVisible: true,
            ToolExecutable: true,
            LastRunAt: DateTimeOffset.UtcNow,
            Blockers: [],
            Warnings: []);

    private static CloudAiReadOptions ConfiguredCloudAiRead() =>
        new()
        {
            Enabled = true,
            BaseUrl = "https://cloud.example.invalid",
            ServiceAccountToken = "redacted-test-token"
        };

    private static IReadOnlyCollection<ToolRegistration> ProtectedTools()
    {
        return ProtectedCloudReadonlyToolPolicy.ProtectedToolCodes
            .Select(code => CreateTool(code, isEnabled: false, isVisibleToPlanner: false, isExecutableByAgent: false))
            .ToArray();
    }

    private static ToolRegistration CreateTool(
        string toolCode,
        bool isEnabled,
        bool isVisibleToPlanner,
        bool isExecutableByAgent)
    {
        var definition = ProtectedCloudReadonlyToolPolicy.GetDefinition(toolCode)
                         ?? throw new InvalidOperationException($"Missing built-in tool definition {toolCode}.");
        return new ToolRegistration(
            definition.ToolCode,
            definition.DisplayName,
            definition.Description,
            definition.ProviderType,
            definition.TargetType,
            definition.TargetName,
            definition.InputSchemaJson,
            definition.OutputSchemaJson,
            definition.RiskLevel,
            definition.RequiredPermission,
            definition.RequiresApproval,
            isEnabled,
            definition.TimeoutSeconds,
            definition.AuditLevel,
            DateTimeOffset.UtcNow,
            definition.Category,
            definition.BusinessDomains,
            definition.DataBoundary,
            isVisibleToPlanner,
            isExecutableByAgent,
            definition.SchemaVersion,
            definition.CatalogVersion,
            definition.ApprovalPolicy);
    }

    private sealed class DisabledCloudAiReadClient : ICloudAiReadClient
    {
        public bool IsEnabled => false;

        public Task<JsonDocument> SendJsonAsync(HttpMethod method, string path, IReadOnlyDictionary<string, string?>? query = null, CancellationToken cancellationToken = default) =>
            throw new CloudAiReadException(CloudAiReadProblemCodes.NotConfigured, "CloudAiRead disabled.");

        public Task<CloudAiReadResult<CloudAiReadDeviceDto>> GetDevicesAsync(CloudAiReadQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CloudAiReadResult<CloudAiReadCapacitySummaryDto>> GetCapacitySummaryAsync(CloudAiReadQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CloudAiReadResult<CloudAiReadDeviceLogDto>> GetDeviceLogsAsync(CloudAiReadQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CloudAiReadResult<CloudAiReadPassStationRecordDto>> GetPassStationRecordsAsync(CloudAiReadQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CloudAiReadResult<object>> QuerySemanticAsync(SemanticQueryPlan plan, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeCloudAiReadClient : ICloudAiReadClient
    {
        public bool IsEnabled => true;

        public IReadOnlyDictionary<string, string?> LastQuery { get; private set; } =
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        public Task<JsonDocument> SendJsonAsync(HttpMethod method, string path, IReadOnlyDictionary<string, string?>? query = null, CancellationToken cancellationToken = default)
        {
            LastQuery = query ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            if (path.Contains("devices", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonDocument.Parse(
                    """{"items":[{"deviceCode":"D-001","status":"Running"},{"deviceCode":"D-002","status":"Idle"}],"isTruncated":false}"""));
            }

            return Task.FromResult(JsonDocument.Parse(
                """{"items":[{"recordId":"R-001","outputQty":120},{"recordId":"R-002","outputQty":95}],"isTruncated":false}"""));
        }

        public Task<CloudAiReadResult<CloudAiReadDeviceDto>> GetDevicesAsync(CloudAiReadQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CloudAiReadResult<CloudAiReadCapacitySummaryDto>> GetCapacitySummaryAsync(CloudAiReadQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CloudAiReadResult<CloudAiReadDeviceLogDto>> GetDeviceLogsAsync(CloudAiReadQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CloudAiReadResult<CloudAiReadPassStationRecordDto>> GetPassStationRecordsAsync(CloudAiReadQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CloudAiReadResult<object>> QuerySemanticAsync(SemanticQueryPlan plan, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
