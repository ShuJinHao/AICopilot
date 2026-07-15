using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AICopilot.HttpIntegrationTests;

[Collection(CoreBackendTestCollection.Name)]
public sealed class McpToolGovernancePermissionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AICopilotAppFixture _fixture;

    public McpToolGovernancePermissionTests(CoreAICopilotAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ToolGovernanceEndpoint_ShouldRequireMcpAndToolRegistryReadPermissions()
    {
        await AuthenticateAsAdminAsync();
        using var adminResponse = await _fixture.HttpClient.GetAsync("/api/mcp/tool-governance");
        adminResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var mcpOnlyRole = await CreateRoleAsync(
            $"McpPermissionOnly-{Guid.NewGuid():N}",
            ["Mcp.GetListServers"]);
        var mcpOnlyUser = await CreateUserAsync($"mcp-permission-only-{Guid.NewGuid():N}", mcpOnlyRole.RoleName);
        await AuthenticateAsync(mcpOnlyUser.UserName, "Password123!");
        using var missingRegistryResponse = await _fixture.HttpClient.GetAsync("/api/mcp/tool-governance");
        missingRegistryResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var missingRegistryProblem = await ReadJsonAsync<ProblemDetailsDto>(missingRegistryResponse);
        missingRegistryProblem.MissingPermissions.Should().Contain("AiGateway.ToolRegistry.Read");

        await AuthenticateAsAdminAsync();
        var registryOnlyRole = await CreateRoleAsync(
            $"ToolRegistryPermissionOnly-{Guid.NewGuid():N}",
            ["AiGateway.ToolRegistry.Read"]);
        var registryOnlyUser = await CreateUserAsync($"tool-registry-permission-only-{Guid.NewGuid():N}", registryOnlyRole.RoleName);
        await AuthenticateAsync(registryOnlyUser.UserName, "Password123!");
        using var missingMcpResponse = await _fixture.HttpClient.GetAsync("/api/mcp/tool-governance");
        missingMcpResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var missingMcpProblem = await ReadJsonAsync<ProblemDetailsDto>(missingMcpResponse);
        missingMcpProblem.MissingPermissions.Should().Contain("Mcp.GetListServers");
    }

    private async Task<CreatedRoleDto> CreateRoleAsync(string roleName, IReadOnlyCollection<string> permissions)
    {
        return await PostJsonAsync<CreatedRoleDto>("/api/identity/role", new
        {
            roleName,
            permissions
        });
    }

    private async Task<CreatedUserDto> CreateUserAsync(string userName, string roleName)
    {
        return await PostJsonAsync<CreatedUserDto>("/api/identity/user", new
        {
            userName,
            password = "Password123!",
            roleName
        });
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

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    private sealed record LoginUserDto(string UserName, string Token);

    private sealed record CreatedRoleDto(
        string RoleId,
        string RoleName,
        IReadOnlyCollection<string> Permissions,
        bool IsSystemRole,
        int AssignedUserCount);

    private sealed record CreatedUserDto(
        string UserId,
        string UserName,
        string RoleName,
        bool IsEnabled,
        string Status);

    private sealed record ProblemDetailsDto(
        string? Title,
        string? Detail,
        int? Status,
        string? Code,
        IReadOnlyCollection<string> MissingPermissions);
}
