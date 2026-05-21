using System.Text.Json;
using AICopilot.AiGatewayService.CloudReadiness;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using Microsoft.Extensions.Options;

namespace AICopilot.BackendTests;

[Trait("Suite", "EnterpriseCloudReadonlyProductionOperationsP14")]
public sealed class EnterpriseCloudReadonlyProductionOperationsP14Tests
{
    [Fact]
    public async Task EmergencyStop_ShouldBlockP12AndP13ProductionPilotExecution()
    {
        var fixture = CreateFixture();
        var window = fixture.CreateApprovedWindow();
        var readyP12 = fixture.P12.BuildStatus(RehearsalPassed(), ProtectedTools());

        readyP12.Status.Should().Be(CloudReadonlyProductionPilotStatuses.Ready);

        fixture.Operations.ActivateEmergencyStop("p14 drill", "tester");
        var stoppedP12 = fixture.P12.BuildStatus(RehearsalPassed(), ProtectedTools());
        var stoppedP13 = fixture.P13.BuildStatus(stoppedP12, ProtectedTools());

        stoppedP12.Status.Should().Be(CloudReadonlyProductionPilotStatuses.EmergencyStopped);
        stoppedP12.ToolExecutable.Should().BeFalse();
        stoppedP13.Status.Should().Be(CloudReadonlyProductionControlledPilotStatuses.EmergencyStopped);
        stoppedP13.ToolExecutable.Should().BeFalse();

        var p12Run = await fixture.P12.RunScenarioAsync(
            new RunCloudReadonlyProductionPilotScenarioCommand(
                "cloud-production-pilot-devices",
                PilotWindowId: window.WindowId),
            RehearsalPassed(),
            ProtectedTools(),
            CancellationToken.None);
        var p13Intent = fixture.P13.CreateIntent("device list", ["Markdown"], null, 10, stoppedP12, ProtectedTools());

        p12Run.IsSuccess.Should().BeFalse();
        p12Run.Errors.Should().Contain(error => error.ToString()!.Contains("EmergencyStopped", StringComparison.OrdinalIgnoreCase));
        p13Intent.IsSuccess.Should().BeFalse();
        p13Intent.Errors.Should().Contain(error => error.ToString()!.Contains("EmergencyStopped", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ClearingEmergencyStop_ShouldNotBypassOriginalGate()
    {
        var fixture = CreateFixture(
            p12Options: new CloudReadonlyProductionPilotOptions(),
            p13Options: new CloudReadonlyProductionControlledPilotOptions());

        fixture.Operations.ActivateEmergencyStop("p14 drill", "tester");
        fixture.Operations.ClearEmergencyStop("drill completed", "tester");

        fixture.Operations.GetEmergencyStop().DrillCompleted.Should().BeTrue();
        var p12 = fixture.P12.BuildStatus(RehearsalPassed(), ProtectedTools());
        var p13 = fixture.P13.BuildStatus(p12, ProtectedTools());

        p12.Status.Should().Be(CloudReadonlyProductionPilotStatuses.Disabled);
        p12.ToolExecutable.Should().BeFalse();
        p13.Status.Should().Be(CloudReadonlyProductionControlledPilotStatuses.Disabled);
        p13.ToolExecutable.Should().BeFalse();
    }

    [Fact]
    public async Task OperationsLedger_ShouldSummarizeP12AndP13RunsWithoutSecrets()
    {
        var fixture = CreateFixture();
        var window = fixture.CreateApprovedWindow();
        var p12Status = fixture.P12.BuildStatus(RehearsalPassed(), ProtectedTools());

        await fixture.P12.RunScenarioAsync(
            new RunCloudReadonlyProductionPilotScenarioCommand(
                "cloud-production-pilot-devices",
                PilotWindowId: window.WindowId,
                MaxRows: 10),
            RehearsalPassed(),
            ProtectedTools(),
            CancellationToken.None);
        var intent = fixture.P13.CreateIntent(
            "device list",
            ["Markdown", "Html"],
            null,
            10,
            p12Status,
            ProtectedTools()).Value!;
        await fixture.P13.RunIntentAsync(
            intent.IntentId,
            ["Markdown", "Html"],
            10,
            5000,
            p12Status,
            ProtectedTools(),
            CancellationToken.None);

        var ledger = fixture.Operations.BuildLedger();
        var status = fixture.Operations.BuildStatus(
            fixture.P12.BuildStatus(RehearsalPassed(), ProtectedTools()),
            fixture.P13.BuildStatus(fixture.P12.BuildStatus(RehearsalPassed(), ProtectedTools()), ProtectedTools()));
        var json = JsonSerializer.Serialize(new { ledger, status }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        ledger.Should().HaveCount(2);
        ledger.Should().Contain(item => item.SourceMode == CloudReadonlyProductionPilotMarkers.SourceMode);
        ledger.Should().Contain(item => item.SourceMode == CloudReadonlyProductionControlledPilotMarkers.SourceMode);
        ledger.Should().OnlyContain(item => !string.IsNullOrWhiteSpace(item.QueryHash) && !string.IsNullOrWhiteSpace(item.ResultHash));
        status.RunMetrics.TotalRuns.Should().Be(2);
        status.RunMetrics.TotalRows.Should().BeGreaterThan(0);
        json.Should().Contain("CloudReadonlyProductionPilot");
        json.Should().Contain("CloudReadonlyProductionControlledPilot");
        json.Should().NotContain("redacted-test-token");
        json.Should().NotContain("ServiceAccountToken");
        json.Contains("payload", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
    }

    [Fact]
    public async Task GaReadiness_ShouldBlockWithOpenCriticalIncident_AndReadyAfterIncidentResolved()
    {
        var fixture = CreateFixture();
        var window = fixture.CreateApprovedWindow();
        var p12Ready = fixture.P12.BuildStatus(RehearsalPassed(), ProtectedTools());
        await fixture.P12.RunScenarioAsync(
            new RunCloudReadonlyProductionPilotScenarioCommand(
                "cloud-production-pilot-devices",
                PilotWindowId: window.WindowId,
                MaxRows: 10),
            RehearsalPassed(),
            ProtectedTools(),
            CancellationToken.None);
        fixture.Operations.ActivateEmergencyStop("p14 drill", "tester");
        fixture.Operations.ClearEmergencyStop("drill completed", "tester");

        var incident = fixture.Operations.UpsertIncident(new UpsertProductionPilotIncidentCommand(
            null,
            "Critical",
            "Security",
            ProductionPilotIncidentStatuses.Open,
            "PilotOps",
            "run:p12",
            null));
        var blocked = fixture.Operations.BuildGaReadinessAssessment(
            fixture.P12.BuildStatus(RehearsalPassed(), ProtectedTools()),
            fixture.P13.BuildStatus(p12Ready, ProtectedTools()),
            ProtectedTools());

        blocked.Status.Should().Be(CloudReadonlyProductionOperationsStatuses.Blocked);
        blocked.Blockers.Should().Contain(item => item.Contains("incidents", StringComparison.OrdinalIgnoreCase));

        fixture.Operations.UpsertIncident(new UpsertProductionPilotIncidentCommand(
            incident.IncidentId,
            "Critical",
            "Security",
            ProductionPilotIncidentStatuses.Resolved,
            "PilotOps",
            "run:p12",
            "sha256:resolved"));
        var ready = fixture.Operations.BuildGaReadinessAssessment(
            fixture.P12.BuildStatus(RehearsalPassed(), ProtectedTools()),
            fixture.P13.BuildStatus(p12Ready, ProtectedTools()),
            ProtectedTools());

        ready.Status.Should().Be(CloudReadonlyProductionOperationsStatuses.ReadyForP15Planning);
        ready.Checks.Should().Contain(item => item.Code == "EmergencyStopDrill" && item.Status == "Passed");
        ready.Checks.Should().Contain(item => item.Code == "AuditLedger" && item.Status == "Passed");
    }

    private static OperationsFixture CreateFixture(
        CloudReadonlyProductionPilotOptions? p12Options = null,
        CloudReadonlyProductionControlledPilotOptions? p13Options = null)
    {
        var operationsStore = new InMemoryProductionPilotOperationsStore();
        var p12Store = new InMemoryCloudReadonlyProductionPilotStore();
        var p13Store = new InMemoryCloudReadonlyProductionControlledPilotStore();
        var fakeClient = new FakeCloudAiReadClient();
        var p12 = new CloudReadonlyProductionPilotService(
            Options.Create(new CloudReadonlyOptions()),
            Options.Create(ConfiguredCloudAiRead()),
            Options.Create(p12Options ?? new CloudReadonlyProductionPilotOptions { Enabled = true }),
            p12Store,
            fakeClient,
            operationsStore);
        var p13 = new CloudReadonlyProductionControlledPilotService(
            Options.Create(new CloudReadonlyOptions()),
            Options.Create(ConfiguredCloudAiRead()),
            Options.Create(p13Options ?? new CloudReadonlyProductionControlledPilotOptions { Enabled = true, FreeGoalEnabled = true }),
            p13Store,
            fakeClient,
            operationsStore);
        var operations = new CloudReadonlyProductionOperationsService(operationsStore, p12Store, p13Store);
        return new OperationsFixture(p12, p13, operations);
    }

    private static CloudReadonlyPilotReadinessStatusDto RehearsalPassed() =>
        new(
            CloudReadonlyPilotReadinessStatuses.RehearsalPassed,
            Enabled: true,
            EvidencePackageId: "campaign:test",
            ConfigSummary: null,
            ApprovalRehearsalStatus: "Passed",
            ContractCheckSummary: new CloudReadonlyPilotContractCheckSummaryDto(4, 4, 0, 0, DateTimeOffset.UtcNow),
            Blockers: [],
            Warnings: [],
            LastCheckedAt: DateTimeOffset.UtcNow);

    private static CloudAiReadOptions ConfiguredCloudAiRead() =>
        new()
        {
            Enabled = true,
            BaseUrl = "https://cloud.example.invalid",
            ServiceAccountToken = "redacted-test-token"
        };

    private static IReadOnlyCollection<ToolRegistration> ProtectedTools() =>
        ProtectedCloudReadonlyToolPolicy.ProtectedToolCodes
            .Select(code => CreateTool(code, isEnabled: false, isVisibleToPlanner: false, isExecutableByAgent: false))
            .ToArray();

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

    private sealed record OperationsFixture(
        CloudReadonlyProductionPilotService P12,
        CloudReadonlyProductionControlledPilotService P13,
        CloudReadonlyProductionOperationsService Operations)
    {
        public CloudReadonlyProductionPilotWindowDto CreateApprovedWindow()
        {
            var window = P12.CreateWindow(
                new CreateCloudReadonlyProductionPilotWindowCommand(
                    AllowedEndpointCodes: ["devices", "capacity_summary", "device_logs", "pass_station_records"],
                    MaxRows: 50,
                    TimeoutMs: 5000),
                RehearsalPassed(),
                ProtectedTools()).Value!;
            P12.UpdateWindowStatus(window.WindowId, CloudReadonlyProductionPilotWindowStatuses.Approved).IsSuccess.Should().BeTrue();
            return window;
        }
    }

    private sealed class FakeCloudAiReadClient : ICloudAiReadClient
    {
        public bool IsEnabled => true;

        public Task<JsonDocument> SendJsonAsync(HttpMethod method, string path, IReadOnlyDictionary<string, string?>? query = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(JsonDocument.Parse(
                """{"items":[{"deviceCode":"D-001","status":"Running"},{"deviceCode":"D-002","status":"Idle"}],"isTruncated":false}"""));

        public Task<CloudAiReadResult<CloudAiReadDeviceDto>> GetDevicesAsync(CloudAiReadQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CloudAiReadResult<CloudAiReadCapacitySummaryDto>> GetCapacitySummaryAsync(CloudAiReadQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CloudAiReadResult<CloudAiReadDeviceLogDto>> GetDeviceLogsAsync(CloudAiReadQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CloudAiReadResult<CloudAiReadPassStationRecordDto>> GetPassStationRecordsAsync(CloudAiReadQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CloudAiReadResult<object>> QuerySemanticAsync(SemanticQueryPlan plan, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
