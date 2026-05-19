using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.RoutingModel;

public class RoutingModelConfiguration : IAggregateRoot<RoutingModelConfigurationId>
{
    protected RoutingModelConfiguration()
    {
    }

    public RoutingModelConfiguration(string name, LanguageModelId modelId, bool isActive = false)
    {
        Validate(name, modelId);

        Id = RoutingModelConfigurationId.New();
        Name = name.Trim();
        ModelId = modelId;
        IsActive = isActive;
    }

    public RoutingModelConfigurationId Id { get; private set; }

    public uint RowVersion { get; private set; }

    public string Name { get; private set; } = null!;

    public LanguageModelId ModelId { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(string name, LanguageModelId modelId)
    {
        Validate(name, modelId);
        Name = name.Trim();
        ModelId = modelId;
    }

    public void Activate()
    {
        IsActive = true;
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    private static void Validate(string name, LanguageModelId modelId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Routing model configuration name is required.", nameof(name));
        }

        if (name.Trim().Length > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(name), "Routing model configuration name cannot exceed 200 characters.");
        }

        if (modelId.Value == Guid.Empty)
        {
            throw new ArgumentException("Routing model configuration model id is required.", nameof(modelId));
        }
    }
}
