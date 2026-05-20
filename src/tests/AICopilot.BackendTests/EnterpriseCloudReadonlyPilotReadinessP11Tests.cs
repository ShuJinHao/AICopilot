using System.Text.Json;
using AICopilot.AiGatewayService.CloudReadiness;
using AICopilot.AiGatewayService.TrialOperations;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Options;

namespace AICopilot.BackendTests;

[Trait("Suite", "EnterpriseCloudReadonlyPilotReadinessP11")]
public sealed class EnterpriseCloudReadonlyPilotReadinessP11Tests
{
    [Fact]
    public void BuildStatus_WithDefaultConfig_ShouldKeepPilotReadinessNotConfiguredAndProductionToolsClosed()
    {
        var service = CreateService();

        var status = service.BuildStatus();

        status.Status.Should().Be(CloudReadonlyPilotReadinessStatuses.NotConfigured);
        status.Enabled.Should().BeFalse();
        status.Blockers.Should().BeEmpty();
        status.Warnings.Should().Contain(item => item.Contains("Enabled is false", StringComparison.Ordinal));
        status.ContractCheckSummary.Total.Should().Be(0);

        var productionTool = BuiltInToolRegistrations.AgentRuntimeTools
            .Should()
            .ContainSingle(item => item.ToolCode == "query_cloud_data_readonly")
            .Which;
        productionTool.IsEnabled.Should().BeFalse();
        productionTool.IsVisibleToPlanner.Should().BeFalse();
        productionTool.IsExecutableByAgent.Should().BeFalse();

        var readinessTool = BuiltInToolRegistrations.AgentRuntimeTools
            .Should()
            .ContainSingle(item => item.ToolCode == CloudReadonlyPilotReadinessMarkers.ToolCode)
            .Which;
        readinessTool.DataBoundary.Should().Be(ToolDataBoundary.CloudReadonlyPilotReadinessOnly);
        readinessTool.ApprovalPolicy.Should().Be("PilotReadinessRehearsalOnly");
        readinessTool.IsEnabled.Should().BeFalse();
        readinessTool.IsVisibleToPlanner.Should().BeFalse();
        readinessTool.IsExecutableByAgent.Should().BeFalse();
    }

    [Fact]
    public void PilotPackageApprovalAndContractRehearsal_ShouldReachRehearsalPassedWithoutProductionData()
    {
        var store = new InMemoryCloudReadonlyPilotReadinessStore();
        var service = CreateService(
            pilot: new CloudReadonlyPilotReadinessOptions { Enabled = true },
            store: store);
        var package = service.CreatePackage(CreatePackageCommand(), EvidencePackage()).Value!;

        var approval = service.RunApprovalRehearsal(package.PackageId);
        var contract = service.RunContractRehearsal(
            package.PackageId,
            ["devices", "capacity_summary", "device_logs", "pass_station_records"],
            maxRows: 20,
            timeoutMs: 5000);
        var status = service.BuildStatus();

        approval.IsSuccess.Should().BeTrue();
        contract.IsSuccess.Should().BeTrue();
        status.Status.Should().Be(CloudReadonlyPilotReadinessStatuses.RehearsalPassed);
        contract.Value!.SourceMode.Should().Be(CloudReadonlyPilotReadinessMarkers.SourceMode);
        contract.Value.Boundary.Should().Be(CloudReadonlyPilotReadinessMarkers.Boundary);
        contract.Value.IsProductionData.Should().BeFalse();
        contract.Value.Checks.Should().OnlyContain(check => check.Status == "Passed");
        contract.Value.Checks.Should().OnlyContain(check => !string.IsNullOrWhiteSpace(check.ResultHash) && check.ResultHash.Length > 12);

        var json = JsonSerializer.Serialize(new { package, approval = approval.Value, contract = contract.Value, status }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json.Should().Contain("CloudReadonlyPilotReadiness");
        json.Should().Contain("PilotReadinessRehearsal");
        json.Should().NotContain("ServiceAccountToken");
        json.Should().NotContain("super-secret-token");
        json.Should().NotContain("Bearer ");
        json.Should().NotContain("SELECT ");
    }

    [Fact]
    public void ContractRehearsal_ShouldBlockRecipeWriteUnknownAndOutOfAllowlistEndpoints()
    {
        var service = CreateReadyService();
        var package = service.CreatePackage(CreatePackageCommand(["devices"]), EvidencePackage()).Value!;

        var result = service.RunContractRehearsal(
            package.PackageId,
            ["devices", "recipe", "recipe_versions", "write_path", "unknown_endpoint", "capacity_summary"],
            maxRows: 20,
            timeoutMs: 5000);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Checks.Should().Contain(check => check.EndpointCode == "devices" && check.Status == "Passed");
        result.Value.Checks.Should().Contain(check => check.EndpointCode == "recipe" && check.Status == "BlockedByPolicy");
        result.Value.Checks.Should().Contain(check => check.EndpointCode == "write_path" && check.Status == "BlockedByPolicy");
        result.Value.Checks.Should().Contain(check => check.EndpointCode == "capacity_summary" && check.Status == "BlockedByPolicy");
        result.Value.BlockedSamples.Should().NotBeEmpty();
    }

    [Fact]
    public void GateEvaluation_ShouldBlockWhenAnyProductionReadFlagIsEnabled()
    {
        var service = CreateService(
            cloudReadonly: new CloudReadonlyOptions
            {
                Mode = CloudReadonlyDataSourceMode.Real,
                Real = new CloudReadonlyRealOptions
                {
                    Enabled = true,
                    AllowProductionRead = true
                }
            },
            cloudAiRead: new CloudAiReadOptions
            {
                Enabled = true,
                BaseUrl = "https://cloud.example.invalid",
                ServiceAccountToken = "super-secret-token"
            },
            pilot: new CloudReadonlyPilotReadinessOptions { Enabled = true });

        var status = service.EvaluateGate(new PilotReadinessAssessmentDto(
            Guid.NewGuid(),
            "ReadyForP11Planning",
            [],
            [],
            [],
            new PilotReadinessMetricsDto(1, 1, 1, 0, 0, 1, 1),
            DateTimeOffset.UtcNow));

        status.Status.Should().Be(CloudReadonlyPilotReadinessStatuses.Blocked);
        status.Blockers.Should().Contain(item => item.Contains("CloudReadonly.Mode", StringComparison.Ordinal));
        status.Blockers.Should().Contain(item => item.Contains("AllowProductionRead", StringComparison.Ordinal));
        status.Blockers.Should().Contain(item => item.Contains("CloudAiRead.Enabled", StringComparison.Ordinal));
    }

    private static CloudReadonlyPilotReadinessService CreateReadyService()
    {
        return CreateService(pilot: new CloudReadonlyPilotReadinessOptions { Enabled = true });
    }

    private static CloudReadonlyPilotReadinessService CreateService(
        CloudReadonlyOptions? cloudReadonly = null,
        CloudAiReadOptions? cloudAiRead = null,
        CloudReadonlyPilotReadinessOptions? pilot = null,
        ICloudReadonlyPilotReadinessStore? store = null)
    {
        return new CloudReadonlyPilotReadinessService(
            Options.Create(cloudReadonly ?? new CloudReadonlyOptions()),
            Options.Create(cloudAiRead ?? new CloudAiReadOptions()),
            Options.Create(pilot ?? new CloudReadonlyPilotReadinessOptions()),
            store ?? new InMemoryCloudReadonlyPilotReadinessStore());
    }

    private static CreateCloudReadonlyPilotConfigPackageCommand CreatePackageCommand(
        IReadOnlyCollection<string>? endpointCodes = null)
    {
        return new CreateCloudReadonlyPilotConfigPackageCommand(
            Guid.NewGuid(),
            endpointCodes,
            MaxTimeRangeDays: 7,
            MaxRows: 20,
            TimeoutMs: 5000,
            ApprovalPolicy: "PilotReadinessRehearsal",
            RollbackPolicy: "DisablePilotConfigAndKeepProductionToolsClosed",
            OwnerDepartment: "AI Platform");
    }

    private static TrialEvidencePackageDto EvidencePackage()
    {
        return new TrialEvidencePackageDto(
            Guid.NewGuid(),
            "ReadyForP11Planning",
            [
                new TrialEvidenceMetricDto("scenario_runs", "Scenario runs", 2),
                new TrialEvidenceMetricDto("final_artifacts", "Final artifacts", 1)
            ],
            [
                new TrialEvidenceItemDto(
                    "AgentScenarioRun",
                    "SimulationBusiness",
                    "SimulationBusiness",
                    "Passed",
                    ["query-hash-sim", "result-hash-sim"],
                    Guid.NewGuid().ToString()),
                new TrialEvidenceItemDto(
                    "AgentScenarioRun",
                    "CloudReadonlySandbox",
                    "SandboxControlledTrial",
                    "Passed",
                    ["query-hash-sandbox", "result-hash-sandbox"],
                    Guid.NewGuid().ToString())
            ],
            [],
            ReportArtifactId: null,
            DateTimeOffset.UtcNow);
    }
}
