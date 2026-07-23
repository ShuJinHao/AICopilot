namespace AICopilot.AiRuntime;

public sealed partial class ModelProviderReliabilityOptions
{
    public int PerTenantRpmLimit { get; set; }
    public int PerTenantTpmLimit { get; set; }
    public int PerTenantConcurrencyLimit { get; set; }
}
