using System.Text.Json;
using AICopilot.AiGatewayService.CloudReadiness;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using Microsoft.Extensions.Options;

namespace AICopilot.BackendTests;

[Trait("Suite", "EnterpriseCloudReadonlyReadinessP5")]
public sealed class EnterpriseCloudReadonlyReadinessP5Tests
{
    [Fact]
    public void BuildCurrent_WithDefaultDisabledConfig_ShouldBeReadyForFake()
    {
        var service = CreateService();

        var report = service.BuildCurrent();

        report.Status.Should().Be(CloudReadonlyReadinessStatuses.ReadyForFake);
        report.Mode.Should().Be(CloudReadonlyReadinessModes.DryRun);
        report.Boundary.Should().Be("ReadinessOnly");
        report.CloudAiReadEnabled.Should().BeFalse();
        report.RealEnabled.Should().BeFalse();
        report.AllowProductionRead.Should().BeFalse();
        report.BaseUrlConfigured.Should().BeFalse();
        report.TokenConfigured.Should().BeFalse();
        report.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task RunFakeEndpoint_ShouldPassSupportedReadonlyContracts_AndReturnHashes()
    {
        var service = CreateService();

        var report = await service.RunAsync(
            CloudReadonlyReadinessModes.FakeEndpoint,
            null,
            maxRows: 20,
            timeoutMs: 5000,
            CancellationToken.None);

        report.Status.Should().Be(CloudReadonlyReadinessStatuses.FakePassed);
        report.LastCheckedAt.Should().NotBeNull();
        report.Checks.Should().HaveCount(4);
        report.Checks.Should().OnlyContain(check =>
            check.Status == "Passed" &&
            check.PolicyStatus == "Allowed" &&
            check.HttpStatus == 200 &&
            check.RowCount > 0 &&
            !string.IsNullOrWhiteSpace(check.ResultHash));
        report.Checks.Select(check => check.EndpointCode)
            .Should()
            .BeEquivalentTo(["devices", "capacity_summary", "device_logs", "pass_station_records"]);
    }

    [Fact]
    public async Task RunFakeEndpoint_ShouldBlockRecipeAndWritePath_ByPolicy()
    {
        var service = CreateService();

        var report = await service.RunAsync(
            CloudReadonlyReadinessModes.FakeEndpoint,
            ["recipe_versions", "write_path"],
            maxRows: 20,
            timeoutMs: 5000,
            CancellationToken.None);

        report.Status.Should().Be(CloudReadonlyReadinessStatuses.Blocked);
        report.Checks.Should().HaveCount(2);
        report.Checks.Should().OnlyContain(check =>
            check.Status == "BlockedByPolicy" &&
            check.PolicyStatus == "Blocked" &&
            check.ErrorCode == CloudAiReadProblemCodes.RequestBlocked);
    }

    [Fact]
    public async Task RunFakeEndpoint_ShouldMapFailureScenarios_WithoutSensitivePayload()
    {
        var service = CreateService(cloudAiRead: new CloudAiReadOptions
        {
            ServiceAccountToken = "super-secret-token"
        });

        var report = await service.RunAsync(
            CloudReadonlyReadinessModes.FakeEndpoint,
            ["http_401", "invalid_json", "timeout"],
            maxRows: 20,
            timeoutMs: 5000,
            CancellationToken.None);

        report.Status.Should().Be(CloudReadonlyReadinessStatuses.Failed);
        report.Checks.Select(check => check.ErrorCode)
            .Should()
            .Contain([CloudAiReadProblemCodes.Unauthorized, CloudAiReadProblemCodes.Unavailable]);

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json.Should().NotContain("super-secret-token");
        json.Should().NotContain("ServiceAccountToken");
    }

    [Fact]
    public async Task RealSandboxSmoke_WithoutExplicitRealConfig_ShouldRemainPending_AndNotCallCloud()
    {
        var client = new ThrowingCloudReadonlySandboxClient();
        var service = CreateService(sandboxClient: client);

        var report = await service.RunAsync(
            CloudReadonlyReadinessModes.RealSandboxSmoke,
            ["devices"],
            maxRows: 20,
            timeoutMs: 5000,
            CancellationToken.None);

        report.Status.Should().Be(CloudReadonlyReadinessStatuses.RealSandboxPending);
        report.Checks.Should().ContainSingle().Which.Status.Should().Be("Skipped");
        client.CallCount.Should().Be(0);
    }

    [Fact]
    public void CloudReadonlyTool_ShouldRemainDisabledHiddenAndNonExecutable_ForP5()
    {
        var tool = BuiltInToolRegistrations.AgentRuntimeTools
            .Should()
            .ContainSingle(item => item.ToolCode == "query_cloud_data_readonly")
            .Which;

        tool.IsEnabled.Should().BeFalse();
        tool.IsVisibleToPlanner.Should().BeFalse();
        tool.IsExecutableByAgent.Should().BeFalse();
        tool.ProviderType.Should().Be(ToolProviderType.CloudReadonly);
    }

    private static CloudReadonlyReadinessService CreateService(
        CloudReadonlyOptions? cloudReadonly = null,
        CloudAiReadOptions? cloudAiRead = null,
        CloudReadonlySandboxOptions? sandbox = null,
        ICloudReadonlySandboxClient? sandboxClient = null)
    {
        return new CloudReadonlyReadinessService(
            Options.Create(cloudReadonly ?? new CloudReadonlyOptions()),
            Options.Create(sandbox ?? new CloudReadonlySandboxOptions()),
            Options.Create(cloudAiRead ?? new CloudAiReadOptions()),
            sandboxClient ?? new ThrowingCloudReadonlySandboxClient());
    }

    private sealed class ThrowingCloudReadonlySandboxClient : ICloudReadonlySandboxClient
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
            throw new InvalidOperationException("P5 default readiness must not call a sandbox endpoint.");
        }
    }
}
