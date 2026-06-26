namespace AICopilot.Services.Contracts;

public static class CloudReadonlyRuntimeStatuses
{
    public const string Disabled = "Disabled";
    public const string Simulation = "Simulation";
    public const string RealReady = "RealReady";
    public const string RealMissingBaseUrl = "RealMissingBaseUrl";
    public const string RealMissingToken = "RealMissingToken";
    public const string RealNotAllowed = "RealNotAllowed";
}

public sealed record CloudReadonlyStatusDto(
    string Mode,
    string Status,
    bool BaseUrlConfigured,
    bool TokenConfigured,
    bool ProductionReadAllowed,
    string Message);
