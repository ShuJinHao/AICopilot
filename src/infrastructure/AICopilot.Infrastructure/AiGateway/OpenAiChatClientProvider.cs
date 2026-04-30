using System.ClientModel;
using System.ClientModel.Primitives;
using AICopilot.AiRuntime;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace AICopilot.Infrastructure.AiGateway;

public sealed class OpenAiChatClientProvider(IHttpClientFactory httpClientFactory) : IChatClientProvider
{
    public bool CanHandle(string providerName)
    {
        return string.Equals(providerName, "OpenAI", StringComparison.OrdinalIgnoreCase);
    }

    public IChatClient CreateClient(LanguageModel model)
    {
        if (string.IsNullOrWhiteSpace(model.ApiKey))
        {
            throw new InvalidOperationException($"LanguageModel ApiKey is required; check configuration for {model.Name}.");
        }

        var httpClient = httpClientFactory.CreateClient("OpenAI");
        var client = new OpenAIClient(
            new ApiKeyCredential(model.ApiKey),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(model.BaseUrl),
                Transport = new HttpClientPipelineTransport(httpClient)
            });

        return client
            .GetChatClient(model.Name)
            .AsIChatClient();
    }
}
