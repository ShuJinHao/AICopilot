using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;
using System.ClientModel.Primitives;

namespace AICopilot.Embedding;

public class EmbeddingGeneratorFactory(
    IHttpClientFactory httpClientFactory,
    ISecretProtector secretProtector)
{
    public IEmbeddingGenerator<string, Embedding<float>> CreateGenerator(EmbeddingModel model)
    {
        var apiKey = secretProtector.Unprotect(model.ApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException($"EmbeddingModel ApiKey is required; check configuration for {model.Name}.");
        }

        var endpoint = new Uri(model.BaseUrl);
        var credential = new ApiKeyCredential(apiKey);

        var httpClient = httpClientFactory.CreateClient("EmbeddingClient");

        var options = new OpenAIClientOptions
        {
            Endpoint = endpoint,
            // 使用 IHttpClientFactory 创建 HttpClient，复用连接池
            Transport = new HttpClientPipelineTransport(httpClient),
            NetworkTimeout = TimeSpan.FromMinutes(20)
        };

        // 创建 OpenAI 客户端
        var client = new OpenAIClient(credential, options);
        return client
            .GetEmbeddingClient(model.ModelName)
            .AsIEmbeddingGenerator(model.Dimensions);
    }
}
