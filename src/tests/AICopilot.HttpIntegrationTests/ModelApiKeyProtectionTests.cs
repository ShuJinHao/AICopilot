using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.Security;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.HttpIntegrationTests;

[Collection(BackendTestCollection.Name)]
public sealed class ModelApiKeyProtectionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AICopilotAppFixture _fixture;

    public ModelApiKeyProtectionTests(AICopilotAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task LanguageModelApiKey_ShouldBeProtectedAtRest_AndConnectivityShouldUseStoredCipher()
    {
        await AuthenticateAsAdminAsync();
        var secret = $"sk-model-secret-language-{Guid.NewGuid():N}";
        var model = await PostJsonAsync<CreatedLanguageModelDto>("/api/aigateway/language-model", new
        {
            provider = "OpenAI",
            name = $"model-secret-lm-{Guid.NewGuid():N}",
            baseUrl = new Uri(_fixture.FakeAiBaseUri, "/v1").ToString().TrimEnd('/'),
            apiKey = secret,
            maxTokens = 4096,
            contextWindowTokens = 4096,
            maxOutputTokens = 1024,
            usages = new[] { "Chat", "Routing" },
            temperature = 0.2
        });

        try
        {
            await using var dbContext = await CreateAiGatewayDbContextAsync();
            var stored = await dbContext.LanguageModels.SingleAsync(item => item.Id == model.Id);

            stored.ApiKey.Should().StartWith(SecretStringEncryptor.CipherPrefix);
            stored.ApiKey.Should().NotContain(secret);
            SecretStringEncryptor.Decrypt(stored.ApiKey).Should().Be(secret);

            var testResult = await PostJsonAsync<LanguageModelTestResultDto>(
                "/api/aigateway/language-model/test",
                new
                {
                    id = model.Id,
                    persistResult = false
                });

            testResult.Success.Should().BeTrue();
            testResult.Status.Should().Be("Succeeded");
        }
        finally
        {
            await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/language-model", new { id = model.Id }, HttpStatusCode.NoContent);
        }
    }

    [Fact]
    public async Task EmbeddingModelApiKey_ShouldBeProtectedAtRest_AndNullUpdateShouldPreserveCipher()
    {
        await AuthenticateAsAdminAsync();
        var secret = $"sk-model-secret-embedding-{Guid.NewGuid():N}";
        var model = await PostJsonAsync<CreatedEmbeddingModelDto>("/api/rag/embedding-model", new
        {
            name = $"model-secret-embedding-{Guid.NewGuid():N}",
            provider = "OpenAI",
            baseUrl = new Uri(_fixture.FakeAiBaseUri, "/v1").ToString().TrimEnd('/'),
            apiKey = secret,
            modelName = "fake-embedding-model",
            dimensions = 4,
            maxTokens = 256,
            isEnabled = true
        });

        try
        {
            await using var dbContext = await CreateRagDbContextAsync();
            var stored = await dbContext.EmbeddingModels.SingleAsync(item => item.Id == model.Id);

            stored.ApiKey.Should().StartWith(SecretStringEncryptor.CipherPrefix);
            stored.ApiKey.Should().NotContain(secret);
            SecretStringEncryptor.Decrypt(stored.ApiKey).Should().Be(secret);
            var originalCipher = stored.ApiKey;

            await SendJsonAsync(HttpMethod.Put, "/api/rag/embedding-model", new
            {
                id = model.Id,
                name = "model-secret-embedding-preserve",
                provider = "OpenAI",
                baseUrl = new Uri(_fixture.FakeAiBaseUri, "/v1").ToString().TrimEnd('/'),
                apiKey = (string?)null,
                modelName = "fake-embedding-model",
                dimensions = 4,
                maxTokens = 256,
                isEnabled = true
            }, HttpStatusCode.NoContent);

            await dbContext.Entry(stored).ReloadAsync();
            stored.ApiKey.Should().Be(originalCipher);

            var updatedSecret = $"sk-model-secret-embedding-updated-{Guid.NewGuid():N}";
            await SendJsonAsync(HttpMethod.Put, "/api/rag/embedding-model", new
            {
                id = model.Id,
                name = "model-secret-embedding-replaced",
                provider = "OpenAI",
                baseUrl = new Uri(_fixture.FakeAiBaseUri, "/v1").ToString().TrimEnd('/'),
                apiKey = updatedSecret,
                modelName = "fake-embedding-model-updated",
                dimensions = 4,
                maxTokens = 256,
                isEnabled = true
            }, HttpStatusCode.NoContent);

            await dbContext.Entry(stored).ReloadAsync();
            stored.ApiKey.Should().StartWith(SecretStringEncryptor.CipherPrefix);
            stored.ApiKey.Should().NotBe(originalCipher);
            stored.ApiKey.Should().NotContain(updatedSecret);
            SecretStringEncryptor.Decrypt(stored.ApiKey).Should().Be(updatedSecret);
        }
        finally
        {
            await SendJsonAsync(HttpMethod.Delete, "/api/rag/embedding-model", new { id = model.Id }, HttpStatusCode.NoContent);
        }
    }

    private async Task AuthenticateAsAdminAsync()
    {
        _fixture.ClearAuthToken();
        var result = await PostJsonAsync<LoginUserDto>("/api/identity/login", new
        {
            username = _fixture.BootstrapAdminUserName,
            password = _fixture.BootstrapAdminPassword
        });
        _fixture.SetAuthToken(result.Token);
    }

    private async Task<AiGatewayDbContext> CreateAiGatewayDbContextAsync()
    {
        var connectionString = await _fixture.GetConnectionStringAsync();
        var options = new DbContextOptionsBuilder<AiGatewayDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AiGatewayDbContext(options);
    }

    private async Task<RagDbContext> CreateRagDbContextAsync()
    {
        var connectionString = await _fixture.GetConnectionStringAsync();
        var options = new DbContextOptionsBuilder<RagDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new RagDbContext(options);
    }

    private async Task<T> PostJsonAsync<T>(string uri, object body)
    {
        using var response = await _fixture.HttpClient.PostAsJsonAsync(uri, body);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    private async Task SendJsonAsync(HttpMethod method, string uri, object body, HttpStatusCode expectedStatusCode = HttpStatusCode.OK)
    {
        using var request = new HttpRequestMessage(method, uri)
        {
            Content = JsonContent.Create(body)
        };

        using var response = await _fixture.HttpClient.SendAsync(request);
        response.StatusCode.Should().Be(expectedStatusCode, await response.Content.ReadAsStringAsync());
    }

    private sealed record LoginUserDto(string Token);

    private sealed record CreatedLanguageModelDto(Guid Id, string Provider, string Name);

    private sealed record CreatedEmbeddingModelDto(Guid Id, string Name);

    private sealed record LanguageModelTestResultDto(
        bool Success,
        string Status,
        string Message,
        string? Error,
        long ElapsedMilliseconds,
        DateTimeOffset CheckedAt);
}
