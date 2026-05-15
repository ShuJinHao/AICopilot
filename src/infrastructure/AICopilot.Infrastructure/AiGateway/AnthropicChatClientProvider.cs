using Anthropic;
using Anthropic.Core;
using AICopilot.AiRuntime;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using Microsoft.Extensions.AI;

namespace AICopilot.Infrastructure.AiGateway;

public sealed class AnthropicChatClientProvider(IHttpClientFactory httpClientFactory) : IChatClientProvider
{
    public bool CanHandle(string providerName)
    {
        return string.Equals(providerName, LanguageModelProtocolTypes.AnthropicMessages, StringComparison.OrdinalIgnoreCase)
               || string.Equals(providerName, "Anthropic", StringComparison.OrdinalIgnoreCase)
               || string.Equals(providerName, "Claude", StringComparison.OrdinalIgnoreCase);
    }

    public IChatClient CreateClient(LanguageModel model)
    {
        if (string.IsNullOrWhiteSpace(model.ApiKey))
        {
            throw new InvalidOperationException($"LanguageModel ApiKey is required; check configuration for {model.Name}.");
        }

        var httpClient = httpClientFactory.CreateClient("Anthropic");
        var client = new AnthropicClient(new ClientOptions
        {
            ApiKey = model.ApiKey,
            BaseUrl = model.BaseUrl,
            HttpClient = httpClient,
            MaxRetries = 0
        });

        return client.AsIChatClient(model.Name);
    }
}
