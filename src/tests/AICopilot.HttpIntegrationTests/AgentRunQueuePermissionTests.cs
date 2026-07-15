using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AICopilot.HttpIntegrationTests;

[Collection(CoreBackendTestCollection.Name)]
public sealed class AgentRunQueuePermissionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AICopilotAppFixture _fixture;

    public AgentRunQueuePermissionTests(CoreAICopilotAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RunQueueOperationsEndpoints_ShouldStayHiddenFromPublicChatApi()
    {
        await AuthenticateAsAdminAsync();
        using var adminSummary = await _fixture.HttpClient.GetAsync("/api/aigateway/agent/run-queue/summary");
        adminSummary.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var adminWorker = await _fixture.HttpClient.GetAsync("/api/aigateway/agent/worker/status");
        adminWorker.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var adminDeadLetter = await _fixture.HttpClient.PostAsJsonAsync(
            $"/api/aigateway/agent/run-queue/{Guid.NewGuid():D}/dead-letter",
            new { reason = "test" },
            JsonOptions);
        adminDeadLetter.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task AuthenticateAsAdminAsync()
    {
        await AuthenticateAsync(_fixture.BootstrapAdminUserName, _fixture.BootstrapAdminPassword);
    }

    private async Task AuthenticateAsync(string userName, string password)
    {
        var result = await PostJsonAsync<LoginUserDto>("/api/identity/login", new
        {
            username = userName,
            password
        });
        _fixture.SetAuthToken(result.Token);
    }

    private async Task<T> PostJsonAsync<T>(string uri, object payload)
    {
        using var response = await _fixture.HttpClient.PostAsJsonAsync(uri, payload, JsonOptions);
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue($"POST '{uri}' failed: {body}");
        return JsonSerializer.Deserialize<T>(body, JsonOptions)!;
    }

    private sealed record LoginUserDto(string UserName, string Token);
}
