using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.CloudReadiness;

internal static class CloudReadonlyReadinessPolicy
{
    public static (List<string> Errors, List<string> Warnings) ValidateConfiguration(
        string mode,
        CloudReadonlyOptions cloudReadonly,
        CloudReadonlySandboxOptions sandbox,
        CloudAiReadOptions cloudAiRead)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (!Enum.IsDefined(cloudReadonly.Mode))
        {
            errors.Add("CloudReadonly.Mode must be Disabled, Simulation, or Real.");
        }

        if (!string.IsNullOrWhiteSpace(cloudAiRead.BaseUrl) &&
            (!Uri.TryCreate(cloudAiRead.BaseUrl, UriKind.Absolute, out var baseUri) ||
             (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps)))
        {
            errors.Add("CloudAiRead.BaseUrl must be an absolute HTTP/HTTPS URL when configured.");
        }

        if (cloudAiRead.TimeoutSeconds is < 1 or > 30)
        {
            errors.Add("CloudAiRead.TimeoutSeconds must be between 1 and 30.");
        }

        foreach (var path in cloudAiRead.ExplicitPostQueryPaths)
        {
            var decision = CloudAiReadEndpointPolicy.Evaluate(HttpMethod.Post, path, cloudAiRead.ExplicitPostQueryPaths);
            if (!decision.IsAllowed)
            {
                errors.Add($"CloudAiRead explicit POST path '{path}' is not readiness-safe: {decision.Reason}");
            }
        }

        var (sandboxErrors, sandboxWarnings) = ValidateSandboxConfiguration(sandbox);
        errors.AddRange(sandboxErrors);
        if (mode == CloudReadonlyReadinessModes.RealSandboxSmoke)
        {
            warnings.AddRange(sandboxWarnings);
        }

        if (mode != CloudReadonlyReadinessModes.RealSandboxSmoke)
        {
            if (cloudReadonly.Mode != CloudReadonlyDataSourceMode.Disabled)
            {
                errors.Add("CloudReadonly.Mode must remain Disabled during P5/P6 DryRun/FakeEndpoint readiness.");
            }

            if (cloudReadonly.Real.Enabled)
            {
                errors.Add("CloudReadonly.Real.Enabled must remain false by default during P5/P6 readiness.");
            }

            if (cloudReadonly.Real.AllowProductionRead)
            {
                errors.Add("CloudReadonly.Real.AllowProductionRead must remain false by default during P5/P6 readiness.");
            }

            if (cloudAiRead.Enabled)
            {
                errors.Add("CloudAiRead.Enabled must remain false by default during P5/P6 readiness.");
            }
        }
        else
        {
            if (cloudReadonly.Mode != CloudReadonlyDataSourceMode.Disabled)
            {
                errors.Add("CloudReadonly.Mode must remain Disabled during P6 sandbox smoke.");
            }

            if (cloudReadonly.Real.Enabled)
            {
                errors.Add("CloudReadonly.Real.Enabled must remain false during P6 sandbox smoke.");
            }

            if (cloudReadonly.Real.AllowProductionRead)
            {
                errors.Add("CloudReadonly.Real.AllowProductionRead must remain false during P6 sandbox smoke.");
            }

            if (cloudAiRead.Enabled)
            {
                errors.Add("CloudAiRead.Enabled must remain false during P6 sandbox smoke; use CloudReadonlySandbox only.");
            }

            if (!sandbox.IsConfigured())
            {
                warnings.Add("RealSandboxSmoke is pending because CloudReadonlySandbox is disabled or incomplete.");
            }
        }

        ValidateCloudReadonlyTool(errors);

        if (string.IsNullOrWhiteSpace(cloudAiRead.BaseUrl))
        {
            warnings.Add("CloudAiRead.BaseUrl is not configured; fake readiness does not require it.");
        }

        if (string.IsNullOrWhiteSpace(cloudAiRead.ServiceAccountToken))
        {
            warnings.Add("CloudAiRead token is not configured; token value is never returned by readiness APIs.");
        }

        return (errors, warnings);
    }

    public static (List<string> Errors, List<string> Warnings) ValidateSandboxConfiguration(
        CloudReadonlySandboxOptions sandbox)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (!string.IsNullOrWhiteSpace(sandbox.BaseUrl) &&
            (!Uri.TryCreate(sandbox.BaseUrl, UriKind.Absolute, out var baseUri) ||
             (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps)))
        {
            errors.Add("CloudReadonlySandbox.BaseUrl must be an absolute HTTP/HTTPS URL when configured.");
        }

        if (sandbox.TimeoutSeconds is < 1 or > 30)
        {
            errors.Add("CloudReadonlySandbox.TimeoutSeconds must be between 1 and 30.");
        }

        if (!CloudAiReadEndpointPolicy.IsSafeRouteSegment(sandbox.DefaultPassStationTypeKey))
        {
            errors.Add("CloudReadonlySandbox.DefaultPassStationTypeKey must be a single safe route segment.");
        }

        foreach (var path in sandbox.ExplicitPostQueryPaths)
        {
            var decision = CloudAiReadEndpointPolicy.Evaluate(HttpMethod.Post, path, sandbox.ExplicitPostQueryPaths);
            if (!decision.IsAllowed)
            {
                errors.Add($"CloudReadonlySandbox explicit POST path '{path}' is not sandbox-safe: {decision.Reason}");
            }
        }

        if (!sandbox.Enabled)
        {
            warnings.Add("CloudReadonlySandbox.Enabled is false; sandbox smoke stays disabled.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(sandbox.BaseUrl))
            {
                warnings.Add("CloudReadonlySandbox.BaseUrl is not configured.");
            }

            if (string.IsNullOrWhiteSpace(sandbox.ServiceAccountToken))
            {
                warnings.Add("CloudReadonlySandbox token is not configured; token value is never returned by readiness APIs.");
            }
        }

        return (errors, warnings);
    }

    public static string ResolveStatus(
        string mode,
        IReadOnlyCollection<string> errors,
        IReadOnlyCollection<CloudAiReadEndpointCheckDto> checks)
    {
        if (errors.Count > 0)
        {
            return CloudReadonlyReadinessStatuses.Blocked;
        }

        if (checks.Any(check => check.Status == "BlockedByPolicy"))
        {
            return CloudReadonlyReadinessStatuses.Blocked;
        }

        if (checks.Any(check => check.Status is "Failed" or "Timeout" or "SchemaMismatch"))
        {
            return CloudReadonlyReadinessStatuses.Failed;
        }

        if (mode == CloudReadonlyReadinessModes.RealSandboxSmoke)
        {
            return checks.Any(check => check.Status == "Skipped")
                ? CloudReadonlyReadinessStatuses.RealSandboxPending
                : CloudReadonlyReadinessStatuses.RealSandboxPassed;
        }

        if (mode == CloudReadonlyReadinessModes.FakeEndpoint)
        {
            return CloudReadonlyReadinessStatuses.FakePassed;
        }

        return CloudReadonlyReadinessStatuses.ReadyForFake;
    }

    private static void ValidateCloudReadonlyTool(ICollection<string> errors)
    {
        var cloudReadonlyTool = BuiltInToolRegistrations.AgentRuntimeTools
            .FirstOrDefault(tool => tool.ToolCode == "query_cloud_data_readonly");
        if (cloudReadonlyTool is null)
        {
            errors.Add("Tool Registry is missing query_cloud_data_readonly.");
        }
        else
        {
            if (cloudReadonlyTool.IsEnabled)
            {
                errors.Add("query_cloud_data_readonly must remain disabled by default in P5/P6.");
            }

            if (cloudReadonlyTool.IsVisibleToPlanner)
            {
                errors.Add("query_cloud_data_readonly must not be visible to Planner in P5/P6.");
            }

            if (cloudReadonlyTool.IsExecutableByAgent)
            {
                errors.Add("query_cloud_data_readonly must not be executable by Agent in P5/P6.");
            }
        }
    }
}
