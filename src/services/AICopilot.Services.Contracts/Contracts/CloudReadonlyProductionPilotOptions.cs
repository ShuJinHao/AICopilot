namespace AICopilot.Services.Contracts;

public sealed class CloudReadonlyProductionPilotOptions
{
    public const string SectionName = "CloudReadonlyProductionPilot";

    public bool Enabled { get; init; }

    public string[] AllowedEndpointCodes { get; init; } =
    [
        "devices",
        "capacity_summary",
        "device_logs",
        "pass_station_records"
    ];

    public string[] AllowedScenarioIds { get; init; } =
    [
        "cloud-production-pilot-devices",
        "cloud-production-pilot-capacity-summary",
        "cloud-production-pilot-device-logs",
        "cloud-production-pilot-pass-station-records",
        "cloud-production-pilot-device-exception-analysis",
        "cloud-production-pilot-capacity-delivery-analysis"
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

    public int MaxTimeRangeDays { get; init; } = 7;

    public int DefaultMaxRows { get; init; } = 20;

    public int MaxRows { get; init; } = 50;

    public int TimeoutMs { get; init; } = 5000;

    public string ApprovalPolicy { get; init; } = "ProductionPilotToolApproval";

    public string RollbackPolicy { get; init; } = "EmergencyDisableProductionPilot";

    public string OwnerDepartment { get; init; } = "AI Platform";

    public bool RequiresToolApproval { get; init; } = true;

    public bool RequiresFinalApproval { get; init; } = true;

    public void EnsureValid()
    {
        if (MaxTimeRangeDays is < 1 or > 31)
        {
            throw new InvalidOperationException("CloudReadonlyProductionPilot:MaxTimeRangeDays must be between 1 and 31.");
        }

        if (DefaultMaxRows is < 1 or > 100)
        {
            throw new InvalidOperationException("CloudReadonlyProductionPilot:DefaultMaxRows must be between 1 and 100.");
        }

        if (MaxRows < DefaultMaxRows || MaxRows is < 1 or > 500)
        {
            throw new InvalidOperationException("CloudReadonlyProductionPilot:MaxRows must be between DefaultMaxRows and 500.");
        }

        if (TimeoutMs is < 500 or > 30000)
        {
            throw new InvalidOperationException("CloudReadonlyProductionPilot:TimeoutMs must be between 500 and 30000.");
        }

        foreach (var endpointCode in AllowedEndpointCodes)
        {
            if (!CloudAiReadEndpointPolicy.IsSafeRouteSegment(endpointCode))
            {
                throw new InvalidOperationException($"CloudReadonlyProductionPilot:AllowedEndpointCodes contains an unsafe endpoint code '{endpointCode}'.");
            }
        }

        foreach (var scenarioId in AllowedScenarioIds)
        {
            if (!CloudAiReadEndpointPolicy.IsSafeRouteSegment(scenarioId))
            {
                throw new InvalidOperationException($"CloudReadonlyProductionPilot:AllowedScenarioIds contains an unsafe scenario id '{scenarioId}'.");
            }
        }

        foreach (var artifactType in AllowedArtifactTypes)
        {
            if (!CloudAiReadEndpointPolicy.IsSafeRouteSegment(artifactType))
            {
                throw new InvalidOperationException($"CloudReadonlyProductionPilot:AllowedArtifactTypes contains an unsafe artifact type '{artifactType}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(ApprovalPolicy))
        {
            throw new InvalidOperationException("CloudReadonlyProductionPilot:ApprovalPolicy is required.");
        }

        if (string.IsNullOrWhiteSpace(RollbackPolicy))
        {
            throw new InvalidOperationException("CloudReadonlyProductionPilot:RollbackPolicy is required.");
        }
    }
}

public sealed class CloudReadonlyProductionControlledPilotOptions
{
    public const string SectionName = "CloudReadonlyProductionControlledPilot";

    public bool Enabled { get; init; }

    public bool FreeGoalEnabled { get; init; }

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

    public int MaxTimeRangeDays { get; init; } = 7;

    public int DefaultMaxRows { get; init; } = 20;

    public int MaxRows { get; init; } = 50;

    public int TimeoutMs { get; init; } = 5000;

    public bool RequiresToolApproval { get; init; } = true;

    public bool RequiresFinalApproval { get; init; } = true;

    public string ApprovalPolicy { get; init; } = "ProductionControlledPilotToolApproval";

    public string RollbackPolicy { get; init; } = "EmergencyDisableProductionControlledPilot";

    public void EnsureValid()
    {
        if (MaxTimeRangeDays is < 1 or > 31)
        {
            throw new InvalidOperationException("CloudReadonlyProductionControlledPilot:MaxTimeRangeDays must be between 1 and 31.");
        }

        if (DefaultMaxRows is < 1 or > 100)
        {
            throw new InvalidOperationException("CloudReadonlyProductionControlledPilot:DefaultMaxRows must be between 1 and 100.");
        }

        if (MaxRows < DefaultMaxRows || MaxRows is < 1 or > 500)
        {
            throw new InvalidOperationException("CloudReadonlyProductionControlledPilot:MaxRows must be between DefaultMaxRows and 500.");
        }

        if (TimeoutMs is < 500 or > 30000)
        {
            throw new InvalidOperationException("CloudReadonlyProductionControlledPilot:TimeoutMs must be between 500 and 30000.");
        }

        foreach (var endpointCode in AllowedEndpointCodes)
        {
            if (!CloudAiReadEndpointPolicy.IsSafeRouteSegment(endpointCode))
            {
                throw new InvalidOperationException($"CloudReadonlyProductionControlledPilot:AllowedEndpointCodes contains an unsafe endpoint code '{endpointCode}'.");
            }
        }

        foreach (var artifactType in AllowedArtifactTypes)
        {
            if (!CloudAiReadEndpointPolicy.IsSafeRouteSegment(artifactType))
            {
                throw new InvalidOperationException($"CloudReadonlyProductionControlledPilot:AllowedArtifactTypes contains an unsafe artifact type '{artifactType}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(ApprovalPolicy))
        {
            throw new InvalidOperationException("CloudReadonlyProductionControlledPilot:ApprovalPolicy is required.");
        }

        if (string.IsNullOrWhiteSpace(RollbackPolicy))
        {
            throw new InvalidOperationException("CloudReadonlyProductionControlledPilot:RollbackPolicy is required.");
        }
    }
}
