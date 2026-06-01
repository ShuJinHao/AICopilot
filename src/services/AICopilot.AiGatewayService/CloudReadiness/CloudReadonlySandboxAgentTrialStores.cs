namespace AICopilot.AiGatewayService.CloudReadiness;

internal sealed class InMemoryCloudReadonlySandboxAgentTrialHistoryStore
    : ICloudReadonlySandboxAgentTrialHistoryStore
{
    private readonly object sync = new();
    private readonly List<CloudReadonlySandboxAgentTrialResultDto> items = [];

    public void Save(CloudReadonlySandboxAgentTrialResultDto result)
    {
        lock (sync)
        {
            items.Insert(0, result);
            if (items.Count > 20)
            {
                items.RemoveRange(20, items.Count - 20);
            }
        }
    }

    public IReadOnlyCollection<CloudReadonlySandboxAgentTrialResultDto> List()
    {
        lock (sync)
        {
            return items.ToArray();
        }
    }
}
