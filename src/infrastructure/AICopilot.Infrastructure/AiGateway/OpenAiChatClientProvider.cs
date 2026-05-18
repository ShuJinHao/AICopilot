using System.ClientModel;
using System.ClientModel.Primitives;
using AICopilot.AiRuntime;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.AI;
using OpenAI;

namespace AICopilot.Infrastructure.AiGateway;

public sealed class OpenAiChatClientProvider(
    IHttpClientFactory httpClientFactory,
    ISecretProtector secretProtector) : IChatClientProvider
{
    public bool CanHandle(string providerName)
    {
        return string.Equals(providerName, LanguageModelProtocolTypes.OpenAICompatible, StringComparison.OrdinalIgnoreCase)
               || string.Equals(providerName, "OpenAI", StringComparison.OrdinalIgnoreCase);
    }

    public IChatClient CreateClient(LanguageModel model)
    {
        var apiKey = secretProtector.Unprotect(model.ApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException($"LanguageModel ApiKey is required; check configuration for {model.Name}.");
        }

        var httpClient = httpClientFactory.CreateClient("OpenAI");
        var client = new OpenAIClient(
            new ApiKeyCredential(apiKey),
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
