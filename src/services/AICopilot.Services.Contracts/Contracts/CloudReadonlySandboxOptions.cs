using System.Net.Http;

namespace AICopilot.Services.Contracts;

public sealed class CloudReadonlySandboxOptions
{
    public const string SectionName = "CloudReadonlySandbox";

    public bool Enabled { get; init; }

    public string BaseUrl { get; init; } = string.Empty;

    public string ServiceAccountToken { get; init; } = string.Empty;

    public int TimeoutSeconds { get; init; } = 10;

    public string EnvironmentLabel { get; init; } = "RealSandbox";

    public string DefaultPassStationTypeKey { get; init; } = "default";

    public string[] ExplicitPostQueryPaths { get; init; } = [];

    public bool IsConfigured() =>
        Enabled &&
        !string.IsNullOrWhiteSpace(BaseUrl) &&
        !string.IsNullOrWhiteSpace(ServiceAccountToken);

    public void EnsureValid()
    {
        if (!Enabled)
        {
            return;
        }

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("CloudReadonlySandbox:BaseUrl must be an absolute HTTP/HTTPS URL when enabled.");
        }

        if (string.IsNullOrWhiteSpace(ServiceAccountToken))
        {
            throw new InvalidOperationException("CloudReadonlySandbox:ServiceAccountToken is required when enabled.");
        }

        if (TimeoutSeconds is < 1 or > 30)
        {
            throw new InvalidOperationException("CloudReadonlySandbox:TimeoutSeconds must be between 1 and 30.");
        }

        if (!CloudAiReadEndpointPolicy.IsSafeRouteSegment(DefaultPassStationTypeKey))
        {
            throw new InvalidOperationException("CloudReadonlySandbox:DefaultPassStationTypeKey must be a single safe route segment.");
        }

        foreach (var path in ExplicitPostQueryPaths)
        {
            var decision = CloudAiReadEndpointPolicy.Evaluate(HttpMethod.Post, path, ExplicitPostQueryPaths);
            if (!decision.IsAllowed)
            {
                throw new InvalidOperationException($"CloudReadonlySandbox:ExplicitPostQueryPaths contains an unsafe path '{path}': {decision.Reason}");
            }
        }
    }
}

public sealed class CloudReadonlySandboxAgentTrialOptions
{
    public const string SectionName = "CloudReadonlySandboxAgentTrial";

    public bool Enabled { get; init; }

    public string[] AllowedScenarioIds { get; init; } =
    [
        "cloud-sandbox-devices",
        "cloud-sandbox-capacity-summary",
        "cloud-sandbox-device-logs",
        "cloud-sandbox-pass-station-records",
        "cloud-sandbox-device-exception-analysis",
        "cloud-sandbox-capacity-delivery-analysis"
    ];

    public int MaxRows { get; init; } = 20;

    public int TimeoutMs { get; init; } = 5000;

    public bool RequiresToolApproval { get; init; } = true;

    public bool RequiresFinalApproval { get; init; } = true;

    public void EnsureValid()
    {
        if (MaxRows is < 1 or > 100)
        {
            throw new InvalidOperationException("CloudReadonlySandboxAgentTrial:MaxRows must be between 1 and 100.");
        }

        if (TimeoutMs is < 500 or > 30000)
        {
            throw new InvalidOperationException("CloudReadonlySandboxAgentTrial:TimeoutMs must be between 500 and 30000.");
        }

        foreach (var scenarioId in AllowedScenarioIds)
        {
            if (!CloudAiReadEndpointPolicy.IsSafeRouteSegment(scenarioId))
            {
                throw new InvalidOperationException($"CloudReadonlySandboxAgentTrial:AllowedScenarioIds contains an unsafe id '{scenarioId}'.");
            }
        }
    }
}

public sealed class CloudReadonlySandboxControlledTrialOptions
{
    public const string SectionName = "CloudReadonlySandboxControlledTrial";

    public bool Enabled { get; init; }

    public bool FreeGoalEnabled { get; init; } = true;

    public string[] AllowedEndpointCodes { get; init; } =
    [
        "devices",
        "capacity_summary",
        "device_logs",
        "pass_station_records"
    ];

    public string[] AllowedArtifactTypes { get; init; } =
    [
        "Chart",
        "Markdown",
        "Html",
        "Pdf",
        "Pptx",
        "Xlsx"
    ];

    public int MaxTimeRangeDays { get; init; } = 31;

    public int DefaultMaxRows { get; init; } = 20;

    public int MaxRows { get; init; } = 50;

    public int TimeoutMs { get; init; } = 5000;

    public bool RequiresToolApproval { get; init; } = true;

    public bool RequiresFinalApproval { get; init; } = true;

    public void EnsureValid()
    {
        if (MaxTimeRangeDays is < 1 or > 366)
        {
            throw new InvalidOperationException("CloudReadonlySandboxControlledTrial:MaxTimeRangeDays must be between 1 and 366.");
        }

        if (DefaultMaxRows is < 1 or > 100)
        {
            throw new InvalidOperationException("CloudReadonlySandboxControlledTrial:DefaultMaxRows must be between 1 and 100.");
        }

        if (MaxRows < DefaultMaxRows || MaxRows is < 1 or > 500)
        {
            throw new InvalidOperationException("CloudReadonlySandboxControlledTrial:MaxRows must be between DefaultMaxRows and 500.");
        }

        if (TimeoutMs is < 500 or > 30000)
        {
            throw new InvalidOperationException("CloudReadonlySandboxControlledTrial:TimeoutMs must be between 500 and 30000.");
        }

        foreach (var endpointCode in AllowedEndpointCodes)
        {
            if (!CloudAiReadEndpointPolicy.IsSafeRouteSegment(endpointCode))
            {
                throw new InvalidOperationException($"CloudReadonlySandboxControlledTrial:AllowedEndpointCodes contains an unsafe endpoint code '{endpointCode}'.");
            }
        }

        foreach (var artifactType in AllowedArtifactTypes)
        {
            if (!CloudAiReadEndpointPolicy.IsSafeRouteSegment(artifactType))
            {
                throw new InvalidOperationException($"CloudReadonlySandboxControlledTrial:AllowedArtifactTypes contains an unsafe artifact type '{artifactType}'.");
            }
        }
    }
}
