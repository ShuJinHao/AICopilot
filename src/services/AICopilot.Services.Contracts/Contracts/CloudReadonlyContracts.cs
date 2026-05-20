using System.Net.Http;

namespace AICopilot.Services.Contracts;

public enum CloudReadonlyDataSourceMode
{
    Disabled = 0,
    Simulation = 1,
    Real = 2
}

public sealed class CloudReadonlyOptions
{
    public const string SectionName = "CloudReadonly";

    public CloudReadonlyDataSourceMode Mode { get; init; } = CloudReadonlyDataSourceMode.Disabled;

    public CloudReadonlySimulationOptions Simulation { get; init; } = new();

    public CloudReadonlyRealOptions Real { get; init; } = new();

    public void EnsureValid(CloudAiReadOptions? cloudAiReadOptions = null)
    {
        if (!Enum.IsDefined(Mode))
        {
            throw new InvalidOperationException("CloudReadonly:Mode must be Disabled, Simulation, or Real.");
        }

        if (Mode == CloudReadonlyDataSourceMode.Simulation)
        {
            if (!Simulation.Enabled)
            {
                throw new InvalidOperationException("CloudReadonly:Simulation:Enabled must be true when CloudReadonly:Mode is Simulation.");
            }

            if (!Simulation.AlwaysMarkAsSimulation)
            {
                throw new InvalidOperationException("CloudReadonly:Simulation:AlwaysMarkAsSimulation must stay true.");
            }
        }

        if (Mode == CloudReadonlyDataSourceMode.Real)
        {
            if (!Real.Enabled)
            {
                throw new InvalidOperationException("CloudReadonly:Real:Enabled must be true when CloudReadonly:Mode is Real.");
            }

            if (!Real.AllowProductionRead)
            {
                throw new InvalidOperationException("CloudReadonly:Real:AllowProductionRead must be true when CloudReadonly:Mode is Real.");
            }

            if (cloudAiReadOptions is null || !cloudAiReadOptions.Enabled)
            {
                throw new InvalidOperationException("CloudAiRead:Enabled must be true when CloudReadonly:Mode is Real.");
            }
        }
    }
}

public sealed class CloudReadonlySimulationOptions
{
    public bool Enabled { get; init; }

    public bool SeedData { get; init; } = true;

    public string DataSet { get; init; } = "ManufacturingDemo";

    public bool AlwaysMarkAsSimulation { get; init; } = true;
}

public sealed class CloudReadonlyRealOptions
{
    public bool Enabled { get; init; }

    public bool AllowProductionRead { get; init; }
}

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

public sealed class CloudReadonlyPilotReadinessOptions
{
    public const string SectionName = "CloudReadonlyPilotReadiness";

    public bool Enabled { get; init; }

    public string[] AllowedEndpointCodes { get; init; } =
    [
        "devices",
        "capacity_summary",
        "device_logs",
        "pass_station_records"
    ];

    public int MaxTimeRangeDays { get; init; } = 7;

    public int DefaultMaxRows { get; init; } = 20;

    public int MaxRows { get; init; } = 50;

    public int TimeoutMs { get; init; } = 5000;

    public string ApprovalPolicy { get; init; } = "PilotReadinessRehearsal";

    public string RollbackPolicy { get; init; } = "DisablePilotConfigAndKeepProductionToolsClosed";

    public string OwnerDepartment { get; init; } = "AI Platform";

    public bool RequiresToolApproval { get; init; } = true;

    public bool RequiresFinalApproval { get; init; } = true;

    public void EnsureValid()
    {
        if (MaxTimeRangeDays is < 1 or > 31)
        {
            throw new InvalidOperationException("CloudReadonlyPilotReadiness:MaxTimeRangeDays must be between 1 and 31.");
        }

        if (DefaultMaxRows is < 1 or > 100)
        {
            throw new InvalidOperationException("CloudReadonlyPilotReadiness:DefaultMaxRows must be between 1 and 100.");
        }

        if (MaxRows < DefaultMaxRows || MaxRows is < 1 or > 500)
        {
            throw new InvalidOperationException("CloudReadonlyPilotReadiness:MaxRows must be between DefaultMaxRows and 500.");
        }

        if (TimeoutMs is < 500 or > 30000)
        {
            throw new InvalidOperationException("CloudReadonlyPilotReadiness:TimeoutMs must be between 500 and 30000.");
        }

        foreach (var endpointCode in AllowedEndpointCodes)
        {
            if (!CloudAiReadEndpointPolicy.IsSafeRouteSegment(endpointCode))
            {
                throw new InvalidOperationException($"CloudReadonlyPilotReadiness:AllowedEndpointCodes contains an unsafe endpoint code '{endpointCode}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(ApprovalPolicy))
        {
            throw new InvalidOperationException("CloudReadonlyPilotReadiness:ApprovalPolicy is required.");
        }

        if (string.IsNullOrWhiteSpace(RollbackPolicy))
        {
            throw new InvalidOperationException("CloudReadonlyPilotReadiness:RollbackPolicy is required.");
        }
    }
}

public static class CloudReadonlySandboxAgentTrialMarkers
{
    public const string SourceType = "CloudReadonly";
    public const string SourceMode = "CloudReadonlySandbox";
    public const string SourceLabel = "Cloud 只读 Sandbox（非生产）";
    public const string Boundary = "SandboxAgentTrial";
    public const string ToolCode = "query_cloud_sandbox_readonly";
}

public static class CloudReadonlySandboxControlledTrialMarkers
{
    public const string Boundary = "SandboxControlledTrial";
    public const string TrialMode = "ControlledGoal";
    public const string FixedScenarioTrialMode = "FixedScenario";
}

public static class CloudReadonlyPilotReadinessMarkers
{
    public const string SourceType = "CloudReadonly";
    public const string SourceMode = "CloudReadonlyPilotReadiness";
    public const string SourceLabel = "Cloud 只读 Pilot 准入演练（非生产）";
    public const string Boundary = "PilotReadinessRehearsal";
    public const string ToolCode = "query_cloud_pilot_readiness_readonly";
}

public static class CloudReadonlySourceMarkers
{
    public const string SimulationSourceMode = "Simulation";
    public const string RealSourceMode = "Real";
    public const string SimulationSourceLabel = "模拟 Cloud 只读数据";
}
