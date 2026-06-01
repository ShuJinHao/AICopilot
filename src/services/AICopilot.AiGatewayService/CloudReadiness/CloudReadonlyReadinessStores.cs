namespace AICopilot.AiGatewayService.CloudReadiness;

internal sealed class InMemoryCloudReadonlyReadinessHistoryStore : ICloudReadonlyReadinessHistoryStore
{
    private readonly object _sync = new();
    private readonly List<CloudReadonlyReadinessDto> _items = [];

    public void Save(CloudReadonlyReadinessDto report)
    {
        lock (_sync)
        {
            _items.Insert(0, report);
            if (_items.Count > 20)
            {
                _items.RemoveRange(20, _items.Count - 20);
            }
        }
    }

    public IReadOnlyCollection<CloudReadonlyReadinessDto> List()
    {
        lock (_sync)
        {
            return _items.ToArray();
        }
    }
}
