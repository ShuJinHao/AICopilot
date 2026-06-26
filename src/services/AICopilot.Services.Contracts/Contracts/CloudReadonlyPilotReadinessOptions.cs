namespace AICopilot.Services.Contracts;

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
