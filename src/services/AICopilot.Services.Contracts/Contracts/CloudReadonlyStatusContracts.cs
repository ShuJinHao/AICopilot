namespace AICopilot.Services.Contracts;

public static class CloudReadonlyRuntimeStatuses
{
    public const string Disabled = "Disabled";
    public const string Simulation = "Simulation";
    public const string RealReady = "RealReady";
    public const string RealMissingBaseUrl = "RealMissingBaseUrl";
    public const string RealMissingDelegation = "RealMissingDelegation";
    public const string RealNotAllowed = "RealNotAllowed";
}

public sealed record CloudReadonlyStatusDto(
    string Mode,
    string Status,
    bool BaseUrlConfigured,
    bool TransportConfigured,
    bool DelegationAvailable,
    bool ProductionReadAllowed,
    string Message);
