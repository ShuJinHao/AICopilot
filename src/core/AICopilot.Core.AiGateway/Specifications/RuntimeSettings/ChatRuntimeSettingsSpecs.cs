using AICopilot.Core.AiGateway.Aggregates.RuntimeSettings;
using AICopilot.SharedKernel.Specification;

namespace AICopilot.Core.AiGateway.Specifications.RuntimeSettings;

public sealed class GlobalChatRuntimeSettingsSpec : Specification<ChatRuntimeSettings>
{
    public GlobalChatRuntimeSettingsSpec()
    {
        FilterCondition = settings => settings.Id == ChatRuntimeSettings.GlobalId;
    }
}
