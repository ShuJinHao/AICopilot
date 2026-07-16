using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.SharedKernel.Ai;

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

    [Fact]
    public async Task ServerMutationEndpoints_ShouldRejectMissingSecurityMetadataBeforePersistenceOrAudit()
    {
        await AuthenticateAsAdminAsync();

        foreach (var missingProperty in new[] { "externalSystemType", "capabilityKind" })
        {
            var rejectedName = $"missing-create-{missingProperty}-{Guid.NewGuid():N}";
            var payload = CreateServerMutationPayload(rejectedName);
            payload.Remove(missingProperty);

            using var response = await SendJsonAsync(HttpMethod.Post, "/api/mcp/server", payload);

            await AssertBadRequestProblemAsync(response, missingProperty);
            var servers = await GetJsonAsync<List<McpServerListItemDto>>("/api/mcp/server/list");
            servers.Should().NotContain(server => server.Name == rejectedName);
        }

        var serverName = $"missing-update-metadata-{Guid.NewGuid():N}";
        var created = await PostJsonAsync<CreatedMcpServerDto>(
            "/api/mcp/server",
            CreateServerMutationPayload(serverName));
        try
        {
            var originalServer = await GetBodyAsync($"/api/mcp/server?id={created.Id}");
            var auditPath =
                $"/api/identity/audit-log/list?page=1&pageSize=20&actionGroup=Config&actionCode=Mcp.UpdateServer&targetName={Uri.EscapeDataString(serverName)}";
            var auditBefore = await GetJsonAsync<AuditLogListDto>(auditPath);

            foreach (var missingProperty in new[] { "externalSystemType", "capabilityKind" })
            {
                var payload = CreateServerMutationPayload(serverName, created.Id);
                payload["description"] = $"must-not-apply-{missingProperty}";
                payload.Remove(missingProperty);

                using var response = await SendJsonAsync(HttpMethod.Put, "/api/mcp/server", payload);

                await AssertBadRequestProblemAsync(response, missingProperty);
                (await GetBodyAsync($"/api/mcp/server?id={created.Id}"))
                    .Should().Be(originalServer);
                var auditAfter = await GetJsonAsync<AuditLogListDto>(auditPath);
                auditAfter.TotalCount.Should().Be(auditBefore.TotalCount);
            }
        }
        finally
        {
            using var deleteResponse = await SendJsonAsync(
                HttpMethod.Delete,
                "/api/mcp/server",
                new Dictionary<string, object?> { ["id"] = created.Id });
            deleteResponse.IsSuccessStatusCode.Should().BeTrue();
        }
    }

    private static Dictionary<string, object?> CreateServerMutationPayload(
        string name,
        Guid? id = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["description"] = "HTTP security metadata contract",
            ["transportType"] = McpTransportType.Stdio,
            ["command"] = "dotnet",
            ["arguments"] = "contract-server.dll",
            ["externalSystemType"] = AiToolExternalSystemType.CloudReadOnly,
            ["capabilityKind"] = AiToolCapabilityKind.ReadOnlyQuery,
            ["chatExposureMode"] = ChatExposureMode.Advisory,
            ["allowedTools"] = new[]
            {
                new { toolName = "QueryStatus", readOnlyDeclared = true }
            },
            ["isEnabled"] = true,
            ["riskLevel"] = AiToolRiskLevel.RequiresApproval
        };
        if (id.HasValue)
        {
            payload["id"] = id.Value;
        }

        return payload;
    }

    private async Task<HttpResponseMessage> SendJsonAsync(
        HttpMethod method,
        string uri,
        object payload)
    {
        using var request = new HttpRequestMessage(method, uri)
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        return await _fixture.HttpClient.SendAsync(request);
    }

    private async Task<T> GetJsonAsync<T>(string uri)
    {
        using var response = await _fixture.HttpClient.GetAsync(uri);
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue($"GET '{uri}' failed: {body}");
        return JsonSerializer.Deserialize<T>(body, JsonOptions)!;
    }

    private async Task<string> GetBodyAsync(string uri)
    {
        using var response = await _fixture.HttpClient.GetAsync(uri);
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue($"GET '{uri}' failed: {body}");
        return body;
    }

    private static async Task AssertBadRequestProblemAsync(
        HttpResponseMessage response,
        string missingProperty)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().BeOneOf(
            "application/problem+json",
            "application/json");
        var body = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("status").GetInt32().Should().Be(400);
        problem.RootElement.GetProperty("title").GetString().Should().NotBeNullOrWhiteSpace();
        body.Should().Contain(missingProperty);
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

    private sealed record CreatedMcpServerDto(Guid Id, string Name);

    private sealed record McpServerListItemDto(Guid Id, string Name);

    private sealed record AuditLogListDto(int TotalCount);

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
