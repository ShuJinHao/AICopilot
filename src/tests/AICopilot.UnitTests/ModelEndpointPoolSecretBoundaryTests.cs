using AICopilot.AiRuntime;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Options;

namespace AICopilot.UnitTests;

public sealed class ModelEndpointPoolSecretBoundaryTests
{
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

    private sealed class RecordingSecretProtector : ISecretProtector
    {
        public List<string?> UnprotectedValues { get; } = [];

        public string? Protect(string? plaintext) =>
            string.IsNullOrEmpty(plaintext) ? plaintext : $"encv2:{plaintext}";

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

        public bool IsProtected(string? storedValue) =>
            storedValue?.StartsWith("encv2:", StringComparison.Ordinal) == true;

        public void EnsureConfigured()
        {
        }
    }
}
