namespace AICopilot.AiGatewayService.CloudReadiness;

internal sealed class InMemoryCloudReadonlySandboxControlledTrialIntentStore
    : ICloudReadonlySandboxControlledTrialIntentStore
{
    private readonly object sync = new();
    private readonly Dictionary<string, CloudSandboxGoalIntentDto> items = new(StringComparer.OrdinalIgnoreCase);

    public void Save(CloudSandboxGoalIntentDto intent)
    {
        lock (sync)
        {
            items[intent.IntentId] = intent;
            if (items.Count <= 100)
            {
                return;
            }

            foreach (var key in items.Keys.Take(items.Count - 100).ToArray())
            {
                items.Remove(key);
            }
        }
    }

    public CloudSandboxGoalIntentDto? Get(string intentId)
    {
        lock (sync)
        {
            return items.GetValueOrDefault(intentId);
        }
    }
}
