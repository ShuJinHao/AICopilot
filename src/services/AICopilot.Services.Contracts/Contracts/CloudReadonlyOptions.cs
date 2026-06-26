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

    public void EnsureValid(CloudAiReadOptions? cloudAiReadOptions = null, string? environmentName = null)
    {
        if (!Enum.IsDefined(Mode))
        {
            throw new InvalidOperationException("CloudReadonly:Mode must be Disabled, Simulation, or Real.");
        }

        if (Mode == CloudReadonlyDataSourceMode.Simulation)
        {
            if (!IsDevelopmentEnvironment(environmentName))
            {
                throw new InvalidOperationException(
                    "CloudReadonly:Mode=Simulation is only allowed in Development.");
            }

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

    private static bool IsDevelopmentEnvironment(string? environmentName)
    {
        return string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);
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
