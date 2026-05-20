using System.Net;
using System.Text.Json;
using AICopilot.AiGatewayService.CloudReadiness;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using Microsoft.Extensions.Options;

namespace AICopilot.BackendTests;

[Trait("Suite", "EnterpriseCloudReadonlySandboxP6")]
public sealed class EnterpriseCloudReadonlySandboxP6Tests
{
    [Fact]
    public void BuildCurrent_WithDefaultDisabledConfig_ShouldExposeSandboxStatus()
    {
        var service = CreateService();

        var report = service.BuildCurrent();

        report.Status.Should().Be(CloudReadonlyReadinessStatuses.ReadyForFake);
        report.Boundary.Should().Be("ReadinessOnly");
        report.CloudAiReadEnabled.Should().BeFalse();
        report.RealEnabled.Should().BeFalse();
        report.AllowProductionRead.Should().BeFalse();
        report.SandboxStatus.Should().NotBeNull();
        report.SandboxStatus!.Status.Should().Be(CloudReadonlyReadinessStatuses.NotConfigured);
        report.SandboxStatus.SandboxEnabled.Should().BeFalse();
        report.SandboxStatus.Boundary.Should().Be("SandboxSmokeOnly");
    }

    [Fact]
    public async Task RealSandboxSmoke_WithoutSandboxConfig_ShouldRemainPending_AndNotCallSandbox()
    {
        var client = new RecordingSandboxClient();
        var service = CreateService(sandboxClient: client);

        var report = await service.RunAsync(
            CloudReadonlyReadinessModes.RealSandboxSmoke,
            ["devices"],
            maxRows: 20,
            timeoutMs: 5000,
            CancellationToken.None);

        report.Status.Should().Be(CloudReadonlyReadinessStatuses.RealSandboxPending);
        report.Boundary.Should().Be("SandboxSmokeOnly");
        report.Checks.Should().ContainSingle().Which.Status.Should().Be("Skipped");
        report.SandboxStatus!.LastSmokeAt.Should().NotBeNull();
        client.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task RealSandboxSmoke_WithSandboxConfig_ShouldCallSandboxOnly_AndMaskToken()
    {
        var client = new RecordingSandboxClient();
        var service = CreateService(
            sandbox: EnabledSandbox(),
            sandboxClient: client);

        var report = await service.RunAsync(
            CloudReadonlyReadinessModes.RealSandboxSmoke,
            null,
            maxRows: 20,
            timeoutMs: 5000,
            CancellationToken.None);

        report.Status.Should().Be(CloudReadonlyReadinessStatuses.RealSandboxPassed);
        report.Boundary.Should().Be("SandboxSmokeOnly");
        report.CloudAiReadEnabled.Should().BeFalse();
        report.RealEnabled.Should().BeFalse();
        report.AllowProductionRead.Should().BeFalse();
        report.Checks.Should().HaveCount(4);
        report.Checks.Should().OnlyContain(check =>
            check.Status == "Passed" &&
            check.PolicyStatus == "Allowed" &&
            check.HttpStatus == 200 &&
            check.RowCount > 0 &&
            !string.IsNullOrWhiteSpace(check.ResultHash));
        report.SandboxStatus!.Status.Should().Be(CloudReadonlyReadinessStatuses.RealSandboxPassed);
        report.SandboxStatus.BaseUrlConfigured.Should().BeTrue();
        report.SandboxStatus.TokenConfigured.Should().BeTrue();
        client.CallCount.Should().Be(4);
        client.CapturedOptions.Should().OnlyContain(option => option.Enabled);

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json.Should().NotContain("super-secret-token");
        json.Should().NotContain("ServiceAccountToken");
    }

    [Fact]
    public async Task RealSandboxSmoke_ShouldBlockRecipeAndWritePath_WithoutCallingSandbox()
    {
        var client = new RecordingSandboxClient();
        var service = CreateService(
            sandbox: EnabledSandbox(),
            sandboxClient: client);

        var report = await service.RunAsync(
            CloudReadonlyReadinessModes.RealSandboxSmoke,
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
        client.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task RealSandboxSmoke_WithGlobalRealConfig_ShouldBlock_AndNotCallSandbox()
    {
        var client = new RecordingSandboxClient();
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
                BaseUrl = "http://real-cloud.local",
                ServiceAccountToken = "real-production-token"
            },
            sandbox: EnabledSandbox(),
            sandboxClient: client);

        var report = await service.RunAsync(
            CloudReadonlyReadinessModes.RealSandboxSmoke,
            ["devices"],
            maxRows: 20,
            timeoutMs: 5000,
            CancellationToken.None);

        report.Status.Should().Be(CloudReadonlyReadinessStatuses.Blocked);
        report.Errors.Should().Contain(error => error.Contains("CloudReadonly.Mode must remain Disabled"));
        report.Errors.Should().Contain(error => error.Contains("CloudAiRead.Enabled must remain false"));
        client.CallCount.Should().Be(0);

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json.Should().NotContain("real-production-token");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, CloudAiReadProblemCodes.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, CloudAiReadProblemCodes.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, CloudAiReadProblemCodes.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError, CloudAiReadProblemCodes.Unavailable)]
    public async Task RealSandboxSmoke_ShouldMapSandboxHttpErrors(
        HttpStatusCode statusCode,
        string expectedCode)
    {
        var service = CreateService(
            sandbox: EnabledSandbox(),
            sandboxClient: new FailingSandboxClient(new CloudAiReadException(
                expectedCode,
                "sandbox failed",
                statusCode)));

        var report = await service.RunAsync(
            CloudReadonlyReadinessModes.RealSandboxSmoke,
            ["devices"],
            maxRows: 20,
            timeoutMs: 5000,
            CancellationToken.None);

        report.Status.Should().Be(CloudReadonlyReadinessStatuses.Failed);
        report.Checks.Should().ContainSingle().Which.ErrorCode.Should().Be(expectedCode);
        report.Checks.Single().HttpStatus.Should().Be((int)statusCode);
    }

    [Fact]
    public async Task RealSandboxSmoke_ShouldMapInvalidJsonAsSchemaMismatch()
    {
        var service = CreateService(
            sandbox: EnabledSandbox(),
            sandboxClient: new FailingSandboxClient(new JsonException("invalid sandbox json")));

        var report = await service.RunAsync(
            CloudReadonlyReadinessModes.RealSandboxSmoke,
            ["devices"],
            maxRows: 20,
            timeoutMs: 5000,
            CancellationToken.None);

        report.Status.Should().Be(CloudReadonlyReadinessStatuses.Failed);
        report.Checks.Should().ContainSingle().Which.Status.Should().Be("SchemaMismatch");
    }

    [Fact]
    public void CloudReadonlyTool_ShouldRemainDisabledHiddenAndNonExecutable_AfterSandboxSmoke()
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
            sandboxClient ?? new RecordingSandboxClient());
    }

    private static CloudReadonlySandboxOptions EnabledSandbox() => new()
    {
        Enabled = true,
        BaseUrl = "http://sandbox-cloud.local",
        ServiceAccountToken = "super-secret-token",
        TimeoutSeconds = 10,
        DefaultPassStationTypeKey = "default"
    };

    private sealed class RecordingSandboxClient : ICloudReadonlySandboxClient
    {
        public int CallCount { get; private set; }

        public List<CloudReadonlySandboxOptions> CapturedOptions { get; } = [];

        public Task<JsonDocument> SendJsonAsync(
            CloudReadonlySandboxOptions options,
            HttpMethod method,
            string path,
            IReadOnlyDictionary<string, string?>? query = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            CapturedOptions.Add(options);

            return Task.FromResult(JsonDocument.Parse("""
                {
                  "items": [
                    { "id": "smoke-001", "deviceId": "READINESS-DEVICE", "deviceCode": "READINESS-DEVICE" },
                    { "id": "smoke-002", "deviceId": "READINESS-DEVICE", "deviceCode": "READINESS-DEVICE" }
                  ],
                  "isTruncated": false
                }
                """));
        }
    }

    private sealed class FailingSandboxClient(Exception exception) : ICloudReadonlySandboxClient
    {
        public Task<JsonDocument> SendJsonAsync(
            CloudReadonlySandboxOptions options,
            HttpMethod method,
            string path,
            IReadOnlyDictionary<string, string?>? query = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<JsonDocument>(exception);
        }
    }
}
