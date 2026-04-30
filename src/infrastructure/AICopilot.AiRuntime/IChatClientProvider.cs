using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using Microsoft.Extensions.AI;

namespace AICopilot.AiRuntime;

public interface IChatClientProvider
{
    bool CanHandle(string providerName);

    IChatClient CreateClient(LanguageModel model);
}
