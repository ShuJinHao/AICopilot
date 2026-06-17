using System.Text.Json;
using AICopilot.AiGatewayService.CloudReadiness;
using AICopilot.AiGatewayService.TrialOperations;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using Microsoft.Extensions.Options;

namespace AICopilot.BackendTests;

[Trait("Suite", "EnterpriseCloudReadonlyProductionPilotP12")]
public sealed class EnterpriseCloudReadonlyProductionPilotP12Tests
{
    [Fact]
    public void BuildStatus_WithDefaultConfig_ShouldKeepProductionPilotDisabledAndToolClosed()
    {
        var service = CreateService();
        var p11Status = new CloudReadonlyPilotReadinessStatusDto(
            CloudReadonlyPilotReadinessStatuses.RehearsalPassed,
            Enabled: true,
            EvidencePackageId: "campaign:test",
            ConfigSummary: null,
            ApprovalRehearsalStatus: "Passed",
            ContractCheckSummary: new CloudReadonlyPilotContractCheckSummaryDto(4, 4, 0, 0, DateTimeOffset.UtcNow),
            Blockers: [],
            Warnings: [],
            LastCheckedAt: DateTimeOffset.UtcNow);

        var status = service.BuildStatus(p11Status, ProtectedTools());
        var pilotTool = BuiltInToolRegistrations.AgentRuntimeTools
            .Should()
            .ContainSingle(item => item.ToolCode == CloudReadonlyProductionPilotMarkers.ToolCode)
            .Which;

        status.Status.Should().Be(CloudReadonlyProductionPilotStatuses.Disabled);
        status.Enabled.Should().BeFalse();
        status.ToolVisible.Should().BeFalse();
        status.ToolExecutable.Should().BeFalse();
        pilotTool.IsEnabled.Should().BeFalse();
        pilotTool.IsVisibleToPlanner.Should().BeFalse();
        pilotTool.IsExecutableByAgent.Should().BeFalse();
        pilotTool.DataBoundary.Should().Be(ToolDataBoundary.CloudReadonlyProductionPilotOnly);
        pilotTool.ApprovalPolicy.Should().Be("ProductionPilotToolApproval");
    }

    [Fact]
    public async Task ApprovedWindow_ShouldRunFixedScenarioWithProductionPilotMarkers()
    {
        var cloudClient = new FakeCloudAiReadClient();
        var service = CreateService(
            cloudAiRead: new CloudAiReadOptions
            {
                Enabled = true,
                BaseUrl = "https://cloud.example.invalid",
                ServiceAccountToken = "redacted-test-token"
            },
            pilot: new CloudReadonlyProductionPilotOptions { Enabled = true },
            cloudAiReadClient: cloudClient);
        var p11Status = RehearsalPassed();
        var window = service.CreateWindow(
            new CreateCloudReadonlyProductionPilotWindowCommand(
                AllowedEndpointCodes: ["devices", "capacity_summary"],
                MaxRows: 20,
                TimeoutMs: 5000),
            p11Status,
            ProtectedTools()).Value!;
        service.UpdateWindowStatus(window.WindowId, CloudReadonlyProductionPilotWindowStatuses.Approved).IsSuccess.Should().BeTrue();

        var status = service.BuildStatus(p11Status, ProtectedTools());
        var result = await service.RunScenarioAsync(
            new RunCloudReadonlyProductionPilotScenarioCommand(
                "cloud-production-pilot-devices",
                ["Markdown", "Html"],
                window.WindowId,
                new CloudProductionPilotTimeRangeDto(DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow),
                MaxRows: 10,
                TimeoutMs: 5000),
            p11Status,
            ProtectedTools(),
            CancellationToken.None);

        status.Status.Should().Be(CloudReadonlyProductionPilotStatuses.Ready);
        status.ToolVisible.Should().BeTrue();
        status.ToolExecutable.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
        result.Value!.QueryResult.SourceType.Should().Be(CloudReadonlyProductionPilotMarkers.SourceType);
        result.Value.QueryResult.SourceMode.Should().Be(CloudReadonlyProductionPilotMarkers.SourceMode);
        result.Value.QueryResult.IsProductionData.Should().BeTrue();
        result.Value.QueryResult.IsSandbox.Should().BeFalse();
        result.Value.QueryResult.IsSimulation.Should().BeFalse();
        result.Value.QueryResult.SourceLabel.Should().Be(CloudReadonlyProductionPilotMarkers.SourceLabel);
        result.Value.QueryResult.Boundary.Should().Be(CloudReadonlyProductionPilotMarkers.Boundary);
        result.Value.QueryResult.PilotWindowId.Should().Be(window.WindowId);
        result.Value.QueryResult.EndpointCode.Should().Be("devices");
        result.Value.QueryResult.QueryHash.Should().NotBeNullOrWhiteSpace();
        result.Value.QueryResult.ResultHash.Should().NotBeNullOrWhiteSpace();
        result.Value.QueryResult.Rows.Should().OnlyContain(row =>
            Convert.ToString(row["sourceMode"]) == CloudReadonlyProductionPilotMarkers.SourceMode &&
            Convert.ToString(row["boundary"]) == CloudReadonlyProductionPilotMarkers.Boundary);
        cloudClient.LastQuery.Should().ContainKey("maxRows");
        cloudClient.LastQuery.Should().NotContainKey("scenarioId");
        cloudClient.LastQuery.Should().NotContainKey("from");
        cloudClient.LastQuery.Should().NotContainKey("to");
        cloudClient.LastQuery.Should().NotContainKey("boundary");
        cloudClient.LastQuery.Should().NotContainKey("pilotWindowId");

        var json = JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json.Should().Contain("CloudReadonlyProductionPilot");
        json.Should().Contain("ProductionPilot");
        json.Should().NotContain("redacted-test-token");
        json.Should().NotContain("ServiceAccountToken");
    }

    [Fact]
    public async Task RunScenario_ShouldRejectWhenP11GateIsNotPassedOrEndpointIsOutOfWindow()
    {
        var service = CreateService(
            cloudAiRead: new CloudAiReadOptions
            {
                Enabled = true,
                BaseUrl = "https://cloud.example.invalid",
                ServiceAccountToken = "redacted-test-token"
            },
            pilot: new CloudReadonlyProductionPilotOptions { Enabled = true },
            cloudAiReadClient: new FakeCloudAiReadClient());
        var notPassed = RehearsalPassed() with { Status = CloudReadonlyPilotReadinessStatuses.Blocked };

        var blocked = await service.RunScenarioAsync(
            new RunCloudReadonlyProductionPilotScenarioCommand("cloud-production-pilot-devices"),
            notPassed,
            ProtectedTools(),
            CancellationToken.None);

        blocked.IsSuccess.Should().BeFalse();

        var window = service.CreateWindow(
            new CreateCloudReadonlyProductionPilotWindowCommand(AllowedEndpointCodes: ["devices"]),
            RehearsalPassed(),
            ProtectedTools()).Value!;
        service.UpdateWindowStatus(window.WindowId, CloudReadonlyProductionPilotWindowStatuses.Approved);

        var outOfAllowlist = await service.RunScenarioAsync(
            new RunCloudReadonlyProductionPilotScenarioCommand(
                "cloud-production-pilot-capacity-summary",
                PilotWindowId: window.WindowId),
            RehearsalPassed(),
            ProtectedTools(),
            CancellationToken.None);

        outOfAllowlist.IsSuccess.Should().BeFalse();
        outOfAllowlist.Errors.Should().Contain(error => error.ToString()!.Contains("allowlist", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunScenario_ShouldRequireDeviceIdForDeviceScopedEndpoints()
    {
        var service = CreateService(
            cloudAiRead: new CloudAiReadOptions
            {
                Enabled = true,
                BaseUrl = "https://cloud.example.invalid",
                ServiceAccountToken = "redacted-test-token"
            },
            pilot: new CloudReadonlyProductionPilotOptions { Enabled = true },
            cloudAiReadClient: new FakeCloudAiReadClient());

        var window = service.CreateWindow(
            new CreateCloudReadonlyProductionPilotWindowCommand(AllowedEndpointCodes: ["capacity_summary"]),
            RehearsalPassed(),
            ProtectedTools()).Value!;
        service.UpdateWindowStatus(window.WindowId, CloudReadonlyProductionPilotWindowStatuses.Approved);

        var missingDevice = await service.RunScenarioAsync(
            new RunCloudReadonlyProductionPilotScenarioCommand(
                "cloud-production-pilot-capacity-summary",
                PilotWindowId: window.WindowId),
            RehearsalPassed(),
            ProtectedTools(),
            CancellationToken.None);

        missingDevice.IsSuccess.Should().BeFalse();
        missingDevice.Errors.Should().Contain(error => error.ToString()!.Contains("deviceId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Gate_ShouldBlock_WhenPersistedProductionPilotToolIsEnabled()
    {
        var service = CreateService(
            pilot: new CloudReadonlyProductionPilotOptions { Enabled = true },
            cloudAiRead: new CloudAiReadOptions
            {
                Enabled = true,
                BaseUrl = "https://cloud.example.invalid",
                ServiceAccountToken = "redacted-test-token"
            },
            cloudAiReadClient: new FakeCloudAiReadClient());
        var unsafeTools = ProtectedTools()
            .Select(tool => tool.ToolCode == CloudReadonlyProductionPilotMarkers.ToolCode
                ? CreateTool(tool.ToolCode, isEnabled: true, isVisibleToPlanner: true, isExecutableByAgent: true)
                : tool)
            .ToArray();

        var status = service.BuildStatus(RehearsalPassed(), unsafeTools);

        status.Status.Should().Be(CloudReadonlyProductionPilotStatuses.Blocked);
        status.Blockers.Should().Contain(item =>
            item.Contains("Persisted ToolRegistry unsafe", StringComparison.Ordinal) &&
            item.Contains(CloudReadonlyProductionPilotMarkers.ToolCode, StringComparison.Ordinal));
    }

    private static CloudReadonlyProductionPilotService CreateService(
        CloudReadonlyOptions? cloudReadonly = null,
        CloudAiReadOptions? cloudAiRead = null,
        CloudReadonlyProductionPilotOptions? pilot = null,
        ICloudReadonlyProductionPilotStore? store = null,
        ICloudAiReadClient? cloudAiReadClient = null)
    {
        return new CloudReadonlyProductionPilotService(
            Options.Create(cloudReadonly ?? new CloudReadonlyOptions()),
            Options.Create(cloudAiRead ?? new CloudAiReadOptions()),
            Options.Create(pilot ?? new CloudReadonlyProductionPilotOptions()),
            store ?? new InMemoryCloudReadonlyProductionPilotStore(),
            cloudAiReadClient ?? new DisabledCloudAiReadClient());
    }

    private static CloudReadonlyPilotReadinessStatusDto RehearsalPassed()
    {
        return new CloudReadonlyPilotReadinessStatusDto(
            CloudReadonlyPilotReadinessStatuses.RehearsalPassed,
            Enabled: true,
            EvidencePackageId: "campaign:test",
            ConfigSummary: null,
            ApprovalRehearsalStatus: "Passed",
            ContractCheckSummary: new CloudReadonlyPilotContractCheckSummaryDto(4, 4, 0, 0, DateTimeOffset.UtcNow),
            Blockers: [],
            Warnings: [],
            LastCheckedAt: DateTimeOffset.UtcNow);
    }

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
