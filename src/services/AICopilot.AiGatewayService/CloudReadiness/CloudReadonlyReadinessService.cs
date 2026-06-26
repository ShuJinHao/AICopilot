using AICopilot.Services.Contracts;
using Microsoft.Extensions.Options;

namespace AICopilot.AiGatewayService.CloudReadiness;

public sealed class CloudReadonlyReadinessService(
    IOptions<CloudReadonlyOptions> cloudReadonlyOptions,
    IOptions<CloudReadonlySandboxOptions> cloudReadonlySandboxOptions,
    IOptions<CloudAiReadOptions> cloudAiReadOptions,
    ICloudReadonlySandboxClient cloudReadonlySandboxClient)
{
    public static string? NormalizeMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return CloudReadonlyReadinessModes.FakeEndpoint;
        }

        if (mode.Equals(CloudReadonlyReadinessModes.DryRun, StringComparison.OrdinalIgnoreCase))
        {
            return CloudReadonlyReadinessModes.DryRun;
        }

        if (mode.Equals(CloudReadonlyReadinessModes.FakeEndpoint, StringComparison.OrdinalIgnoreCase))
        {
            return CloudReadonlyReadinessModes.FakeEndpoint;
        }

        if (mode.Equals(CloudReadonlyReadinessModes.RealSandboxSmoke, StringComparison.OrdinalIgnoreCase))
        {
            return CloudReadonlyReadinessModes.RealSandboxSmoke;
        }

        return null;
    }

    public CloudReadonlyReadinessDto BuildCurrent()
    {
        var (errors, warnings) = CloudReadonlyReadinessPolicy.ValidateConfiguration(
            CloudReadonlyReadinessModes.DryRun,
            cloudReadonlyOptions.Value,
            cloudReadonlySandboxOptions.Value,
            cloudAiReadOptions.Value);
        return BuildReport(
            CloudReadonlyReadinessModes.DryRun,
            errors.Count == 0 ? CloudReadonlyReadinessStatuses.ReadyForFake : CloudReadonlyReadinessStatuses.Blocked,
            null,
            [],
            errors,
            warnings);
    }

    public CloudReadonlySandboxStatusDto BuildSandboxStatus(CloudReadonlyReadinessDto? latestSmoke = null)
    {
        var sandbox = cloudReadonlySandboxOptions.Value;
        var (errors, warnings) = CloudReadonlyReadinessPolicy.ValidateSandboxConfiguration(sandbox);
        if (latestSmoke?.Mode == CloudReadonlyReadinessModes.RealSandboxSmoke)
        {
            return latestSmoke.SandboxStatus ?? new CloudReadonlySandboxStatusDto(
                latestSmoke.Status,
                sandbox.Enabled,
                !string.IsNullOrWhiteSpace(sandbox.BaseUrl),
                !string.IsNullOrWhiteSpace(sandbox.ServiceAccountToken),
                latestSmoke.LastCheckedAt,
                latestSmoke.Checks,
                latestSmoke.Errors,
                latestSmoke.Warnings);
        }

        var status = errors.Count > 0
            ? CloudReadonlyReadinessStatuses.Blocked
            : sandbox.IsConfigured()
                ? CloudReadonlyReadinessStatuses.RealSandboxPending
                : CloudReadonlyReadinessStatuses.NotConfigured;

        return new CloudReadonlySandboxStatusDto(
            status,
            sandbox.Enabled,
            !string.IsNullOrWhiteSpace(sandbox.BaseUrl),
            !string.IsNullOrWhiteSpace(sandbox.ServiceAccountToken),
            null,
            [],
            errors,
            warnings);
    }

    public async Task<CloudReadonlyReadinessDto> RunAsync(
        string mode,
        IReadOnlyCollection<string>? endpointCodes,
        int maxRows,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var (errors, warnings) = CloudReadonlyReadinessPolicy.ValidateConfiguration(
            mode,
            cloudReadonlyOptions.Value,
            cloudReadonlySandboxOptions.Value,
            cloudAiReadOptions.Value);
        var effectiveMaxRows = Math.Clamp(maxRows, 1, 50);
        var effectiveTimeoutMs = Math.Clamp(timeoutMs, 500, 30_000);

        var specs = CloudReadonlyReadinessEndpointCatalog.ResolveEndpointSpecs(endpointCodes);
        var checks = new List<CloudAiReadEndpointCheckDto>();
        if (mode == CloudReadonlyReadinessModes.DryRun)
        {
            checks.AddRange(specs.Select(CloudReadonlyReadinessEndpointCatalog.BuildDryRunCheck));
        }
        else if (mode == CloudReadonlyReadinessModes.FakeEndpoint)
        {
            checks.AddRange(specs.Select(spec => CloudReadonlyReadinessEndpointCatalog.BuildFakeEndpointCheck(
                spec,
                effectiveMaxRows,
                effectiveTimeoutMs)));
        }
        else if (errors.Count > 0)
        {
            checks.AddRange(specs.Select(spec => CloudReadonlyReadinessEndpointCatalog.BuildSkippedSandboxCheck(
                spec,
                CloudAiReadProblemCodes.RequestBlocked)));
        }
        else
        {
            checks.AddRange(await CloudReadonlySandboxSmokeRunner.RunAsync(
                cloudReadonlySandboxClient,
                cloudReadonlySandboxOptions.Value,
                specs,
                effectiveMaxRows,
                effectiveTimeoutMs,
                cancellationToken));
        }

        var status = CloudReadonlyReadinessPolicy.ResolveStatus(mode, errors, checks);
        return BuildReport(
            mode,
            status,
            DateTimeOffset.UtcNow,
            checks,
            errors,
            warnings);
    }

    private CloudReadonlyReadinessDto BuildReport(
        string mode,
        string status,
        DateTimeOffset? checkedAt,
        IReadOnlyCollection<CloudAiReadEndpointCheckDto> checks,
        IReadOnlyCollection<string> errors,
        IReadOnlyCollection<string> warnings)
    {
        var cloudReadonly = cloudReadonlyOptions.Value;
        var cloudAiRead = cloudAiReadOptions.Value;
        var boundary = mode == CloudReadonlyReadinessModes.RealSandboxSmoke
            ? "SandboxSmokeOnly"
            : "ReadinessOnly";
        var sandboxStatus = mode == CloudReadonlyReadinessModes.RealSandboxSmoke
            ? new CloudReadonlySandboxStatusDto(
                status,
                cloudReadonlySandboxOptions.Value.Enabled,
                !string.IsNullOrWhiteSpace(cloudReadonlySandboxOptions.Value.BaseUrl),
                !string.IsNullOrWhiteSpace(cloudReadonlySandboxOptions.Value.ServiceAccountToken),
                checkedAt,
                checks,
                errors,
                warnings)
            : BuildSandboxStatus();

        return new CloudReadonlyReadinessDto(
            status,
            mode,
            cloudAiRead.Enabled,
            cloudReadonly.Real.Enabled,
            cloudReadonly.Real.AllowProductionRead,
            !string.IsNullOrWhiteSpace(cloudAiRead.BaseUrl),
            !string.IsNullOrWhiteSpace(cloudAiRead.ServiceAccountToken),
            checkedAt,
            checks,
            errors,
            warnings,
            boundary,
            sandboxStatus);
    }
}
