using System.Text.Json;
using AICopilot.AiGatewayService.Queries.LanguageModels;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.RagService.EmbeddingModels;
using AICopilot.Services.Contracts.AiGateway.Dtos;

namespace AICopilot.ContractTests;

public sealed class ModelSecretContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void LanguageModelDto_ShouldExposeOnlySecretPresenceAndFixedPreview()
    {
        var properties = typeof(LanguageModelDto)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        properties.Should().Contain(nameof(LanguageModelDto.HasApiKey));
        properties.Should().Contain(nameof(LanguageModelDto.ApiKeyPreview));
        properties.Should().NotContain("ApiKey");
        properties.Should().NotContain("ApiKeyMasked");

        var json = JsonSerializer.Serialize(
            new LanguageModelDto
            {
                Id = Guid.NewGuid(),
                Provider = "OpenAI",
                ProtocolType = "OpenAI",
                Name = "chat",
                BaseUrl = "https://example.invalid/v1",
                MaxTokens = 4096,
                ContextWindowTokens = 4096,
                MaxOutputTokens = 1024,
                Temperature = 0.2,
                IsEnabled = true,
                Usages = ["Chat"],
                HasApiKey = true,
                ApiKeyPreview = "******",
                ConnectivityStatus = "Unknown"
            },
            JsonOptions);

        json.Should().Contain("\"hasApiKey\":true");
        json.Should().Contain("\"apiKeyPreview\":\"******\"");
        json.Should().NotContain("\"apiKey\":");
        json.Should().NotContain("sk-test-secret");
        json.Should().NotContain("apiKeyMasked");
    }

    [Fact]
    public void EmbeddingModelDto_ShouldExposeOnlySecretPresenceAndFixedPreview()
    {
        var properties = typeof(EmbeddingModelDto)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        properties.Should().Contain(nameof(EmbeddingModelDto.HasApiKey));
        properties.Should().Contain(nameof(EmbeddingModelDto.ApiKeyPreview));
        properties.Should().NotContain("ApiKey");
        properties.Should().NotContain("ApiKeyMasked");

        var json = JsonSerializer.Serialize(
            new EmbeddingModelDto
            {
                Id = Guid.NewGuid(),
                Name = "embedding",
                Provider = "OpenAI",
                BaseUrl = "https://example.invalid/v1",
                ModelName = "text-embedding-test",
                Dimensions = 1536,
                MaxTokens = 8191,
                IsEnabled = true,
                HasApiKey = true,
                ApiKeyPreview = "******"
            },
            JsonOptions);

        json.Should().Contain("\"hasApiKey\":true");
        json.Should().Contain("\"apiKeyPreview\":\"******\"");
        json.Should().NotContain("\"apiKey\":");
        json.Should().NotContain("sk-test-secret");
        json.Should().NotContain("apiKeyMasked");
    }

    [Fact]
    public void LanguageModelDto_ShouldExposePlannerUsageWithoutApiKey()
    {
        var model = new LanguageModel(
            "FakeEval",
            "planner",
            "http://localhost/fake",
            "sk-test-secret",
            new ModelParameters { MaxTokens = 4096, MaxOutputTokens = 1024, Temperature = 0.2f },
            "FakeEval",
            LanguageModelUsage.Chat | LanguageModelUsage.Planner,
            true);

        var dto = LanguageModelDtoMapper.Map(model);
        var json = JsonSerializer.Serialize(dto, JsonOptions);

        dto.Usages.Should().Contain("Planner");
        dto.HasApiKey.Should().BeTrue();
        dto.ApiKeyPreview.Should().Be("******");
        json.Should().Contain("\"Planner\"");
        json.Should().NotContain("sk-test-secret");
        json.Should().NotContain("\"apiKey\":");
    }
}
