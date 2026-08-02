using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.Agents;

public interface IChatExecutionMetadataAccessor
{
    void SetFinalConfiguration(RuntimeAgentConfigurationSnapshot snapshot);

    MessageModelSnapshot ToMessageSnapshot();
}

public sealed class ChatExecutionMetadataAccessor : IChatExecutionMetadataAccessor
{
    private RuntimeAgentConfigurationSnapshot? current;

    public void SetFinalConfiguration(RuntimeAgentConfigurationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        current = snapshot;
    }

    public MessageModelSnapshot ToMessageSnapshot()
    {
        return current is null
            ? new MessageModelSnapshot(null, null, null, null)
            : new MessageModelSnapshot(
                current.ModelId,
                current.ModelName,
                current.ContextWindowTokens,
                current.MaxOutputTokens);
    }
}
