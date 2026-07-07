using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.AiRuntime;
using AICopilot.Embedding;
using AICopilot.Infrastructure.AiGateway;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Options;

namespace AICopilot.BackendTests;

[Trait("Suite", "Batch6SecretProtection")]
public sealed class ModelSecretRuntimeBoundaryTests
{
    [Fact]
    public void OpenAiChatClientProvider_ShouldDecryptProtectedApiKeyBeforeCreatingClient()
    {
        var protector = new RecordingSecretProtector();
        var provider = new OpenAiChatClientProvider(new TestHttpClientFactory(), protector);
        var model = CreateLanguageModel("encv2:sk-runtime-chat");

        var client = provider.CreateClient(model);

        client.Should().NotBeNull();
        protector.UnprotectedValues.Should().ContainSingle().Which.Should().Be("encv2:sk-runtime-chat");
        (client as IDisposable)?.Dispose();
    }

    [Fact]
    public void OpenAiChatClientProvider_ShouldRejectLegacyPlaintextApiKey()
    {
        var protector = new RecordingSecretProtector();
        var provider = new OpenAiChatClientProvider(new TestHttpClientFactory(), protector);
        var model = CreateLanguageModel("sk-legacy-chat");

        var action = () => provider.CreateClient(model);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*encv2:*");
    }

    [Fact]
    public void EmbeddingGeneratorFactory_ShouldDecryptProtectedApiKeyBeforeCreatingGenerator()
    {
        var protector = new RecordingSecretProtector();
        var factory = new EmbeddingGeneratorFactory(new TestHttpClientFactory(), protector);
        var model = CreateEmbeddingModel("encv2:sk-runtime-embedding");

        var generator = factory.CreateGenerator(model);

        generator.Should().NotBeNull();
        protector.UnprotectedValues.Should().ContainSingle().Which.Should().Be("encv2:sk-runtime-embedding");
    }

    [Fact]
    public void EmbeddingGeneratorFactory_ShouldRejectLegacyPlaintextApiKey()
    {
        var protector = new RecordingSecretProtector();
        var factory = new EmbeddingGeneratorFactory(new TestHttpClientFactory(), protector);
        var model = CreateEmbeddingModel("sk-legacy-embedding");

        var action = () => factory.CreateGenerator(model);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*encv2:*");
    }

    [Fact]
    public void ModelEndpointPoolScheduler_ShouldRejectPlaintextOverrideApiKey()
    {
        var scheduler = new InMemoryModelEndpointPoolScheduler(
            Options.Create(new ModelProviderReliabilityOptions
            {
                EndpointPools =
                {
                    ["AnswerPool"] = new ModelEndpointPoolOptions
                    {
                        Endpoints =
                        [
                            new ModelEndpointOptions
                            {
                                EndpointId = "answer-plain",
                                Provider = "OpenAI",
                                BaseUrl = "https://example.test/v1",
                                ApiKey = "sk-plain"
                            }
                        ]
                    }
                }
            }),
            new RecordingSecretProtector());

        var action = () => scheduler.SelectEndpoint("AnswerPool");

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*encv2:*");
    }

    [Fact]
    public void ModelEndpointPoolScheduler_ShouldPassProtectedEnvironmentApiKeyWithoutDecrypting()
    {
        const string variableName = "AICOPILOT_TEST_ENDPOINT_POOL_API_KEY";
        var original = Environment.GetEnvironmentVariable(variableName);
        Environment.SetEnvironmentVariable(variableName, "encv2:pool-secret");
        var protector = new RecordingSecretProtector();
        var scheduler = new InMemoryModelEndpointPoolScheduler(
            Options.Create(new ModelProviderReliabilityOptions
            {
                EndpointPools =
                {
                    ["AnswerPool"] = new ModelEndpointPoolOptions
                    {
                        Endpoints =
                        [
                            new ModelEndpointOptions
                            {
                                EndpointId = "answer-env",
                                Provider = "OpenAI",
                                BaseUrl = "https://example.test/v1",
                                ApiKeyEnvironmentVariable = variableName
                            }
                        ]
                    }
                }
            }),
            protector);

        try
        {
            var selection = scheduler.SelectEndpoint("AnswerPool");

            selection.ApiKey.Should().Be("encv2:pool-secret");
            protector.UnprotectedValues.Should().BeEmpty();
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, original);
        }
    }

    [Fact]
    public void ModelEndpointPoolScheduler_ShouldRejectPlaintextEnvironmentApiKey()
    {
        const string variableName = "AICOPILOT_TEST_ENDPOINT_POOL_PLAINTEXT_API_KEY";
        var original = Environment.GetEnvironmentVariable(variableName);
        Environment.SetEnvironmentVariable(variableName, "sk-pool-plain");
        var scheduler = new InMemoryModelEndpointPoolScheduler(
            Options.Create(new ModelProviderReliabilityOptions
            {
                EndpointPools =
                {
                    ["AnswerPool"] = new ModelEndpointPoolOptions
                    {
                        Endpoints =
                        [
                            new ModelEndpointOptions
                            {
                                EndpointId = "answer-env-plain",
                                Provider = "OpenAI",
                                BaseUrl = "https://example.test/v1",
                                ApiKeyEnvironmentVariable = variableName
                            }
                        ]
                    }
                }
            }),
            new RecordingSecretProtector());

        try
        {
            var action = () => scheduler.SelectEndpoint("AnswerPool");

            action.Should().Throw<InvalidOperationException>()
                .WithMessage("*environment variable*encv2:*");
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, original);
        }
    }

    private static LanguageModel CreateLanguageModel(string apiKey)
    {
        return new LanguageModel(
            "OpenAI",
            "fake-chat-model",
            "https://example.test/v1",
            apiKey,
            new ModelParameters
            {
                MaxTokens = 4096,
                MaxOutputTokens = 1024,
                Temperature = 0.2f
            },
            LanguageModelProtocolTypes.OpenAICompatible,
            LanguageModelUsage.Chat,
            true);
    }

    private static EmbeddingModel CreateEmbeddingModel(string apiKey)
    {
        return new EmbeddingModel(
            "fake-embedding",
            "OpenAI",
            "https://example.test/v1",
            "fake-embedding-model",
            4,
            256,
            apiKey);
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient();
        }
    }

    private sealed class RecordingSecretProtector : ISecretProtector
    {
        public List<string?> UnprotectedValues { get; } = [];

        public string? Protect(string? plaintext)
        {
            return string.IsNullOrEmpty(plaintext) ? plaintext : $"encv2:{plaintext}";
        }

        public string? Unprotect(string? storedValue)
        {
            if (string.IsNullOrEmpty(storedValue))
            {
                return storedValue;
            }

            UnprotectedValues.Add(storedValue);
            if (!storedValue.StartsWith("encv2:", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Stored secret must be encrypted with 'encv2:'.");
            }

            return storedValue["encv2:".Length..];
        }

        public bool IsProtected(string? storedValue)
        {
            return storedValue?.StartsWith("encv2:", StringComparison.Ordinal) == true;
        }

        public void EnsureConfigured()
        {
        }
    }
}
