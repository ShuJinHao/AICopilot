using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.Agents;

public interface IAgentExecutionMetadataAccessor
{
    void SetRoutingModel(LanguageModel model);

    void SetRoutingConfiguration(RuntimeAgentConfigurationSnapshot snapshot);

    void SetFinalModel(LanguageModel model, int reservedOutputTokens);

    void SetContextBudget(ContextBudgetReportDto report);

    void Apply(ChatExecutionMetadataSnapshot snapshot);

    ChatExecutionMetadataSnapshot Snapshot();

    MessageModelSnapshot ToMessageSnapshot();
}

public sealed class AgentExecutionMetadataAccessor : IAgentExecutionMetadataAccessor
{
    private ChatExecutionMetadataSnapshot current = new();

    public void SetRoutingModel(LanguageModel model)
    {
        current = current with
        {
            RoutingModelId = model.Id,
            RoutingModelName = model.Name
        };
    }

    public void SetRoutingConfiguration(RuntimeAgentConfigurationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        current = current with
        {
            RoutingModelId = snapshot.ModelId,
            RoutingModelName = snapshot.ModelName,
            RoutingConfiguration = snapshot
        };
    }

    public void SetFinalModel(LanguageModel model, int reservedOutputTokens)
    {
        current = current with
        {
            FinalModelId = model.Id,
            FinalModelName = model.Name,
            ContextWindowTokens = model.Parameters.MaxTokens,
            MaxOutputTokens = reservedOutputTokens
        };
    }

    public void SetContextBudget(ContextBudgetReportDto report)
    {
        current = current with
        {
            ContextBudgetReport = report
        };
    }

    public void Apply(ChatExecutionMetadataSnapshot snapshot)
    {
        current = snapshot;
    }

    public ChatExecutionMetadataSnapshot Snapshot()
    {
        return current;
    }

    public MessageModelSnapshot ToMessageSnapshot()
    {
        return new MessageModelSnapshot(
            current.FinalModelId,
            current.FinalModelName,
            current.RoutingModelId,
            current.RoutingModelName,
            current.ContextWindowTokens,
            current.MaxOutputTokens);
    }
}
